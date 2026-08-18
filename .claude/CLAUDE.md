# Dev environment
- **WSL Ubuntu 26.04 LTS.** Linux is now the reference platform; CI already runs `ubuntu-latest`.
- **.NET SDK 10.0.302 or newer is required and pinned by `global.json`.** Ubuntu's archive only
  carries 10.0.110, which **cannot build this repo** — `Ananke.Analyzers` references Roslyn 5.6, and
  10.0.110 hosts 5.0.26, so every project fails with `CS9057`. Install it *into the existing dotnet
  root* so it sits alongside the apt-managed SDK and needs **no `PATH` change**:
  ```bash
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  sudo bash /tmp/dotnet-install.sh --version 10.0.302 --install-dir /usr/lib/dotnet
  ```
  `/usr/bin/dotnet` already resolves there, SDKs coexist, and `global.json` selects the right one.
  Verify: `dotnet --list-sdks` shows both; `dotnet --version` → `10.0.302`.
  **Never put an SDK on `PATH` per-command to make a build work** — if `dotnet --version` disagrees
  with `global.json`, install the SDK properly rather than shimming around it.
- **`pwsh` is required** for the two gate scripts: `sudo snap install powershell --classic`. There
  is no `powershell` package in Ubuntu 26.04's archive, so `apt install` will not find it. Both
  scripts are PowerShell 7 and cross-platform; there is no Windows-only step left in this repo.
- Remember Linux is **case-sensitive**: a path that resolved on Windows may not here.

# Build & verify
- Build: `dotnet build src/Ananke.slnx --no-restore`
- Test: `dotnet test src/Ananke.slnx --no-build --logger "console;verbosity=normal"`
- Run both from the repo root; there is no project or solution file at the root, so a bare
  `dotnet build` fails with `MSB1003`.
- **Never `cd`.** The Bash tool's working directory persists across calls and already starts at the
  repo root, so `cd` is almost never needed. Use repo-relative paths (`src/Ananke.slnx`) or absolute
  ones instead. A `cd` inside a compound command raises a permission prompt for every new folder,
  and each "don't ask again" stores one more exact-match rule that never generalises.
- `--no-restore` silently skips projects that were never restored. After pulling a branch that adds
  a project, run `dotnet restore src/Ananke.slnx` first, or the new project neither builds nor runs
  its tests — and the build still reports success.
- **Never read a build result through a pipe** (`dotnet build | tail`) — the pipeline returns the
  exit code of the *last* command, so a failed build reads as success. Redirect to a file, or check
  `$?` on `dotnet` itself.

# Only before create a pull request
- Lint: `dotnet format --verify-no-changes`

# Architecture
- Pattern: Vertical Slice Architecture (Features/<FeatureName>/)

# C# conventions
- Use `TimeProvider` over `DateTime.Now` / `DateTime.UtcNow`
- Primary constructors for services (C# 12+)
- Records for DTOs and value objects
- `CancellationToken` on every async public method
- Collection expressions (`[.. list]`) over LINQ `Concat`

# Testing
- NUnit + Shouldly + NSubstitute
- Integration tests use `WebApplicationFactory<Program>`
- Test class naming: `<ClassName>Tests`; method naming: `<Method>_<Scenario>_<Expected>`

# Workflow
- `internals/STATUS.md` is the entry point: where we are, what's next, links to the in-flight
  tracker and ADRs, and the open decisions. Read it first when picking work up. Update it when a
  large piece of work lands or on request (e.g. before going offline) — not continuously, and never
  with detail that belongs in a tracker or an ADR. Kept under `internals/` (excluded from the public
  push by `.public-exclude`) rather than the repo root, deliberately — see `push-public.sh`.
- One iteration ≈ a couple of days to a week, and gets its own tracker in `internals/design/`
  (`<yyyyMMdd>-plan-<name>.md`). When the iteration turns over, start a new tracker and repoint
  `internals/STATUS.md`'s "In flight" table.
- Always `dotnet build` + `dotnet test` after a change set
- When docs (`docs/`, any `README.md` / `ARCHITECTURE.md`) are touched, run
  `pwsh -File scripts/check-docs.ps1` before opening a PR — it flags stale type/API names. See
  `scripts/README.md`.
- Open ADR in internals/design/ before proposing architectural changes
- Do not modify .csproj files without confirmation
- Never commit files with a UTF-8 BOM; CI runs `scripts/fix-encoding.ps1 -Check`. Locally:
  `pwsh -File scripts/fix-encoding.ps1 -Check`
- Provider model lineups (OpenAI/Anthropic/Google) change every few months — check each release
  whether `Models.cs` and both `ModelCatalog`s need new entries. See "Keeping the model catalog
  current" in `src/Ananke.Design/README.md`.
- Referencing a deprecated or retired model id (literal or constant) outside an annotated
  `#pragma warning disable ANNKE00x` block is a build error, not a warning — see
  `docs/reference/model-deprecations.md`.

# Code navigation
- Read `MAP.md` first to find which architecture doc / guide / source dir covers a concept
- Use codegraph_explore and codegraph_search before reading files
- Only fall back to grep/glob if codegraph returns no results
