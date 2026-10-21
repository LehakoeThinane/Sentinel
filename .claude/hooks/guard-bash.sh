#!/bin/bash
# Runs before every Bash and PowerShell tool call; both send .tool_input.command.
cmd=$(jq -r '.tool_input.command')

block() { echo "Blocked by Sentinel guard: $1" >&2; exit 2; }

[[ "$cmd" == *"--no-verify"* ]]      && block "do not skip pre-commit hooks"
[[ "$cmd" =~ (DROP|TRUNCATE)[[:space:]]+(TABLE|SCHEMA) ]] && block "no destructive SQL"
[[ "$cmd" =~ (cat|less|head|tail|type|gc|Get-Content).*\.env($|[[:space:]]) ]] && block "do not read .env files"
[[ "$cmd" =~ git[[:space:]]+push.*(main|master) ]] && block "push to a branch, not main"

# Force-push in any form: --force, --force-with-lease, --force-if-includes,
# -f alone or in a short-flag cluster (-uf), or a +refspec. Each line is split
# on ; & | so only the arguments of the push itself are inspected.
while IFS= read -r push; do
  [[ "$push" =~ [[:space:]]--force ]]                       && block "no force-push"
  [[ "$push" =~ [[:space:]]-[[:alpha:]]*f[[:alpha:]]*([[:space:]]|$) ]] && block "no force-push"
  [[ "$push" =~ [[:space:]]\+[^[:space:]] ]]                && block "no force-push (+refspec)"
done < <(printf '%s\n' "$cmd" | grep -oE '\bgit\b[^;&|]*[[:space:]]push\b[^;&|]*')
exit 0
