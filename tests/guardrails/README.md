# guardrails

Tests for the coding-agent hooks in `.claude/hooks/`; `make test` runs both.

- `test-guard.sh`: the pre-shell guard blocks force-pushes, pushes to main,
  `--no-verify`, destructive SQL and `.env` reads.
- `test-post-edit.sh`: the post-edit hook formats C# files and flags
  `double`/`float` in ledger and payments code, for Windows and Unix paths.
