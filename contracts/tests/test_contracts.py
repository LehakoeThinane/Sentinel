"""Contract tests: every schema is valid, and examples prove what each one accepts and rejects.

Event examples live in contracts/events/examples/{valid,invalid}/ and are named
``<schema-stem>__<case>.json``; the stem selects the schema to validate against.
"""

import json
from pathlib import Path
from typing import Any

import pytest
import yaml
from jsonschema import Draft202012Validator, FormatChecker
from openapi_spec_validator import validate as validate_openapi
from referencing import Registry, Resource
from referencing.jsonschema import DRAFT202012

CONTRACTS = Path(__file__).resolve().parents[1]
EVENTS = CONTRACTS / "events"
EXAMPLES = EVENTS / "examples"
OPENAPI_FILES = sorted((CONTRACTS / "openapi").glob("*.yaml"))
SHARED_SCHEMAS = {"common.v1", "envelope.v1"}


def _load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def _stem(path: Path) -> str:
    return path.name.removesuffix(".schema.json")


SCHEMAS = {_stem(p): _load_json(p) for p in sorted(EVENTS.glob("*.schema.json"))}
EVENT_SCHEMAS = sorted(set(SCHEMAS) - SHARED_SCHEMAS)
REGISTRY = Registry().with_resources(
    (schema["$id"], Resource.from_contents(schema, default_specification=DRAFT202012))
    for schema in SCHEMAS.values()
)
VALID = sorted((EXAMPLES / "valid").glob("*.json"))
INVALID = sorted((EXAMPLES / "invalid").glob("*.json"))


def _event_validator(stem: str) -> Draft202012Validator:
    return Draft202012Validator(SCHEMAS[stem], registry=REGISTRY, format_checker=FormatChecker())


def _schema_for_example(path: Path) -> str:
    stem = path.name.split("__", 1)[0]
    assert stem in SCHEMAS, f"{path.name}: no schema named {stem}.schema.json"
    return stem


@pytest.mark.parametrize("stem", sorted(SCHEMAS))
def test_schema_is_valid_draft_2020_12(stem: str) -> None:
    Draft202012Validator.check_schema(SCHEMAS[stem])


@pytest.mark.parametrize("stem", EVENT_SCHEMAS)
def test_every_event_has_valid_and_invalid_examples(stem: str) -> None:
    assert any(p.name.startswith(f"{stem}__") for p in VALID), f"no valid example for {stem}"
    assert any(p.name.startswith(f"{stem}__") for p in INVALID), f"no invalid example for {stem}"


@pytest.mark.parametrize("path", VALID, ids=lambda p: p.name)
def test_valid_example_is_accepted(path: Path) -> None:
    event = _load_json(path)
    errors = list(_event_validator(_schema_for_example(path)).iter_errors(event))
    assert not errors, [e.message for e in errors]
    # Every event must also satisfy the bare envelope on its own.
    _event_validator("envelope.v1").validate(event)


@pytest.mark.parametrize("path", INVALID, ids=lambda p: p.name)
def test_invalid_example_is_rejected(path: Path) -> None:
    event = _load_json(path)
    assert not _event_validator(_schema_for_example(path)).is_valid(event)


def test_uuid_format_is_enforced() -> None:
    event = _load_json(EXAMPLES / "valid" / "payment-requested.v1__basic.json")
    event["event_id"] = "not-a-uuid"
    assert not _event_validator("payment-requested.v1").is_valid(event)


# --- OpenAPI -----------------------------------------------------------------


def _load_openapi(path: Path) -> dict[str, Any]:
    return yaml.safe_load(path.read_text(encoding="utf-8"))


def _embedded_examples(spec: dict[str, Any]) -> list[tuple[str, dict[str, Any], Any]]:
    """Collect (label, schema, example value) for every named example in the spec."""
    found = []
    for route, operations in spec["paths"].items():
        for method, op in operations.items():
            bodies = [("request", op.get("requestBody", {}))]
            bodies += [(status, resp) for status, resp in op.get("responses", {}).items()]
            for where, body in bodies:
                for media_type, media in body.get("content", {}).items():
                    for name, example in media.get("examples", {}).items():
                        label = f"{method.upper()} {route} {where} {media_type} {name}"
                        found.append((label, media["schema"], example["value"]))
    return found


@pytest.mark.parametrize("path", OPENAPI_FILES, ids=lambda p: p.name)
def test_openapi_document_is_valid(path: Path) -> None:
    validate_openapi(_load_openapi(path))


@pytest.mark.parametrize("path", OPENAPI_FILES, ids=lambda p: p.name)
def test_openapi_examples_match_their_schemas(path: Path) -> None:
    spec = _load_openapi(path)
    examples = _embedded_examples(spec)
    assert examples, f"{path.name} has no embedded examples"
    # Register the whole document so '#/components/...' references resolve.
    uri = path.as_uri()
    registry = Registry().with_resource(
        uri, Resource.from_contents(spec, default_specification=DRAFT202012)
    )
    for label, schema, value in examples:
        # Point at the schema through the registered document so nested refs keep their base.
        wrapper = {"$ref": uri + schema["$ref"]}
        validator = Draft202012Validator(wrapper, registry=registry, format_checker=FormatChecker())
        errors = list(validator.iter_errors(value))
        assert not errors, f"{label}: {[e.message for e in errors]}"
