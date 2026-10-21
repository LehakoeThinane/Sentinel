# payments-api

C# / .NET service that accepts payment requests. It enforces idempotency,
tracks each payment through its state machine, applies risk thresholds and
publishes domain events through a transactional outbox.

Owns the `payments` schema in PostgreSQL.
