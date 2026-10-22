-- Statement queries page through one account's entries in time order (UUIDv7).
CREATE INDEX entries_account_statement_idx ON ledger.entries (account_id, entry_id);
-- Invariant checks and posting look-ups group entries by transaction.
CREATE INDEX entries_transaction_idx ON ledger.entries (transaction_id);
-- Audit trail for one entity, newest last.
CREATE INDEX audit_events_entity_idx ON ledger.audit_events (entity_type, entity_id, occurred_at);
