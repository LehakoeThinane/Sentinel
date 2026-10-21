# Developer entry points. Python tools (ruff, pytest) come from the active
# environment: `uv venv --python 3.12 && uv pip install -r requirements-dev.txt`.

COMPOSE := docker compose -f infra/local/docker-compose.yml
PY_DIRS := contracts services/scoring services/cases-assistant ml

.PHONY: test lint format up down

test:
	bash tests/guardrails/test-guard.sh
	dotnet test Sentinel.sln
	@# pytest exits 5 when it collects no tests; allowed until every package has tests.
	pytest $(PY_DIRS) -q; rc=$$?; [ $$rc -eq 0 ] || [ $$rc -eq 5 ]

lint:
	ruff check .
	ruff format --check .
	dotnet format Sentinel.sln --verify-no-changes

format:
	ruff format .
	dotnet format Sentinel.sln

up:
	$(COMPOSE) up -d --build --wait

down:
	$(COMPOSE) down
