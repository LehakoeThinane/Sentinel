"""Kafka loop: consume PaymentRequested, publish the skeleton's PaymentScored.

Delivery is at-least-once: a message's offset is stored only after the reply
has been acknowledged by the broker. A crash in between means the request is
processed again, and the derived event id lets consumers drop the duplicate.
"""

import json
import logging
import os
import threading

from confluent_kafka import Consumer, KafkaError, KafkaException, Message, Producer

from scoring import events, logs

log = logging.getLogger("scoring.worker")


class ScoringWorker:
    def __init__(self, bootstrap_servers: str, topic: str, group_id: str) -> None:
        self._topic = topic
        self._stop = threading.Event()
        self._assigned = threading.Event()
        self._thread = threading.Thread(target=self._run, name="scoring-worker", daemon=True)
        self._consumer = Consumer(
            {
                "bootstrap.servers": bootstrap_servers,
                "group.id": group_id,
                "auto.offset.reset": "earliest",
                "enable.auto.commit": True,
                # Commit only offsets stored after a message is fully handled.
                "enable.auto.offset.store": False,
            }
        )
        self._producer = Producer(
            {
                "bootstrap.servers": bootstrap_servers,
                "acks": "all",
                "enable.idempotence": True,
                "linger.ms": 5,
            }
        )

    @classmethod
    def from_env(cls) -> "ScoringWorker":
        return cls(
            bootstrap_servers=os.environ.get("KAFKA_BOOTSTRAP_SERVERS", "127.0.0.1:19092"),
            topic=os.environ.get("PAYMENTS_TOPIC", "payments.events"),
            group_id=os.environ.get("KAFKA_GROUP_ID", "scoring"),
        )

    @property
    def ready(self) -> bool:
        return self._assigned.is_set() and self._thread.is_alive()

    def start(self) -> None:
        self._consumer.subscribe(
            [self._topic], on_assign=self._on_assign, on_revoke=self._on_revoke
        )
        self._thread.start()

    def stop(self, timeout: float = 10.0) -> None:
        self._stop.set()
        self._thread.join(timeout)

    def _on_assign(self, _consumer: Consumer, partitions: list) -> None:
        if partitions:
            self._assigned.set()
        log.info("partitions assigned", extra={"partitions": len(partitions)})

    def _on_revoke(self, _consumer: Consumer, _partitions: list) -> None:
        self._assigned.clear()

    def _run(self) -> None:
        try:
            while not self._stop.is_set():
                msg = self._consumer.poll(0.5)
                if msg is None:
                    continue
                if msg.error():
                    if msg.error().code() != KafkaError._PARTITION_EOF:
                        log.warning("kafka error", extra={"reason": str(msg.error())})
                    continue
                self._handle(msg)
                self._consumer.store_offsets(message=msg)
        except Exception:
            log.exception("worker stopped unexpectedly")
            raise
        finally:
            self._producer.flush(5)
            self._consumer.close()

    def _handle(self, msg: Message) -> None:
        try:
            requested = events.parse_envelope(msg.value())
        except events.MalformedEventError as exc:
            # Retrying cannot fix a malformed message; log it and move on.
            log.error("skipping malformed event", extra={"reason": str(exc)})
            return
        if requested.event_type != events.PAYMENT_REQUESTED:
            return  # Other event types on this topic are not for this service.

        token = logs.correlation_id.set(requested.correlation_id)
        try:
            scored = events.build_payment_scored(requested)
            self._publish(scored)
            log.info(
                "PaymentScored published",
                extra={"payment_id": requested.aggregate_id, "event_id": scored["event_id"]},
            )
        finally:
            logs.correlation_id.reset(token)

    def _publish(self, event: dict) -> None:
        """Publish and wait for the broker's acknowledgement; raise if it fails."""
        failure: list[KafkaError] = []

        def on_delivery(err: KafkaError | None, _msg: Message) -> None:
            if err is not None:
                failure.append(err)

        self._producer.produce(
            self._topic,
            key=event["aggregate_id"],  # Partition key: one payment's events stay in order.
            value=json.dumps(event).encode(),
            on_delivery=on_delivery,
        )
        remaining = self._producer.flush(10)
        if failure or remaining:
            raise KafkaException(failure[0] if failure else "delivery timed out")
