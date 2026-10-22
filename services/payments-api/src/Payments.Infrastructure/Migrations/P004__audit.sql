-- Who or what changed each payment, written in the same transaction as the change.
CREATE TABLE payments.audit_events (
  audit_id       UUID PRIMARY KEY,
  occurred_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
  actor_type     TEXT NOT NULL CHECK (actor_type IN ('system', 'user', 'model')),
  actor_id       TEXT NOT NULL,
  action         TEXT NOT NULL,                                  -- e.g. payment.state_changed
  entity_type    TEXT NOT NULL,
  entity_id      UUID NOT NULL,
  before_state   JSONB,
  after_state    JSONB,
  correlation_id TEXT NOT NULL
);

CREATE TRIGGER audit_events_insert_only BEFORE UPDATE OR DELETE ON payments.audit_events
  FOR EACH ROW EXECUTE FUNCTION payments.refuse_change();
CREATE TRIGGER audit_events_no_truncate BEFORE TRUNCATE ON payments.audit_events
  FOR EACH STATEMENT EXECUTE FUNCTION payments.refuse_change();

REVOKE ALL ON payments.audit_events FROM $runtime_role$;
GRANT SELECT, INSERT ON payments.audit_events TO $runtime_role$;
