# Sentinel: rules for coding agents

## Non-negotiable invariants
- Money is BIGINT minor units + currency code. Never float or double.
- Ledger entries are insert-only. Corrections are reversing transactions.
- Every transaction balances: debits equal credits.
- POST /v1/payments requires an Idempotency-Key. Never remove that check.
- Services never read another service's database schema.
- The AI assistant can never approve, decline or move money.
- No real personal data anywhere: fixtures and tests use synthetic data.

## Privacy
- planning/ is private and gitignored. Read it for guidance only.
- Never stage, commit or force-add anything under planning/.
- Never copy or quote planning/ content into code, comments, commit
  messages, the README or any committed file.

## How to work
- Read planning/prd.md, planning/lld.md and planning/adr/ before
  changing a module, plus the current phase pack in planning/phase-packs/.
- Plan first for anything touching the ledger, payments or contracts.
- Write or update tests in the same change. Ledger changes need an
  invariant test (balances, no double posting).
- Never delete, skip or weaken a failing test to make CI pass. Report it.
- Never edit a migration that has been merged; add a new one.
- An architectural change needs a new or updated ADR in planning/adr/.
- Keep changes small: one concern per branch.

## Commands
- Start dependencies: docker compose -f infra/local/docker-compose.yml up -d
- All tests: make test    Lint: make lint

## Never
- Read or print .env files or secrets.
- Run terraform apply/destroy or Azure CLI commands.
- Force-push, rewrite history, force-add files, or commit with --no-verify.
