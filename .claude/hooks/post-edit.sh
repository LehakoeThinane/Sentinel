#!/bin/bash
f=$(jq -r '.tool_input.file_path')

# Tools on Windows pass paths like C:\Users\...\X.cs. Normalise to a path
# relative to the project root with forward slashes, so the checks below match
# on every OS.
root="${CLAUDE_PROJECT_DIR:-$PWD}"
if command -v cygpath >/dev/null 2>&1; then
  f=$(cygpath -u "$f"); root=$(cygpath -u "$root")
fi
f="${f//\\//}"
cd "$root" || exit 0
f=$(realpath -m --relative-to="$root" "$f" 2>/dev/null || echo "$f")

case "$f" in
  # Whitespace only: the full `dotnet format` also applies analyzer code fixes,
  # which can change what code means (for example adding [Obsolete]). Style and
  # analyzer rules are still enforced by `make lint` and CI.
  *.cs) dotnet format whitespace Sentinel.sln --include "$f" >/dev/null 2>&1 ;;
  *.py) ruff format "$f" >/dev/null 2>&1 ;;
esac

# Flag double/float used in code. Comment lines are skipped, and "double-" is
# ignored, so ledger vocabulary such as "double-entry" is not a false positive.
if [[ "$f" =~ (^|/)services/(ledger|payments-api)/.*\.cs$ ]] && \
   grep -nvE '^[[:space:]]*(//|/?\*)' "$f" | grep -E '\b(double|float)\b([^-]|$)' >&2; then
  echo "Money code must use long minor units, not double/float (ADR-005)." >&2
  exit 2
fi
exit 0
