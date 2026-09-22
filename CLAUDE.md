# Ananke

C# .NET 10 library for AI agent orchestration. Solution: `src/Ananke.slnx`. Sessions run on WSL
Ubuntu 26.04 or native Ubuntu; CI runs `ubuntu-latest`.

This file and the skills in `.claude/skills/` are the only standing instructions. Anything that
must hold in every session goes here, not in auto-memory.

# Start here
- Read `internals/STATUS.md` first: what is in flight, what is next, and which tracker or ADR holds
  the detail. Then open that tracker — not the rest of `internals/design/`.
- Use `MAP.md` to find the doc, guide and source folder for a concept.
- Use `codegraph_explore` before reading files; fall back to `rg` only when it finds nothing.

# Environment
- **.NET SDK 10.0.302 or newer, pinned by `global.json`.** Ubuntu's apt SDK (10.0.110) cannot build
  this repo — `Ananke.Analyzers` needs Roslyn 5.6, so every project fails with `CS9057`. Install it
  next to the apt SDK, with no `PATH` change:
  ```bash
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  sudo bash /tmp/dotnet-install.sh --version 10.0.302 --install-dir /usr/lib/dotnet
  ```
  `dotnet --version` must print `10.0.302`. Never put an SDK on `PATH` for one command to get a
  build through — install it properly.
- **`pwsh`** for the gate scripts: `sudo snap install powershell --classic` (not in apt).
- **Once per clone: `bash scripts/install-hooks.sh`.** `git config --get core.hooksPath` must print
  `scripts/git-hooks`; without it the pre-commit check and the codegraph sync silently never run.
- **codegraph must be on `PATH` for non-interactive shells** — set it in `~/.profile`, not
  `~/.bashrc` — or the MCP server and the git hooks both fail without a word. Check:
  `bash -c 'command -v codegraph'`. Details: `internals/tooling-setup.md` §5.5.
- Search with `rg -t cs`. `grep` here is `ugrep`: it ignores `.gitignore` and reads stale `obj/` files.
- Linux paths are case-sensitive.

# Build and test
- Build: `dotnet build src/Ananke.slnx --no-restore`
- Test: `dotnet test src/Ananke.slnx --no-build --logger "console;verbosity=normal"`
- Run from the repo root, always with the solution path — there is no project file at the root, so
  a bare `dotnet build` fails with `MSB1003`.
- **Never `cd`.** The shell already starts at the repo root; use repo-relative paths.
- **Never read a build or test result through a pipe** (`dotnet build | tail`) — the pipe returns the
  last command's exit code, so a failure reads as success. Redirect to a file or check `$?`.
- `--no-restore` silently skips projects that were never restored. After pulling a branch that adds
  a project, run `dotnet restore src/Ananke.slnx` first.
- Build and test after every change set.

# Before a pull request
- `dotnet format src/Ananke.slnx --verify-no-changes`. To fix what it reports, prefer
  `dotnet format whitespace src/Ananke.slnx` — plain `dotnet format` also applies style fixes.
- `pwsh -File scripts/fix-encoding.ps1 -Check` — no file may have a UTF-8 BOM.
- If `docs/`, a `README.md` or an `ARCHITECTURE.md` changed: `pwsh -File scripts/check-docs.ps1`.

# Code
- Vertical slices: `Features/<FeatureName>/`.
- `TimeProvider`, not `DateTime.Now` / `UtcNow`. Primary constructors for services. Records for DTOs
  and value objects. `CancellationToken` on every public async method. Collection expressions
  (`[.. list]`) over LINQ `Concat`.
- Ask before changing any `.csproj`.
- **Before adding a mechanism, check what the framework already has** (`codegraph_explore`) and say
  in one line what you found. The plan code once grew three hand-written parsers while `AgentJob`
  already constrained model output to a JSON schema.
- **Comments say what the code does, and any constraint the code itself does not show.** No history
  ("used to", "this cost N runs"), no rule or item ids (`R27`, `S8`), no ADR ids or `internals/`
  paths. History goes in the commit or the tracker. ADR and `internals/` citations in `src/`,
  `docs/` and root Markdown fail `scripts/check-internal-refs.sh` (pre-commit hook and CI).
- **Use words already in the code or the domain.** No slogans or coined phrases in comments,
  READMEs or commit messages. A new term must exist as a type or member name before prose uses it.
- Provider model lineups change every few months: each release, check `Models.cs` and both
  `ModelCatalog`s ("Keeping the model catalog current" in `src/Ananke.Design/README.md`). A
  deprecated model id outside an annotated `#pragma warning disable ANNKE00x` block is a build
  error — see `docs/reference/model-deprecations.md`.

# Tests
- NUnit + Shouldly + NSubstitute; integration tests use `WebApplicationFactory<Program>`. Class
  `<ClassName>Tests`, method `<Method>_<Scenario>_<Expected>`.
- **Write the failing test first, then the fix.** If the fix changes a signature, land the new
  signature with the old behaviour, watch the test fail, then implement.
- Prove a test by reverting the fix only when it could pass by accident (timing, concurrency,
  "X did not happen"). Revert with `git stash` or a worktree, never by editing source by hand.
  Put a timeout (`WaitAsync`) on any assertion that could hang.
- Demos (`src/demos/**`) get no unit tests. They must build, and are checked by running them.

# Plans, trackers and ADRs (`internals/`)
- `internals/` is never pushed publicly (`.public-exclude`, `push-public.sh`).
- **`STATUS.md` is one screen.** It links to trackers and ADRs and repeats neither; one sentence
  per cell. Update it when a large piece of work lands or when asked, not continuously.
- **One tracker per iteration** (a few days to a week): `internals/design/<yyyyMMdd>-plan-<name>.md`,
  at most ~150 lines and ~5 steps, each step committable on its own. When the iteration ends, mark
  the tracker **Closed** in its header and take it out of STATUS's *In flight*.
- A tracker says what to do; an ADR says why. Open an ADR before an architectural change and keep
  it under ~250 lines.
- **Check every code-level instruction against the source before writing it into a plan.** A plan
  that is confidently wrong gets followed.
- **Ids carry their document** — `048·R50`, not a bare `R50`. Letters are reused across documents.
- **Record what was observed and what changed**, quoting the actual error or output. Do not reply
  to an earlier draft, an earlier session or the author inside the document.
- Many changes to one document: rewrite it with `Write` rather than a long run of `Edit`s.
- For design that will take several iterations to settle, use the `spike` skill.

# Demos
- **Before changing a demo, name the tracker step the change belongs to.** If no tracker says what
  the demo must show, stop and ask. Do not change the scenario to make a finding fit.

# Stop and tell the user
- when a document has been rewritten three times in this session;
- when the reason for a decision is text you wrote earlier, rather than a run, a test or the user;
- when tracked work needs a concept, term or mechanism its tracker does not mention.

# Commits and releases
- Commit subject: imperative, ~60 characters, says what changed ("Rename World to TripService").
  Body: 1–2 lines naming the tracker step. The reasoning belongs in the tracker or the ADR.
- Downstream repos (Zenzique, PoC-*) take the NuGet packages at a version bump and decide from the
  release notes; there is no cross-repo CI. Call out behaviour changes and public API breaks in
  `releases/vX.Y.Z.md`.
- `VersionPrefix` in `src/Directory.Build.props` is bumped last, at release time.
