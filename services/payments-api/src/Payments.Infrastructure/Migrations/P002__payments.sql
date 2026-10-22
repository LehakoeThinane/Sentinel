CREATE TABLE payments.payments (
  payment_id       UUID PRIMARY KEY,
  client_id        TEXT NOT NULL,                                -- X-Client-Id; a client only ever sees its own payments
  payer_account_id UUID NOT NULL,
  payee_account_id UUID NOT NULL,
  amount_minor     BIGINT NOT NULL CHECK (amount_minor > 0),
  currency         CHAR(3) NOT NULL CHECK (currency ~ '^[A-Z]{3}$'),
  channel          TEXT NOT NULL,
  device_id        TEXT,
  -- All states from the design; this phase uses Requested, Approved, Declined and Posted.
  state            TEXT NOT NULL CHECK (state IN ('Requested', 'Scored', 'Approved', 'Held', 'Declined', 'Posted')),
  decline_reason   TEXT,
  ledger_txn_id    UUID,
  correlation_id   TEXT NOT NULL,
  created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT payments_distinct_accounts CHECK (payer_account_id <> payee_account_id),
  -- A posted payment always names its ledger transaction, and only a posted one does.
  CONSTRAINT payments_posted_has_ledger_txn CHECK ((state = 'Posted') = (ledger_txn_id IS NOT NULL)),
  -- A declined payment always says why, and only a declined one does.
  CONSTRAINT payments_declined_has_reason CHECK ((state = 'Declined') = (decline_reason IS NOT NULL))
);

-- Payments change state, so the runtime role may update them, but never delete.
REVOKE ALL ON payments.payments FROM $runtime_role$;
GRANT SELECT, INSERT, UPDATE ON payments.payments TO $runtime_role$;
