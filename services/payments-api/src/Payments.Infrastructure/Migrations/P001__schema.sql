-- The payments schema, owned by the role running migrations (payments_owner).
-- The runtime role may use the schema; its table rights are granted per table.
-- IF NOT EXISTS: the migrator creates the schema first so DbUp can keep its
-- journal (payments.schemaversions) inside it.
CREATE SCHEMA IF NOT EXISTS payments;
REVOKE ALL ON SCHEMA payments FROM PUBLIC;
GRANT USAGE ON SCHEMA payments TO $runtime_role$;

-- Shared guard for insert-only tables: refuses UPDATE, DELETE and TRUNCATE for
-- every role, including the owner.
CREATE FUNCTION payments.refuse_change() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION '% on %.% is not allowed: rows are insert-only', TG_OP, TG_TABLE_SCHEMA, TG_TABLE_NAME;
END;
$$;
REVOKE ALL ON FUNCTION payments.refuse_change() FROM PUBLIC;
