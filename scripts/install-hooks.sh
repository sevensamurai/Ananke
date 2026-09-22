#!/usr/bin/env bash
# Point this clone's git at scripts/git-hooks/.
#
# The hook scripts are committed, but the setting that makes git run them is not:
# core.hooksPath lives in .git/config, which is per-clone and never travels with
# a fetch. So a fresh clone has the hooks on disk and silently runs none of them.
# This script closes that gap; run it once after cloning.
#
# Safe to re-run — it is a single idempotent `git config` write.

set -euo pipefail

root="$(git rev-parse --show-toplevel)"
want="scripts/git-hooks"

current="$(git config --get core.hooksPath || true)"
if [ -n "$current" ] && [ "$current" != "$want" ]; then
    printf 'core.hooksPath is already set to %s.\n' "$current" >&2
    printf 'Refusing to overwrite. Set it to %s by hand if that is what you want.\n' "$want" >&2
    exit 1
fi

git config core.hooksPath "$want"
printf 'core.hooksPath -> %s\n' "$want"

# The hooks are useless without the execute bit, and a clone on a filesystem that
# drops modes (or a checkout through some Windows tooling) loses it silently.
for hook in "$root/$want"/*; do
    [ -x "$hook" ] || chmod +x "$hook"
done

printf 'Installed: %s\n' "$(cd "$root/$want" && echo *)"

if command -v codegraph >/dev/null 2>&1; then
    printf 'codegraph %s found — the index will re-sync after commit, merge and branch checkout.\n' \
        "$(codegraph --version)"
else
    printf 'codegraph is NOT installed — the hooks will no-op silently, which is by design.\n'
fi
