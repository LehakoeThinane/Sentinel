CREATE TABLE ledger.accounts (
  account_id     UUID PRIMARY KEY,
  owner_ref      TEXT NOT NULL,                                  -- masked customer reference
  type           TEXT NOT NULL CHECK (type IN ('asset', 'liability', 'revenue', 'expense')),
  normal_side    CHAR(1) NOT NULL CHECK (normal_side IN ('D', 'C')),
  currency       CHAR(3) NOT NULL CHECK (currency ~ '^[A-Z]{3}$'),
  status         TEXT NOT NULL DEFAULT 'open' CHECK (status IN ('open', 'closed')),
  allow_negative BOOLEAN NOT NULL DEFAULT false,
  created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
  -- A wrong normal side would compute every balance with the wrong sign.
  CONSTRAINT accounts_normal_side_matches_type CHECK (
    (type IN ('asset', 'expense') AND normal_side = 'D') OR
    (type IN ('liability', 'revenue') AND normal_side = 'C')),
  -- Lets entries reference (account, currency) so a line can never be in a
  -- currency other than its account's.
  CONSTRAINT accounts_id_currency_key UNIQUE (account_id, currency)
);

-- Derived from entries and updated in the same transaction as each posting.
-- Measured in the account's normal direction.
CREATE TABLE ledger.account_balances (
  account_id    UUID PRIMARY KEY REFERENCES ledger.accounts,
  balance_minor BIGINT NOT NULL,
  updated_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Accounts and their opening balance rows are created by the owner (seed and
-- admin tasks). The runtime role reads accounts and updates balances only.
REVOKE ALL ON ledger.accounts, ledger.account_balances FROM $runtime_role$;
GRANT SELECT ON ledger.accounts TO $runtime_role$;
GRANT SELECT, UPDATE ON ledger.account_balances TO $runtime_role$;
