-- The ledger schema, owned by the role running migrations (ledger_owner).
-- The runtime role may use the schema; its table rights are granted per table.
-- IF NOT EXISTS: the migrator creates the schema first so DbUp can keep its
-- journal (ledger.schemaversions) inside it.
CREATE SCHEMA IF NOT EXISTS ledger;
REVOKE ALL ON SCHEMA ledger FROM PUBLIC;
GRANT USAGE ON SCHEMA ledger TO $runtime_role$;

-- Shared guard for insert-only tables: refuses UPDATE, DELETE and TRUNCATE for
-- every role, including the owner. Corrections are new rows, never edits.
CREATE FUNCTION ledger.refuse_change() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION '% on %.% is not allowed: rows are insert-only', TG_OP, TG_TABLE_SCHEMA, TG_TABLE_NAME;
END;
$$;
REVOKE ALL ON FUNCTION ledger.refuse_change() FROM PUBLIC;
