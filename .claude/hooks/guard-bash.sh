#!/bin/bash
# Runs before every Bash and PowerShell tool call; both send .tool_input.command.
cmd=$(jq -r '.tool_input.command')

block() { echo "Blocked by Sentinel guard: $1" >&2; exit 2; }

[[ "$cmd" == *"--no-verify"* ]]      && block "do not skip pre-commit hooks"
[[ "$cmd" =~ (DROP|TRUNCATE)[[:space:]]+(TABLE|SCHEMA) ]] && block "no destructive SQL"
# Real env files (.env, .env.local, .env.production, ...) must not be read;
# .env.example is fine. Only a command segment that STARTS with a file reader
# is inspected, so text such as a commit message mentioning ".env" is not blocked.
readers='^(cat|less|more|head|tail|bat|type|gc|Get-Content|Select-String|sls)$'
while IFS= read -r seg; do
  read -r -a words <<< "$seg"
  [[ ${#words[@]} -gt 0 && "${words[0]}" =~ $readers ]] || continue
  for w in "${words[@]:1}"; do
    w="${w//[\"\']/}"; base="${w##*[/\\]}"
    [[ "$base" =~ ^\.env(\..+)?$ && "$base" != ".env.example" ]] && block "do not read .env files"
  done
done < <(printf '%s\n' "$cmd" | tr ';&|' '\n\n\n')
# main/master only as a whole ref (origin main, HEAD:main, +main, refs/heads/main),
# not as letters inside another branch name such as phase1/ledger-domain.
[[ "$cmd" =~ git[[:space:]]+push([[:space:]][^\;\&\|]*)?[[:space:]:+/](main|master)([[:space:]]|$|[\;\&\|]) ]] && block "push to a branch, not main"

# Force-push in any form: --force, --force-with-lease, --force-if-includes,
# -f alone or in a short-flag cluster (-uf), or a +refspec. Each line is split
# on ; & | so only the arguments of the push itself are inspected.
while IFS= read -r push; do
  [[ "$push" =~ [[:space:]]--force ]]                       && block "no force-push"
  [[ "$push" =~ [[:space:]]-[[:alpha:]]*f[[:alpha:]]*([[:space:]]|$) ]] && block "no force-push"
  [[ "$push" =~ [[:space:]]\+[^[:space:]] ]]                && block "no force-push (+refspec)"
done < <(printf '%s\n' "$cmd" | grep -oE '\bgit\b[^;&|]*[[:space:]]push\b[^;&|]*')
exit 0
