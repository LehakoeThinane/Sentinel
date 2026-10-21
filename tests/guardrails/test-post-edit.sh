#!/bin/bash
# Tests for .claude/hooks/post-edit.sh: flags double/float in money code for
# every path style a tool may send, and formats edited C# files.
set -u
root="$(cd "$(dirname "$0")/../.." && pwd)"
hook="${POST_EDIT_HOOK:-$root/.claude/hooks/post-edit.sh}"  # override to test another version
probe_dir="services/payments-api/src/Payments.Api"
fail=0

run_hook() { # file_path -> exit code
  jq -n --arg f "$1" '{tool_input:{file_path:$f}}' |
    CLAUDE_PROJECT_DIR="$root" bash "$hook" >/dev/null 2>&1
}

check() { # expected actual label
  if [[ "$1" == "$2" ]]; then echo "ok    [$2] $3"; else echo "FAIL  want $1 got $2: $3"; fail=1; fi
}

win_path() { # repo-relative -> C:\...\file (or as-is where cygpath is unavailable)
  if command -v cygpath >/dev/null 2>&1; then cygpath -w "$root/$1"; else echo "$root/$1"; fi
}

cleanup() { rm -f "$root/$probe_dir/GuardProbe.cs" "$root/services/scoring/guard_probe.cs"; }
trap cleanup EXIT

# Money code with a double: must be flagged whatever the path style.
printf 'namespace Payments.Api;\n\ninternal static class GuardProbe\n{\n    public static double Amount;\n}\n' \
  > "$root/$probe_dir/GuardProbe.cs"
rel="$probe_dir/GuardProbe.cs"
win=$(win_path "$rel")
run_hook "$win";                 check 2 $? "double in money code, Windows path"
run_hook "${win,}";              check 2 $? "double in money code, Windows path, lower-case drive"
run_hook "$root/$rel";           check 2 $? "double in money code, Unix path"
run_hook "$rel";                 check 2 $? "double in money code, relative path"

# Same file with long: allowed.
printf 'namespace Payments.Api;\n\ninternal static class GuardProbe\n{\n    public static long AmountMinor;\n}\n' \
  > "$root/$probe_dir/GuardProbe.cs"
run_hook "$win";                 check 0 $? "long in money code"

# A double outside ledger/payments-api is not money code.
printf 'class P { double x; }\n' > "$root/services/scoring/guard_probe.cs"
run_hook "$(win_path services/scoring/guard_probe.cs)"; check 0 $? "double outside money code"

# Formatting: badly indented C# is rewritten by dotnet format.
printf 'namespace Payments.Api;\n\ninternal static class GuardProbe\n{\n      public static long AmountMinor;\n}\n' \
  > "$root/$probe_dir/GuardProbe.cs"
run_hook "$win"
if grep -q '^    public static long AmountMinor;' "$root/$probe_dir/GuardProbe.cs"; then
  echo "ok    [fmt] C# file formatted"
else
  echo "FAIL  C# file was not formatted"; fail=1
fi

[[ $fail -eq 0 ]] && echo "all post-edit tests passed" || echo "post-edit tests FAILED"
exit $fail
