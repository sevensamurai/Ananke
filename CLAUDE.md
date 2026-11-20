# Ananke

C# .NET 10 library for AI agent orchestration. Solution: `src/Ananke.slnx`. Sessions run on WSL
Ubuntu 26.04 or native Ubuntu; CI runs `ubuntu-latest`.

This file and the skills in `.claude/skills/` are the only standing instructions. Anything that
must hold in every session goes here, not in auto-memory.

# Start here
- Work is on the NNK board in Zenzique: `zz features NNK` for what is in flight and what is next,
  `zz show <card>` for the card in hand. Open the design note the card or its feature links — not
  the rest of `internals/design/`. `internals/STATUS.md` is no longer kept up.
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
- **`bash scripts/ci.sh` is the check before a PR** (and what `card verify` runs): the encoding
  check (no UTF-8 BOM), the internal-refs check, `scripts/check-docs.ps1`, the docs search index,
  the release notes for the version, then restore, Release build, `dotnet format --verify-no-changes`
  and test of `src/Ananke.slnx`; then it packs every package into `artifacts/ci/packages` and
  builds the two consumers in `scripts/consumers` (Seren-shaped, Zenzique-shaped) from that folder
  only. It stops at the first failure with a non-zero exit.
- To strip a BOM it reports: `pwsh -File scripts/fix-encoding.ps1`. To fix what the format check
  reports, prefer `dotnet format whitespace src/Ananke.slnx` — plain `dotnet format` also applies
  style fixes.

# Code
- Vertical slices: `Features/<FeatureName>/`.
- `TimeProvider`, not `DateTime.Now` / `UtcNow`. Primary constructors for services. Records for DTOs
  and value objects. `CancellationToken` on every public async method. Collection expressions
  (`[.. list]`) over LINQ `Concat`.
- No change needs approval before it is made, a `.csproj` or a new project included. The build, the
  tests and the checks under *Before a pull request* decide whether it stands.
- **Before adding a mechanism, check what the framework already has** (`codegraph_explore`) and say
  in one line what you found. The plan code once grew three hand-written parsers while `AgentJob`
  already constrained model output to a JSON schema.
- **Comments say what the code does, and any constraint the code itself does not show.** No history
  ("used to", "this cost N runs"), no rule or item ids (`R27`, `S8`), no ADR ids or `internals/`
  paths. History goes in the commit or on the card. ADR and `internals/` citations in `src/`,
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

# Design, plan, execute (`internals/`)
- `internals/` is never pushed publicly (`.public-exclude`, `push-public.sh`).
- **The flow is the workspace's:** `/design` when a choice needs settling, `/plan` for a feature
  card and its ready cards, `/execute` to build them. A feature card is the plan: its cards are the
  steps, a card's checklist the steps inside it, card notes the log.
- **A design note in place of an ADR**, only when a change touches a public contract or a package
  boundary, or has more than one workable option: `internals/design/<yyyyMMdd>-design-<name>.md`,
  one page — what was observed, the options, the decision and its reason, what changes for a
  product that upgrades, how we'll know. Linked from its feature card. A decision that changes
  edits the note in place.
- **No new ADRs or trackers.** Those in `internals/design/` are history: read one when a card or
  note cites it, and don't extend it.
- **Check every code-level instruction against the source before writing it into a card or a
  note.** A plan that is confidently wrong gets followed.
- **Ids carry their document** — `048·R50`, not a bare `R50`. Letters are reused across documents.
- **Record what was observed and what changed**, quoting the actual error or output. Do not reply
  to an earlier draft, an earlier session or the author inside the document.
- Many changes to one document: rewrite it with `Write` rather than a long run of `Edit`s.
- For design that will take several iterations to settle, use the `spike` skill.

# Demos
- **Before changing a demo, name the card or tracker step the change belongs to.** If neither says
  what the demo must show, leave the demo as it is and put the question on the card. Do not change
  the scenario to make a finding fit.

# Put the question on the card, and go on with what doesn't depend on it
In a session with the user, say it; in unattended work, block the card with the question:
- when a document has been rewritten three times in this session;
- when the reason for a decision is text you wrote earlier, rather than a run, a test or the user;
- when a card's work needs a concept, term or mechanism neither the card nor its design note mentions.

# Commits and releases
- Commit subject: imperative, ~60 characters, says what changed ("Rename World to TripService").
  Body: 1–2 lines and `Work-Item: NNK-<n>`. The reasoning belongs in the design note or on the card.
- Downstream repos (Zenzique, PoC-*) take the NuGet packages at a version bump and decide from the
  release notes; there is no cross-repo CI. Call out behaviour changes and public API breaks in
  `releases/vX.Y.Z.md`.
- **Before 1.0, a release may break the public API or change behaviour.** Each break is called out
  in its notes, under *Breaking changes* or as a ⚠ under *Upgrading*.
- **nuget.org cannot delete a package; it can deprecate and unlist one.** A package dropped before
  1.0 has every published version deprecated before the notes that drop it are published.
  **At 1.0:** unlist every version of each dropped package, and deprecate — not unlist — every
  version below 1.0 of the packages that ship, reason *Legacy*, with a message pointing at the 1.0
  notes. The account owner does both; the steps are in `internals/nuget-deprecation-0.9.1.md`.
- `VersionPrefix` in `src/Directory.Build.props` is bumped last, at release time.
