-- One login role per service. Runs once, when the PostgreSQL data volume is
-- first initialised. Each role may create its own schema (through its
-- migrations) and has no access to any other service's schema.
--
-- Passwords come from the container environment. If one is missing, the
-- literal :'var' placeholder is left in the statement, which is a syntax error,
-- so initialisation fails instead of creating a role without a password.

\set ON_ERROR_STOP on

\getenv payments_pw PAYMENTS_SVC_PASSWORD
\getenv ledger_pw   LEDGER_SVC_PASSWORD
\getenv cases_pw    CASES_SVC_PASSWORD
\getenv recon_pw    RECON_SVC_PASSWORD

CREATE ROLE payments_svc LOGIN PASSWORD :'payments_pw';
CREATE ROLE ledger_svc   LOGIN PASSWORD :'ledger_pw';
CREATE ROLE cases_svc    LOGIN PASSWORD :'cases_pw';
CREATE ROLE recon_svc    LOGIN PASSWORD :'recon_pw';

-- Nobody connects or creates objects unless granted below.
REVOKE ALL ON DATABASE :"DBNAME" FROM PUBLIC;
REVOKE ALL ON SCHEMA public FROM PUBLIC;

-- CONNECT to the database, and CREATE so each service's migrations can create
-- (and therefore own) that service's schema. No cross-schema grants.
GRANT CONNECT, CREATE ON DATABASE :"DBNAME" TO payments_svc, ledger_svc, cases_svc, recon_svc;
