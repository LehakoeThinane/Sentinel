"""Event envelopes for the scoring service. Pure functions, no I/O.

PaymentScored exists only in the walking skeleton, to prove the event round
trip. Once scoring becomes a synchronous call from the Payments API, this event
and the code that builds it are removed.
"""

import json
import uuid
from dataclasses import dataclass
from datetime import UTC, datetime
from typing import Any

PAYMENT_REQUESTED = "PaymentRequested"
PAYMENT_SCORED = "PaymentScored"

SKELETON_RISK_SCORE = 0.01
SKELETON_MODEL_VERSION = "skeleton"

# Namespace for deriving PaymentScored ids from the request they answer.
_SCORED_NAMESPACE = uuid.UUID("6f0a2c7e-3d1b-4f8a-9c55-2e7b1d4a9f30")


class MalformedEventError(ValueError):
    """The message is not a usable event envelope. Retrying will not fix it."""


@dataclass(frozen=True)
class Envelope:
    event_id: str
    event_type: str
    aggregate_id: str
    correlation_id: str
    payload: dict[str, Any]


def parse_envelope(raw: bytes | str) -> Envelope:
    """Parse the fields this service relies on, rejecting anything unusable."""
    try:
        data = json.loads(raw)
    except (json.JSONDecodeError, UnicodeDecodeError) as exc:
        raise MalformedEventError(f"not JSON: {exc}") from exc
    if not isinstance(data, dict):
        raise MalformedEventError("envelope must be a JSON object")
    try:
        envelope = Envelope(
            event_id=str(uuid.UUID(data["event_id"])),
            event_type=_require_str(data, "event_type"),
            aggregate_id=str(uuid.UUID(data["aggregate_id"])),
            correlation_id=_require_str(data, "correlation_id"),
            payload=data["payload"],
        )
    except (KeyError, TypeError, ValueError) as exc:
        raise MalformedEventError(f"invalid envelope: {exc!r}") from exc
    if not isinstance(envelope.payload, dict):
        raise MalformedEventError("payload must be a JSON object")
    return envelope


def build_payment_scored(requested: Envelope, now: datetime | None = None) -> dict[str, Any]:
    """Build the skeleton's PaymentScored for a PaymentRequested.

    The event id is derived from the request's event id, so a redelivered request
    produces an identical event that downstream consumers can deduplicate.
    """
    if requested.event_type != PAYMENT_REQUESTED:
        raise ValueError(f"expected {PAYMENT_REQUESTED}, got {requested.event_type}")
    occurred = (now or datetime.now(UTC)).astimezone(UTC)
    return {
        "event_id": str(uuid.uuid5(_SCORED_NAMESPACE, requested.event_id)),
        "event_type": PAYMENT_SCORED,
        "schema_version": 1,
        "occurred_at": occurred.isoformat(timespec="milliseconds").replace("+00:00", "Z"),
        "aggregate_id": requested.aggregate_id,
        "correlation_id": requested.correlation_id,
        "payload": {
            "risk_score": SKELETON_RISK_SCORE,
            "model_version": SKELETON_MODEL_VERSION,
        },
    }


def _require_str(data: dict[str, Any], key: str) -> str:
    value = data[key]
    if not isinstance(value, str) or not value:
        raise ValueError(f"{key} must be a non-empty string")
    return value
