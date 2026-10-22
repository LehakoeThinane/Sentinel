-- Database roles, created once when the PostgreSQL data volume is first
-- initialised.
--
-- Ledger and payments each get two roles:
--   <service>_owner  owns the service's schema and runs its migrations;
--   <service>_svc    is the runtime role the service connects with. Migrations
--                    grant it only what it needs, so it can never alter tables
--                    or rewrite insert-only data such as ledger entries.
-- cases_svc and recon_svc are single roles until their services are built.
--
-- Passwords come from the container environment. If one is missing, the
-- literal :'var' placeholder is left in the statement, which is a syntax error,
-- so initialisation fails instead of creating a role without a password.

\set ON_ERROR_STOP on

\getenv ledger_owner_pw   LEDGER_OWNER_PASSWORD
\getenv ledger_pw         LEDGER_SVC_PASSWORD
\getenv payments_owner_pw PAYMENTS_OWNER_PASSWORD
\getenv payments_pw       PAYMENTS_SVC_PASSWORD
\getenv cases_pw          CASES_SVC_PASSWORD
\getenv recon_pw          RECON_SVC_PASSWORD

CREATE ROLE ledger_owner   LOGIN PASSWORD :'ledger_owner_pw';
CREATE ROLE ledger_svc     LOGIN PASSWORD :'ledger_pw';
CREATE ROLE payments_owner LOGIN PASSWORD :'payments_owner_pw';
CREATE ROLE payments_svc   LOGIN PASSWORD :'payments_pw';
CREATE ROLE cases_svc      LOGIN PASSWORD :'cases_pw';
CREATE ROLE recon_svc      LOGIN PASSWORD :'recon_pw';

-- Nobody connects or creates objects unless granted below.
REVOKE ALL ON DATABASE :"DBNAME" FROM PUBLIC;
REVOKE ALL ON SCHEMA public FROM PUBLIC;

-- Owners (and the not-yet-split roles) may create their own schema. Runtime
-- roles may only connect; their table rights come from their service's migrations.
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO ledger_owner, payments_owner, cases_svc, recon_svc;
GRANT CONNECT ON DATABASE :"DBNAME" TO ledger_svc, payments_svc;
