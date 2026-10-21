#!/bin/bash
cmd=$(jq -r '.tool_input.command')

block() { echo "Blocked by Sentinel guard: $1" >&2; exit 2; }

[[ "$cmd" == *"--no-verify"* ]]      && block "do not skip pre-commit hooks"
[[ "$cmd" =~ (DROP|TRUNCATE)[[:space:]]+(TABLE|SCHEMA) ]] && block "no destructive SQL"
[[ "$cmd" =~ (cat|less|head|tail).*\.env($|[[:space:]]) ]] && block "do not read .env files"
[[ "$cmd" =~ git[[:space:]]+push.*(main|master) ]] && block "push to a branch, not main"
exit 0
