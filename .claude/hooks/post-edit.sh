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
  *.cs) dotnet format Sentinel.sln --include "$f" >/dev/null 2>&1 ;;
  *.py) ruff format "$f" >/dev/null 2>&1 ;;
esac

if [[ "$f" =~ (^|/)services/(ledger|payments-api)/.*\.cs$ ]] && \
   grep -nE '\b(double|float)\b' "$f" >&2; then
  echo "Money code must use long minor units, not double/float (ADR-005)." >&2
  exit 2
fi
exit 0
