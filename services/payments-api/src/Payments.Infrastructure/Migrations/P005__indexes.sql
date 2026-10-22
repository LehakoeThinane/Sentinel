-- A client's payments, newest first.
CREATE INDEX payments_client_created_idx ON payments.payments (client_id, created_at);
-- Expired-key cleanup scans by age.
CREATE INDEX idempotency_keys_created_idx ON payments.idempotency_keys (created_at);
-- Audit trail for one entity, newest last.
CREATE INDEX audit_events_entity_idx ON payments.audit_events (entity_type, entity_id, occurred_at);
