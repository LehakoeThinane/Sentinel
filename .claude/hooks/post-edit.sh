#!/bin/bash
f=$(jq -r '.tool_input.file_path')

case "$f" in
  *.cs) dotnet format Sentinel.sln --include "$f" >/dev/null 2>&1 ;;
  *.py) ruff format "$f" >/dev/null 2>&1 ;;
esac

if [[ "$f" =~ services/(ledger|payments-api)/.*\.cs$ ]] && \
   grep -nE '\b(double|float)\b' "$f" >&2; then
  echo "Money code must use long minor units, not double/float (ADR-005)." >&2
  exit 2
fi
exit 0
