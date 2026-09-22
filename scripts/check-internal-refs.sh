#!/usr/bin/env bash
# Fail when shipped files cite an internal document.
#
# WHY
#   `internals/` is excluded from the public push by .public-exclude, so an ADR
#   citation in src/ or docs/ is a pointer nobody outside this repo can follow.
#   Worse for src/: XML doc comments are packaged into the NuGet symbols, so the
#   dangling reference reaches every consumer's IDE tooltip.
#
#   The convention already existed — "ADRs are internal governance artifacts
#   only" — it simply had nothing enforcing it, and 90 references accumulated.
#
# WHAT COUNTS AS A CITATION
#   ADR-arch-030, ADR-019, adr-arch-035, or a path under internals/.
#   Bare "ADR" is deliberately allowed: "open an ADR before ..." is prose about
#   the practice, not a link into a directory the reader does not have.
#
# USAGE
#   scripts/check-internal-refs.sh            # whole tree — for CI
#   scripts/check-internal-refs.sh --staged   # staged blobs — for the pre-commit hook
#
# EXIT
#   0 clean, 1 citations found, 2 bad usage.

set -uo pipefail

mode="${1:-tree}"
case "$mode" in
    tree|--tree) mode=tree ;;
    --staged)    mode=staged ;;
    -h|--help)   sed -n '2,22p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *)           printf 'unknown argument: %s (expected --staged)\n' "$mode" >&2; exit 2 ;;
esac

cd "$(git rev-parse --show-toplevel)" || exit 2

# Skipped, each for a stated reason. Kept short on purpose: a long exclusion list
# is how a rule stops meaning anything.
#
#   internals/       the ADRs themselves; citing a sibling is the point
#   .claude/         repo-local agent instructions, which legitimately drive internals/
#   CLAUDE.md        the same instructions, at the repo root; they point sessions at internals/
#   .github/         repo automation — including the instruction *not* to mention internals/
#   scripts/         dev tooling that operates ON internals/ — including this file
#   releases/        published history. A shipped release note citing the ADR that
#                    authorised the change is a record, and rewriting it later is
#                    revisionism. Skipped by decision, not by oversight
#   .public-exclude  the exclusion list, which necessarily names internals/
#   push-public.sh   the script that applies it
is_skipped() {
    case "$1" in
        internals/*|.claude/*|CLAUDE.md|.github/*|scripts/*|releases/*|.public-exclude|push-public.sh) return 0 ;;
        *) return 1 ;;
    esac
}

# ADR-arch-030 / ADR-019 / adr-arch-035, or any path under internals/.
pattern='(^|[^[:alnum:]])[Aa][Dd][Rr]-([Aa][Rr][Cc][Hh]-)?[0-9]{3}|internals/'

if [ "$mode" = staged ]; then
    mapfile -t files < <(git diff --cached --name-only --diff-filter=ACM)
else
    mapfile -t files < <(git ls-files)
fi

found=0
for file in "${files[@]}"; do
    [ -n "$file" ] || continue
    is_skipped "$file" && continue

    # Text only. A binary blob matching the pattern by accident is not a citation.
    case "$file" in
        *.cs|*.md|*.csproj|*.slnx|*.json|*.yml|*.yaml|*.ps1|*.props|*.targets|*.txt) ;;
        *) continue ;;
    esac

    # Read the staged blob rather than the worktree: the commit is what ships,
    # and the two differ whenever a fix is written but not staged.
    if [ "$mode" = staged ]; then
        content="$(git show ":$file" 2>/dev/null)" || continue
    else
        content="$(cat "$file" 2>/dev/null)" || continue
    fi

    matches="$(printf '%s\n' "$content" | grep -nE "$pattern" || true)"
    if [ -n "$matches" ]; then
        if [ "$found" -eq 0 ]; then
            printf '\n  Internal document references in shipped files:\n\n' >&2
            found=1
        fi
        printf '%s\n' "$matches" | while IFS= read -r line; do
            printf '    %s:%s\n' "$file" "$line" >&2
        done
    fi
done

if [ "$found" -eq 0 ]; then
    [ "$mode" = tree ] && printf 'No internal document references in shipped files. [OK]\n'
    exit 0
fi

cat >&2 <<'MESSAGE'

  internals/ is excluded from the public push, so these point at documents the
  reader does not have — and in src/ they reach consumers through packaged XML docs.

  Keep the reason, drop the citation. "Refused because an unresolved reference
  must not resolve to a default" helps; "see ADR-arch-036 D1" does not.

  To commit anyway: git commit --no-verify
MESSAGE
exit 1
