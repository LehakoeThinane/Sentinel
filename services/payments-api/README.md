# payments-api

C# / .NET service that accepts payment requests. It enforces idempotency,
tracks each payment through its state machine, applies risk thresholds and
publishes domain events through a transactional outbox.

Owns the `payments` schema in PostgreSQL.

## Current state: walking skeleton

`POST /v1/payments` requires `X-Client-Id` and `Idempotency-Key`, validates
the body against `contracts/openapi/payments.v1.yaml`, publishes
`PaymentRequested` to `payments.events` (keyed by payment id, waiting for the
broker's acknowledgement) and answers `202 Accepted`. Nothing is stored yet.
A background consumer logs the skeleton's `PaymentScored` replies.

Every response carries `X-Correlation-Id` (taken from the request or
generated), and every log line is JSON with `correlation_id` in its scope.

Tests: `dotnet test services/payments-api/tests/Payments.Api.Tests`.
