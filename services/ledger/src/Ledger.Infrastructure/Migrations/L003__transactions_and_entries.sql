CREATE TABLE ledger.transactions (
  transaction_id UUID PRIMARY KEY,
  payment_id     UUID UNIQUE,                                    -- one posting per payment: last line of defence
  kind           TEXT NOT NULL CHECK (kind IN ('payment', 'reversal', 'adjustment')),
  reverses_id    UUID REFERENCES ledger.transactions,
  currency       CHAR(3) NOT NULL CHECK (currency ~ '^[A-Z]{3}$'), -- one currency per transaction
  posted_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
  CONSTRAINT transactions_payment_has_payment_id CHECK (kind <> 'payment' OR payment_id IS NOT NULL),
  CONSTRAINT transactions_reversal_has_target CHECK ((kind = 'reversal') = (reverses_id IS NOT NULL)),
  CONSTRAINT transactions_id_currency_key UNIQUE (transaction_id, currency)
);

CREATE TABLE ledger.entries (
  entry_id       UUID PRIMARY KEY,                               -- UUIDv7: time-ordered, used as the statement cursor
  transaction_id UUID NOT NULL,
  account_id     UUID NOT NULL,
  direction      CHAR(1) NOT NULL CHECK (direction IN ('D', 'C')),
  amount_minor   BIGINT NOT NULL CHECK (amount_minor > 0),
  currency       CHAR(3) NOT NULL,
  -- The entry's currency must equal both its transaction's and its account's.
  CONSTRAINT entries_transaction_currency_fkey FOREIGN KEY (transaction_id, currency)
    REFERENCES ledger.transactions (transaction_id, currency),
  CONSTRAINT entries_account_currency_fkey FOREIGN KEY (account_id, currency)
    REFERENCES ledger.accounts (account_id, currency)
);

-- Insert-only: no role may change or remove posted rows.
CREATE TRIGGER transactions_insert_only BEFORE UPDATE OR DELETE ON ledger.transactions
  FOR EACH ROW EXECUTE FUNCTION ledger.refuse_change();
CREATE TRIGGER transactions_no_truncate BEFORE TRUNCATE ON ledger.transactions
  FOR EACH STATEMENT EXECUTE FUNCTION ledger.refuse_change();
CREATE TRIGGER entries_insert_only BEFORE UPDATE OR DELETE ON ledger.entries
  FOR EACH ROW EXECUTE FUNCTION ledger.refuse_change();
CREATE TRIGGER entries_no_truncate BEFORE TRUNCATE ON ledger.entries
  FOR EACH STATEMENT EXECUTE FUNCTION ledger.refuse_change();

-- The runtime role can read and append, nothing else.
REVOKE ALL ON ledger.transactions, ledger.entries FROM $runtime_role$;
GRANT SELECT, INSERT ON ledger.transactions, ledger.entries TO $runtime_role$;
