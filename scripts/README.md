# scripts/

Local developer scripts.

| Script | Shell | When |
|---|---|---|
| [`install-hooks.sh`](#install-hookssh--git-hook-setup) | `bash` | **Once, after cloning** |
| [`check-docs.ps1`](#check-docsps1--documentation-drift-check) | `pwsh` | Before opening a pull request |
| [`fix-encoding.ps1`](#fix-encodingps1--utf-8-bom-check) | `pwsh` | Before opening a pull request |

The two PR gates are PowerShell 7 and run cross-platform. The dev environment is **WSL Ubuntu
26.04**, so install `pwsh` there: `sudo snap install powershell --classic` — there is no
`powershell` package in the 26.04 archive, so `apt install` will not find it. CI runs them on
`ubuntu-latest`, where `pwsh` is preinstalled. `install-hooks.sh` is plain bash on purpose: it is
the first thing run in a new clone, so it must not depend on `pwsh` already being there.

## `install-hooks.sh` — git hook setup

Points this clone's git at [`git-hooks/`](./git-hooks/):

```bash
bash scripts/install-hooks.sh
```

**Why a script for one `git config` line.** The hook scripts are committed, but
`core.hooksPath` — the setting that makes git run them — lives in `.git/config`, which is per-clone
and never travels with a fetch. A fresh clone therefore has the hooks sitting on disk and executes
none of them, **silently**. Nothing about the working tree looks wrong. The script also restores the
execute bit, which a clone across a mode-dropping filesystem loses just as quietly.

Safe to re-run. It refuses rather than clobbering if `core.hooksPath` already points somewhere else.

### What the hooks do

`post-commit`, `post-merge` and `post-checkout` re-sync the codegraph index so it cannot drift out of
step with the tree — a stale index answers confidently about code that no longer exists. All three
delegate to a shared `_codegraph-sync` body, which **never fails the git operation** (every path
exits `0`; errors go to `.codegraph/hook-errors.log`) and **stays silent for contributors who do not
have codegraph installed**. Full rationale in
[`internals/tooling-setup.md` §5.2](../internals/tooling-setup.md).

Verify the wiring:

```bash
git config --get core.hooksPath     # -> scripts/git-hooks
```

## `check-docs.ps1` — documentation drift check

Catches **stale type/API names in Markdown** before they reach a PR. The LLM-facing
docs (`docs/`, per-project `README.md` / `ARCHITECTURE.md`) are only useful if their
type inventories match the code. When a type is renamed — e.g. `GoogleAgentModel` →
`GeminiAgentModel`, or `IJobMiddleware` → `ITransitionMiddleware` — the prose silently
goes stale and an AI assistant reading it is primed with false facts.

### What it does

1. Builds a set of every PascalCase identifier (and source file name) that appears
   anywhere under `src/**/*.cs`.
2. Scans inline `` `code` `` spans in Markdown for backtick-quoted identifiers.
3. Flags any that exist **nowhere** in the source — the rename/typo/removal failure mode.
4. Runs the same question **backwards**: every name in
   [`check-docs-required.txt`](./check-docs-required.txt) must appear somewhere under
   `docs/`, so a shipped type that no doc mentions is a gate failure.

Step 4 exists because steps 1–3 are blind to it. A stale name leaves a token to scan for;
a type that shipped undocumented leaves nothing at all, and the run comes back green — which
is how one iteration landed eleven public plan-tier types with no doc naming any of them.

Any external type the codebase actually uses (e.g. `HttpClient`, `QdrantClient`) appears
in source and passes automatically, so false positives are low. It is **not** a semantic
check — it does not verify signatures or namespaces, only that a name exists. It
complements `dotnet build` / `dotnet test`; it does not replace them.

### Run it

```powershell
pwsh -File scripts/check-docs.ps1
```

Exit code `0` = clean, `1` = drift found. Useful flags:

| Flag | Effect |
|---|---|
| `-ShowContext` | Print the offending line under each finding |
| `-IncludeCodeBlocks` | Also scan fenced ```` ```code``` ```` blocks (noisier; periodic deep audits) |
| `-IncludeExamples` | Also scan `src/demos/**` and `PLAN-*.md` (skipped by default) |
| `-Path <dirs>` | Override scan roots (default: `src`, `docs`) |
| `-RequiredFile <path>` | Override the required-names list (default: `scripts/check-docs-required.txt`) |

### When something is flagged

- **It's a renamed/typo'd Ananke symbol** → fix the doc (this is the point).
- **It's a genuine external library type, third-party product, MSBuild property, or a
  documented placeholder** → add it to [`check-docs-ignore.txt`](./check-docs-ignore.txt),
  with a comment. Prefer fixing the doc; only ignore when the name is truly not a code symbol.
- **It's a required name no doc mentions** → document it, or drop its line from
  [`check-docs-required.txt`](./check-docs-required.txt) if the type is gone. That list is
  the vocabulary a consumer needs to use a tier at all, not an inventory of every public
  type — a second copy of the API surface would be wrong on the first rename.

### Optional: enforce on every push

Once the baseline is green, wire it into a git pre-push hook:

```bash
# .git/hooks/pre-push  (chmod +x)
#!/bin/sh
pwsh scripts/check-docs.ps1 || {
  echo "Docs drift check failed. Fix the stale identifiers above or update scripts/check-docs-ignore.txt."
  exit 1
}
```

The same script drops into CI unchanged (it already returns a non-zero exit code).

## `fix-encoding.ps1` — UTF-8 BOM check

Strips UTF-8 byte-order marks from text files. A BOM breaks tooling that expects plain UTF-8, so
**CI runs this in `-Check` mode and fails the build on any finding**.

> **Why this still exists after the move off Windows.** It reads as a Windows-era patch, and it is
> not: **`dotnet new` emits BOMs on Linux too.** Measured on Ubuntu 26.04 / SDK 10.0.110 —
> `console`, `classlib` and `nunit` templates produced a BOM in **6 of 6** files (`Program.cs`,
> `Class1.cs`, `UnitTest1.cs` and all three `.csproj`). The reintroduction vector is the SDK's own
> templates, not the editor or the shell, so it did not leave with Windows.
>
> `dotnet format` also enforces `charset = utf-8` and *is* the better tool where it reaches — but it
> only sees compiled `.cs`. Verified: it fixes `Program.cs`, leaves the `.csproj` BOM in place, and
> does not see `.md`/`.json` at all. Against this repo's original cleanup (173 files: 11 `.cs`,
> 147 `.md`, 7 `.yml`, 3 `.json`, 5 `.csproj`) it would have caught 11. Markdown was 85% of it.
>
> The *other* half of the original complaint — mojibake — is gone and is not what this script does.
> A scan of every tracked file for the usual signatures (`Ã©`, `â€™`, `Â `, literal `ï»¿`) returns
> zero hits.

### Run it

```powershell
pwsh -File scripts/fix-encoding.ps1 -Check   # report only, exit 1 on findings
pwsh -File scripts/fix-encoding.ps1          # strip the BOMs in place
```

> **`pwsh` is required** — the script uses a PowerShell 7 ternary (`$Check ? … : …`).

### What it looks at

The file list comes from **git**, not a directory walk:

```
git ls-files -z --cached --others --exclude-standard
```

Tracked files, plus new files not yet added, minus anything `.gitignore` covers — exactly the set a
commit could contain. **Every text file type is in scope**; there is no extension allowlist. Files
that open with the BOM bytes but contain a NUL are reported as binary and left alone.

Exit code `0` = clean, `1` = BOMs found (in `-Check` mode). Without `-Check` it **rewrites files in
place** — run it on a clean working tree so the diff is reviewable.

> **Rewritten 2026-08-10, and it found four files the old version could not see.** The previous
> version walked directories with an extension allowlist and a skip-list regex, which failed twice:
>
> - **The skip-list was Windows-only.** It matched `\obj\`-style separators, so on Linux it excluded
>   nothing — after a build it reported **170 false positives** from NuGet-generated
>   `obj/*.nuget.g.props`/`.targets`, and without `-Check` it would have rewritten them. CI never
>   saw this because it runs the check *before* `dotnet restore`, when no `obj/` exists.
> - **The allowlist had holes.** `.html` was never in it and dotfiles have no extension, so
>   `docs/index.html` (the published docs landing page), two presentation slides, and
>   `.codegraph/.gitignore` all carried BOMs through every green run of the old gate. All four are
>   now fixed.
>
> Sourcing from `git` removes both failure modes structurally: gitignored build output cannot be
> enumerated, so there is no skip-list to get wrong, and nothing is filtered by extension.
