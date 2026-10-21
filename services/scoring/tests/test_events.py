"""Tests for scoring.events: parsing requests and building the skeleton's reply."""

import json
from datetime import UTC, datetime
from pathlib import Path

import pytest
from jsonschema import Draft202012Validator, FormatChecker
from referencing import Registry, Resource
from referencing.jsonschema import DRAFT202012

from scoring import events

EVENTS_DIR = Path(__file__).resolve().parents[3] / "contracts" / "events"
REQUEST_EXAMPLE = EVENTS_DIR / "examples" / "valid" / "payment-requested.v1__basic.json"


def _validator(stem: str) -> Draft202012Validator:
    schemas = [json.loads(p.read_text(encoding="utf-8")) for p in EVENTS_DIR.glob("*.schema.json")]
    registry = Registry().with_resources(
        (s["$id"], Resource.from_contents(s, default_specification=DRAFT202012)) for s in schemas
    )
    schema = json.loads((EVENTS_DIR / f"{stem}.schema.json").read_text(encoding="utf-8"))
    return Draft202012Validator(schema, registry=registry, format_checker=FormatChecker())


@pytest.fixture
def requested() -> events.Envelope:
    return events.parse_envelope(REQUEST_EXAMPLE.read_bytes())


def test_reply_satisfies_the_payment_scored_contract(requested: events.Envelope) -> None:
    scored = events.build_payment_scored(requested)

    errors = list(_validator("payment-scored.v1").iter_errors(scored))
    assert not errors, [e.message for e in errors]


def test_reply_keeps_correlation_and_aggregate_ids(requested: events.Envelope) -> None:
    scored = events.build_payment_scored(requested)

    assert scored["correlation_id"] == requested.correlation_id
    assert scored["aggregate_id"] == requested.aggregate_id
    assert scored["payload"] == {"risk_score": 0.01, "model_version": "skeleton"}


def test_redelivered_request_yields_identical_event_id(requested: events.Envelope) -> None:
    first = events.build_payment_scored(requested, now=datetime(2026, 10, 21, 9, 30, tzinfo=UTC))
    again = events.build_payment_scored(requested, now=datetime(2026, 10, 21, 9, 45, tzinfo=UTC))

    assert first["event_id"] == again["event_id"]
    assert first["event_id"] != requested.event_id


def test_occurred_at_is_utc_with_z_suffix(requested: events.Envelope) -> None:
    scored = events.build_payment_scored(requested, now=datetime(2026, 10, 21, 9, 30, tzinfo=UTC))

    assert scored["occurred_at"] == "2026-10-21T09:30:00.000Z"


def test_only_payment_requested_can_be_scored(requested: events.Envelope) -> None:
    other = events.Envelope(**{**requested.__dict__, "event_type": "PaymentPosted"})

    with pytest.raises(ValueError, match="expected PaymentRequested"):
        events.build_payment_scored(other)


def _mutated(**changes: object) -> bytes:
    """The valid example with fields replaced (or removed when the value is ...)."""
    event = json.loads(REQUEST_EXAMPLE.read_text(encoding="utf-8"))
    for key, value in changes.items():
        if value is ...:
            del event[key]
        else:
            event[key] = value
    return json.dumps(event).encode()


@pytest.mark.parametrize(
    ("because", "raw"),
    [
        ("not json", b"{not json"),
        ("not an object", b"[1, 2]"),
        ("invalid utf-8", b"\xff\xfe"),
        ("missing correlation id", _mutated(correlation_id=...)),
        ("empty correlation id", _mutated(correlation_id="")),
        ("event id not a uuid", _mutated(event_id="x")),
        ("aggregate id missing", _mutated(aggregate_id=...)),
        ("payload not an object", _mutated(payload=[])),
    ],
)
def test_malformed_messages_are_rejected(because: str, raw: bytes) -> None:
    with pytest.raises(events.MalformedEventError):
        events.parse_envelope(raw)
