-- Who or what changed what, written in the same transaction as the change.
CREATE TABLE ledger.audit_events (
  audit_id       UUID PRIMARY KEY,
  occurred_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  actor_type     TEXT NOT NULL CHECK (actor_type IN ('system', 'user', 'model')),
  actor_id       TEXT NOT NULL,
  action         TEXT NOT NULL,                                  -- e.g. posting.created
  entity_type    TEXT NOT NULL,
  entity_id      UUID NOT NULL,
  before_state   JSONB,
  after_state    JSONB,
  correlation_id TEXT NOT NULL
);

CREATE TRIGGER audit_events_insert_only BEFORE UPDATE OR DELETE ON ledger.audit_events
  FOR EACH ROW EXECUTE FUNCTION ledger.refuse_change();
CREATE TRIGGER audit_events_no_truncate BEFORE TRUNCATE ON ledger.audit_events
  FOR EACH STATEMENT EXECUTE FUNCTION ledger.refuse_change();

REVOKE ALL ON ledger.audit_events FROM $runtime_role$;
GRANT SELECT, INSERT ON ledger.audit_events TO $runtime_role$;
