CREATE TABLE payments.idempotency_keys (
  client_id     TEXT NOT NULL,
  key           TEXT NOT NULL,
  request_hash  TEXT NOT NULL,                                   -- SHA-256 of the canonical request body
  payment_id    UUID REFERENCES payments.payments,               -- set in the transaction that creates the payment
  status        TEXT NOT NULL CHECK (status IN ('in_progress', 'completed')),
  locked_until  TIMESTAMPTZ NOT NULL,                            -- lease: after this, a retry may take over
  response_code INT,
  response_body JSONB,
  created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  PRIMARY KEY (client_id, key),
  -- A completed key always carries the response it replays.
  CONSTRAINT idempotency_completed_has_response CHECK (
    status <> 'completed' OR (response_code IS NOT NULL AND response_body IS NOT NULL))
);

-- Keys are claimed, taken over and completed by the runtime role. Removing
-- expired keys is a separate maintenance task, so no DELETE here.
REVOKE ALL ON payments.idempotency_keys FROM $runtime_role$;
GRANT SELECT, INSERT, UPDATE ON payments.idempotency_keys TO $runtime_role$;
