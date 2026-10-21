# scoring

Python service that scores payments for fraud risk in real time. Returns a
probability, the model version and the top contributing features.

Reads online features from Redis.

## Current state: walking skeleton

The service consumes `PaymentRequested` from `payments.events` and replies
with a fixed `PaymentScored` (`risk_score: 0.01`, `model_version: "skeleton"`).
That event exists only to prove the event plumbing; it is replaced by a
synchronous scoring call when the real model arrives.

- `scoring/events.py`: pure envelope parsing and building (unit tested).
- `scoring/worker.py`: Kafka loop, at-least-once (offsets stored after the
  reply is acknowledged).
- `scoring/main.py`: FastAPI app with `/health/live` and `/health/ready`.

Run the tests from the repository root: `pytest services/scoring`.
