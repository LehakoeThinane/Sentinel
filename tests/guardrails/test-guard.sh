#!/bin/bash
# Table-driven tests for .claude/hooks/guard-bash.sh.
# Each case: expected exit code (2 = blocked, 0 = allowed) and the command.
set -u
root="$(cd "$(dirname "$0")/../.." && pwd)"
guard="$root/.claude/hooks/guard-bash.sh"

cases=(
  # Force-push forms
  "2|git push --force origin phase0/x"
  "2|git push origin phase0/x --force"
  "2|git push -f origin phase0/x"
  "2|git push origin phase0/x -f"
  "2|git push -uf origin phase0/x"
  "2|git push --force-with-lease origin phase0/x"
  "2|git push --force-with-lease=phase0/x:abc123 origin phase0/x"
  "2|git push --force-if-includes origin phase0/x"
  "2|git push origin +phase0/x"
  "2|git push origin +refs/heads/a:refs/heads/a"
  "2|git -C repo push --force origin phase0/x"
  "2|git status && git push -f origin phase0/x"
  "2|git status; git push origin +phase0/x"
  # Other guard rules
  "2|git push origin main"
  "2|git push -u origin master"
  "2|git commit --no-verify -m x"
  "2|psql -c \"DROP TABLE ledger.entries\""
  "2|psql -c \"TRUNCATE TABLE ledger.entries\""
  "2|cat .env"
  "2|Get-Content .env"
  "2|Get-Content -Path .env"
  "2|type .env"
  "2|gc .env"
  "2|cat ./infra/local/.env"
  "2|cat \"C:\\Users\\x\\Sentinel\\.env\""
  "2|tail -f .env.local"
  "2|less .env.production"
  "2|ls && cat .env"
  "2|echo hi; head -3 .env"
  # Allowed
  "0|git push -u origin phase0/foundation"
  "0|git push origin phase0/x --follow-tags"
  "0|git push --set-upstream origin phase0/x"
  "0|git status"
  "0|git status && rm -f build.log"
  "0|git log --oneline -5"
  "0|cat .env.example"
  "0|head -5 .env.example"
  "0|Get-Content .env.example"
  "0|git commit -m \"type checks keep local secrets and .env files out of images\""
  "0|grep -n KAFKA .gitignore"
  "0|cat README.md"
  "0|cp .env.example .env"
  "0|dotnet test Sentinel.sln"
)

fail=0
for c in "${cases[@]}"; do
  want="${c%%|*}"; cmd="${c#*|}"
  jq -n --arg c "$cmd" '{tool_input:{command:$c}}' | bash "$guard" >/dev/null 2>&1
  got=$?
  if [[ "$got" == "$want" ]]; then
    echo "ok    [$got] $cmd"
  else
    echo "FAIL  want $want got $got: $cmd"; fail=1
  fi
done
[[ $fail -eq 0 ]] && echo "all guard tests passed" || echo "guard tests FAILED"
exit $fail
