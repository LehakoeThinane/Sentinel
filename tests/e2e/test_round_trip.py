"""Walking-skeleton round trip against the running docker-compose stack.

POST /v1/payments -> PaymentRequested -> scoring -> PaymentScored -> payments-api.
Start the stack first: docker compose -f infra/local/docker-compose.yml up -d --wait
"""

import json
import os
import subprocess
import time
import urllib.error
import urllib.request
import uuid
from pathlib import Path

import pytest
from confluent_kafka import OFFSET_END, Consumer, TopicPartition
from jsonschema import Draft202012Validator, FormatChecker
from referencing import Registry, Resource
from referencing.jsonschema import DRAFT202012

ROOT = Path(__file__).resolve().parents[2]
API_URL = os.environ.get("SENTINEL_API_URL", "http://127.0.0.1:8080")
KAFKA = os.environ.get("KAFKA_BOOTSTRAP_SERVERS", "127.0.0.1:19092")
TOPIC = "payments.events"
COMPOSE = ["docker", "compose", "-f", str(ROOT / "infra" / "local" / "docker-compose.yml")]
DEADLINE_SECONDS = 5.0

PAYMENT = {
    "payer_account_id": "0192f3a4-0000-7000-8000-000000000001",
    "payee_account_id": "0192f3a4-0000-7000-8000-000000000900",
    "amount_minor": 25000,
    "currency": "ZAR",
    "channel": "app",
    "device_id": "dev-e2e",
}


def _validator(stem: str) -> Draft202012Validator:
    events_dir = ROOT / "contracts" / "events"
    schemas = [json.loads(p.read_text(encoding="utf-8")) for p in events_dir.glob("*.schema.json")]
    registry = Registry().with_resources(
        (s["$id"], Resource.from_contents(s, default_specification=DRAFT202012)) for s in schemas
    )
    schema = json.loads((events_dir / f"{stem}.schema.json").read_text(encoding="utf-8"))
    return Draft202012Validator(schema, registry=registry, format_checker=FormatChecker())


@pytest.fixture
def tail_consumer():
    """A consumer positioned at the current end of every partition, before we post.

    Explicit assignment at the high watermark avoids the race where a group
    consumer resolves its starting offset only after our events were written.
    """
    consumer = Consumer({"bootstrap.servers": KAFKA, "group.id": f"e2e-{uuid.uuid4()}"})
    partitions = consumer.list_topics(TOPIC, timeout=10).topics[TOPIC].partitions
    assignment = []
    for p in partitions:
        _low, high = consumer.get_watermark_offsets(TopicPartition(TOPIC, p), timeout=10)
        assignment.append(TopicPartition(TOPIC, p, high if high >= 0 else OFFSET_END))
    consumer.assign(assignment)
    yield consumer
    consumer.close()


def _post_payment(headers: dict[str, str]) -> tuple[int, dict[str, str], dict]:
    request = urllib.request.Request(
        f"{API_URL}/v1/payments",
        data=json.dumps(PAYMENT).encode(),
        headers={"Content-Type": "application/json", **headers},
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=10) as response:
            return response.status, dict(response.headers), json.loads(response.read())
    except urllib.error.HTTPError as err:
        return err.code, dict(err.headers), json.loads(err.read())


def _collect(consumer: Consumer, correlation_id: str, until: float) -> dict[str, tuple]:
    """Gather our events by type until PaymentScored arrives or the deadline passes."""
    found: dict[str, tuple] = {}
    while time.monotonic() < until and "PaymentScored" not in found:
        msg = consumer.poll(0.2)
        if msg is None or msg.error():
            continue
        event = json.loads(msg.value())
        if event.get("correlation_id") == correlation_id:
            found[event["event_type"]] = (msg.key().decode(), event)
    return found


def test_payment_is_scored_with_the_same_correlation_id(tail_consumer: Consumer) -> None:
    correlation_id = f"e2e-{uuid.uuid4()}"

    # The deadline covers the whole round trip, starting when the request is sent.
    posted_at = time.monotonic()
    status, headers, body = _post_payment(
        {
            "X-Client-Id": "e2e-client",
            "Idempotency-Key": str(uuid.uuid4()),
            "X-Correlation-Id": correlation_id,
        }
    )

    assert status == 202, body
    assert headers.get("X-Correlation-Id") == correlation_id
    payment_id = body["payment_id"]

    found = _collect(tail_consumer, correlation_id, until=posted_at + DEADLINE_SECONDS)
    elapsed = time.monotonic() - posted_at
    assert "PaymentScored" in found, f"no PaymentScored within {DEADLINE_SECONDS}s; saw {found}"
    assert "PaymentRequested" in found

    for event_type, stem in (
        ("PaymentRequested", "payment-requested.v1"),
        ("PaymentScored", "payment-scored.v1"),
    ):
        key, event = found[event_type]
        errors = list(_validator(stem).iter_errors(event))
        assert not errors, f"{event_type} breaks its contract: {[e.message for e in errors]}"
        assert event["aggregate_id"] == payment_id
        assert key == payment_id, f"{event_type} must be keyed by payment id"

    scored = found["PaymentScored"][1]["payload"]
    assert scored == {"risk_score": 0.01, "model_version": "skeleton"}
    print(f"round trip in {elapsed:.3f}s")


def test_payments_api_logs_the_scored_event(tail_consumer: Consumer) -> None:
    correlation_id = f"e2e-{uuid.uuid4()}"
    status, _, body = _post_payment(
        {
            "X-Client-Id": "e2e-client",
            "Idempotency-Key": str(uuid.uuid4()),
            "X-Correlation-Id": correlation_id,
        }
    )
    assert status == 202, body
    until = time.monotonic() + DEADLINE_SECONDS

    while time.monotonic() < until:
        logs = subprocess.run(  # noqa: S603 - fixed argument list, no shell
            [*COMPOSE, "logs", "payments-api", "--no-log-prefix", "--since", "2m"],
            capture_output=True,
            text=True,
            check=True,
        ).stdout
        if any(
            correlation_id in line and "PaymentScored received" in line
            for line in logs.splitlines()
        ):
            return
        time.sleep(0.25)
    pytest.fail(f"payments-api did not log PaymentScored for {correlation_id}")


def test_missing_idempotency_key_is_rejected_and_nothing_is_published(
    tail_consumer: Consumer,
) -> None:
    correlation_id = f"e2e-{uuid.uuid4()}"

    status, _, body = _post_payment(
        {"X-Client-Id": "e2e-client", "X-Correlation-Id": correlation_id}
    )

    assert status == 400
    assert body["code"] == "idempotency_key_missing"
    found = _collect(tail_consumer, correlation_id, until=time.monotonic() + 2.0)
    assert found == {}, f"a rejected request must not publish events; saw {found}"
