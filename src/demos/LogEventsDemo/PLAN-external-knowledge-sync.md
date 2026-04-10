# Plan: Extend LogEventsDemo with ExternalKnowledgeSyncer

## Goal

The default demo mode becomes a **narrated replay** — an incident
story that plays out from a clean state to resolution, with no user
interaction required. The viewer watches the system populate cards and
releases, simulate logs, detect errors, see a human jump in to
investigate and roll back a service, wait for clean logs, then watch
the agent learn the full causal + remediation pattern.

```
LogEventsDemo                → replay mode (default, tells the story)
LogEventsDemo --interactive  → existing REPL mode (explore manually)
LogEventsDemo --ticks 500    → control simulation length (both modes)
```

The critical design constraint: **commit messages must read like normal
developer work.** Nobody writes "this will break the schema" in their
commit. The value of the system is that it *discovers* the causal link
by correlating timing, files changed, and runtime error patterns —
surfacing human coordination failures that are invisible in isolation.

## Current demo flow

```
Phase 1:   Seed heuristics → IEmpiricalMemory (architecture, runbooks)
Phase 2:   Run log simulation → List<LogEvent>
Phase 3:   Rule-based pattern detection → IEmpiricalMemory (patterns)
Phase 4:   Offline learning → reinforcement, discovery
Phase 5:   Interactive REPL or --auto report
```

## Proposed demo flow: narrated replay (default)

The replay tells the incident story in **acts**. Each act prints a
narration header, runs its step, and shows relevant output. Small
`Thread.Sleep` pauses between acts let the viewer absorb what happened
(skippable with `--fast`). The entire replay is deterministic — a
fixed seed for the RNG so the same story plays out every time (unless
`--seed N` is passed).

```
┌─────────────────────────────────────────────────────────────────┐
│  Act 1 — Setup                                                  │
│    Load architecture heuristics into empirical memory            │
│    Create work cards on the board (simulated backlog)            │
│    Deploy 4 services via CI/CD (ExternalKnowledgeSyncer)         │
│    Show: "All services healthy, 4 releases synced"               │
│                                                                  │
│  Act 2 — Normal operations                                      │
│    Run 60 ticks of log simulation                                │
│    Stream a few representative log lines                         │
│    Show: "All services nominal"                                  │
│                                                                  │
│  Act 3 — Errors appear                                          │
│    Continue simulation (140 ticks) — failure scenarios fire       │
│    Stream error log lines as they appear (color-coded)           │
│    Run pattern detection                                         │
│    Show: "⚠ N patterns detected" with top findings               │
│                                                                  │
│  Act 4 — Agent learns from the incident                         │
│    Run offline learning cycle                                    │
│    Show discoveries: correlations between deploy + errors         │
│    Show: "Deploy v2.4.1 correlates with schema_mismatch"          │
│                                                                  │
│  Act 5 — Human investigates and rolls back                      │
│    Narrate: "On-call engineer checks recent deployments..."      │
│    Query knowledge store for recent releases                     │
│    Surface: reporting-backend v2.4.1 changeset                   │
│    Narrate: "Rolling back reporting-backend v2.4.1 → v2.4.0..."  │
│    Sync rollback release event via ExternalKnowledgeSyncer       │
│    Run 80 more ticks with schema mismatch scenario suppressed    │
│    Show: before/after error comparison                            │
│    Narrate: "Schema mismatch errors: 4 → 0 ✓"                    │
│                                                                  │
│  Act 6 — Agent learns from the resolution                       │
│    Run offline learning cycle                                    │
│    Show: "Learned: rollback resolves schema_mismatch"            │
│    Show: both deploy→error and rollback→resolved patterns         │
│    Show: reinforcement of causal link                             │
│                                                                  │
│  Act 7 — Summary                                                │
│    Release correlation report (changeset + timing)               │
│    Learned heuristics (emergent, not coded)                      │
│    Memory stats                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### What the viewer sees (terminal output sketch)

```
═══════════════════════════════════════════════════════════════════
  LogEventsDemo — Incident Replay
  A simulated distributed system, real pattern learning.
═══════════════════════════════════════════════════════════════════

──── Act 1: Setup ────────────────────────────────────────────────

  🏗️  Loading architecture heuristics...
     8 entries from wiki/architecture docs.

  📋 Work cards on the board:
     Card #412  "Add multi-currency support to reports"
     Card #305  "Upgrade redis client for connection stability"
     Card #288  "Improve MQTT reconnect behavior"
     Card #391  "Add customer field to order API response"

  🚀 Deploying services via CI/CD...
     api-gateway       v3.1.0  deployed  (2 commits, PR #72 → Card #391)
     background-worker v2.8.1  deployed  (2 commits, PR #65 → Card #305)
     reporting-backend v2.4.1  deployed  (2 commits, PR #67 → Card #412)
     iot-ingestion     v1.7.0  deployed  (2 commits, PR #70 → Card #288)
     Synced 4 release documents to knowledge store.

──── Act 2: Normal operations ────────────────────────────────────

  📊 Running simulation (60 ticks)...
  08:00:03 [INF] api-gateway          GET /api/v1/status 200 OK (12ms)
  08:00:03 [INF] background-worker    Dequeued job order-process-7821 from redis
  08:00:05 [INF] reporting-backend    Scheduled report cycle started
  08:00:05 [INF] iot-ingestion        Telemetry batch: 128 events in 340ms
     ...
     All services nominal. 247 events generated.

──── Act 3: Errors appear ────────────────────────────────────────

  📊 Continuing simulation (140 ticks)...
  08:12:47 [ERR] reporting-backend    MongoDB error: document validation failed
                                      — schema mismatch on field 'revenue_cents'
  08:12:47 [ERR] reporting-backend    Report template 'monthly-revenue' cannot be
                                      deserialized: missing field 'currency_code'
  08:12:48 [ERR] api-gateway          GET /api/v1/reports/monthly 500 Internal Error
  08:14:30 [ERR] background-worker    Redis ETIMEDOUT: connection timed out
     ...

  🔍 Running pattern detection...
     Detected 12 patterns (7 after dedup).
     Top findings:
       [pat-001] schema_mismatch on reporting-backend  (conf=0.78)
       [pat-002] redis connection pool on background-worker  (conf=0.65)
       [pat-003] upstream timeout cascade api-gateway→worker  (conf=0.61)

──── Act 4: Agent learns ─────────────────────────────────────────

  🔄 Running offline learning cycle...
     Explored: 8, Reinforced: 5, Contradicted: 0, Decayed: 2

  💡 Discoveries:
     • reporting-backend errors correlate with release v2.4.1
     • schema_mismatch appeared after v2.4.1 deployed at 08:00
     • background-worker redis errors correlate with release v2.8.1

──── Act 5: On-call engineer investigates ────────────────────────

  👤 "Checking recent deployments for reporting-backend..."

  📦 Knowledge store → release reporting-backend v2.4.1:
     Deployed: 08:00:00
     Commits:
       d6b1c9f alice: "Add currency_code to revenue reports"
         files: ReportTemplates/MonthlyRevenue.cs, Models/RevenueRecord.cs
         PR #67 → Card #412
       f1c8e3a charlie: "Migrate report templates to new schema"
         files: ReportTemplates/TemplateSchema.json, Migrations/003_currency.js
         PR #68 → Card #412

  👤 "Rolling back reporting-backend v2.4.1 → v2.4.0..."

  🚀 Syncing rollback release event...
  📊 Running post-rollback simulation (80 ticks)...
  08:22:15 [INF] reporting-backend    Report daily-sales generated in 1200ms
  08:22:18 [INF] reporting-backend    MongoDB find: report_templates returned 3 docs
     ...

  📊 Rollback effect:
     schema_mismatch errors:  4 (pre) → 0 (post)  ✓ resolved
     upstream 500s:           2 (pre) → 0 (post)  ✓ resolved
     redis pool errors:       3 (pre) → 2 (post)    unchanged (different issue)

──── Act 6: Agent learns from resolution ─────────────────────────

  🔄 Running offline learning cycle...
     Explored: 6, Reinforced: 4, Contradicted: 0, Decayed: 1

  💡 New pattern learned:
     "Rollback reporting-backend v2.4.1→v2.4.0 resolves schema_mismatch"
     Tags: [reporting-backend, rollback, schema_mismatch, release:v2.4.0]

  🔗 Causal confirmation:
     deploy v2.4.1 → schema_mismatch errors appeared  (pattern from Act 4)
     rollback v2.4.0 → schema_mismatch errors stopped  (pattern from Act 6)
     Both reinforced — strong causal signal.

──── Act 7: Summary ──────────────────────────────────────────────

  Release Correlation:
    Pattern "schema mismatch on reporting-backend" (conf=0.87)
      ↳ errors started: 08:12:47
      ↳ release v2.4.1 deployed: 08:00:00
      ↳ changeset:
          d6b1c9f alice: "Add currency_code to revenue reports"
            files: ReportTemplates/MonthlyRevenue.cs, Models/RevenueRecord.cs
          f1c8e3a charlie: "Migrate report templates to new schema"
            files: ReportTemplates/TemplateSchema.json, Migrations/003_currency.js
      ↳ rollback to v2.4.0 at 08:22:15 → errors resolved

  Learned heuristics (emergent):
    • "changes to ReportTemplates/ co-occur with error:schema_mismatch"
    • "rollback of reporting-backend resolves schema_mismatch errors"
    • "errors correlated with recent deploys that resolve after
       rollback are likely deploy-caused"

  Memory: 9 patterns, 8 heuristics, 842 log events
```

### CLI arguments

```
LogEventsDemo                      → replay mode (default)
LogEventsDemo --interactive        → REPL mode (existing behavior + new commands)
LogEventsDemo --fast               → replay without pauses
LogEventsDemo --ticks 500          → control simulation length
LogEventsDemo --seed 42            → deterministic RNG seed
```

The `--auto` flag becomes an alias for the default (backward compat).

## New types

### `ReleaseEvent` + `ChangeEntry` — the `TEvent` for the syncer

```
demos/LogEventsDemo/Releases/ReleaseEvent.cs
```

```csharp
sealed record ReleaseEvent
{
    required string Service        // "api-gateway"
    required string Version        // "v3.1.0"
    required string Environment    // "au-prod"
    required DateTimeOffset DeployedAt
    required IReadOnlyList<ChangeEntry> Changes
}

sealed record ChangeEntry
{
    required string CommitHash     // "a3f1c2d"
    required string Author
    required string Message
    required IReadOnlyList<string> FilesChanged
    string? PrNumber               // "42"
    string? CardId                 // "789"
}
```

### `ReleaseKnowledgeSource` — the `IExternalKnowledgeSource<ReleaseEvent>`

```
demos/LogEventsDemo/Releases/ReleaseKnowledgeSource.cs
```

Implements `ResolveAsync` → returns a `ResolvedKnowledgeBatch` with one
`KnowledgeDocument` per release event:

```
Id:       "release:{service}:{version}"
Text:     "{service} {version} deployed to {env} at {time}.
           Contains {N} commits:
           - {hash} by {author}: {message} (files: {files}) [PR #{pr} → Card #{card}]
           - ..."
Metadata: { service, version, environment, deployed_at }
```

The text is a **factual inventory** — files changed, authors, PRs. No
risk assessment, no causal claims. The system discovers causality later
by correlating this inventory with runtime error patterns.

In a real product, `ResolveAsync` would call GitHub's API to resolve
the commit→PR→card chain. Here it's a pure data transform from the
simulated history.

### `SimulatedReleaseHistory` — the fake release/deploy data

```
demos/LogEventsDemo/Releases/SimulatedReleaseHistory.cs
```

Static data modeled after `SimulatedCommitLog` but structured as
`ReleaseEvent` objects with PR/card links.

**Design principle: commits look like normal, well-intentioned work.**
The developers don't know they're introducing problems. The human errors
are coordination failures — deploying incompatible interfaces, missing
migration steps, wrong sequencing — that only become visible when
errors appear in production.

| Service | Version | Commit messages (innocent) | Actual human error (invisible in commits) |
|---|---|---|---|
| api-gateway | v3.1.0 | "Add customer field to order response", "Extract OrderController validation" | Deployed new field before client SDK updated; null when old clients call |
| background-worker | v2.8.1 | "Bump redis client to 2.8.1", "Add batch processing for report jobs" | New redis client has different pool defaults; batch processing multiplies connection pressure under load |
| reporting-backend | v2.4.1 | "Add currency_code to revenue reports", "Migrate report templates to new schema" | Schema migration deployed *after* code; new code hits old schema for ~30 min window |
| iot-ingestion | v1.7.0 | "Upgrade mqtt client library to 5.0.2", "Add reconnect backoff for mqtt client" | New library changed default keepalive interval; broker interprets as stale and disconnects |

Also includes **work card** records used by Act 1 to show the backlog:

| Card | Title | Service |
|---|---|---|
| #412 | Add multi-currency support to reports | reporting-backend |
| #305 | Upgrade redis client for connection stability | background-worker |
| #288 | Improve MQTT reconnect behavior | iot-ingestion |
| #391 | Add customer field to order API response | api-gateway |

Each release links back to its card via `ChangeEntry.CardId`, so the
knowledge document text includes the traceability chain:
`commit → PR → card`.

### `ReplayNarrator` — the new entry point for replay mode

```
demos/LogEventsDemo/ReplayNarrator.cs
```

Orchestrates the 7-act replay. Accepts all the same dependencies as
`Explorer` (simulator, memory, learner, detector, knowledge store,
syncer) but drives them sequentially through the story instead of
waiting for user input.

Each act is a method (`Act1SetupAsync`, `Act2NormalOpsAsync`, etc.)
that:
1. Prints a narration header
2. Runs the step (simulation, detection, learning, rollback)
3. Prints key output (log lines, patterns, discoveries)
4. Pauses briefly (unless `--fast`)

This keeps `Program.cs` clean — it just picks `ReplayNarrator` or
`Explorer` based on the CLI flag.

## Changes to existing files

### `ServiceDefinition.cs` — add `Version` field

```csharp
public required string Version { get; init; }  // "v3.1.0"
```

### `SystemTopology.cs` — add version to each service

Each entry gets version values matching `SimulatedReleaseHistory`.
Also add a mutable version overlay for rollback:

```csharp
static Dictionary<string, string> VersionOverrides { get; }  // used during rollback
static string GetVersion(string service)  // checks overlay first, falls back to definition
```

### `LogEvent.cs` — add `ServiceVersion` field

```csharp
public string? ServiceVersion { get; init; }
```

Mirrors OTEL `service.version`. Every log event carries the release tag.

### `LogSimulator.cs`

- Stamp `ServiceVersion` on every emitted event (via `SystemTopology.GetVersion`)
- Add `suppressedScenarios: IReadOnlySet<string>?` parameter to `RunAsync`
  — scenario names that won't fire (for post-rollback simulation)
- Decouple channel completion from `RunAsync` so the simulator can be
  called multiple times (Act 2 normal ops, Act 3 errors, Act 5 post-rollback)

### `RuleBasedPatternDetector.cs`

- If error events carry `ServiceVersion`, add `release:{version}` to
  the pattern's `Tags`
- Add `deploy:recent` semantic tag
- New method: `DetectRollbackEffectAsync(preEvents, postEvents,
  service, fromVersion, toVersion)` — compares error rates between two
  windows, commits a pattern with `action:rollback` semantic tag if a
  specific error type resolves

### `Explorer.cs` — add REPL commands for interactive mode

New commands (supplement existing):

- **`release <service>`** — queries `IKnowledgeStore.SearchAsync`
- **`changes <version>`** — searches knowledge store for version
- **`rollback <service>`** — runs the full rollback flow interactively
- Existing `commits` command updated to use `SimulatedReleaseHistory`
  instead of `SimulatedCommitLog`

### `Program.cs`

- Default mode becomes replay (currently default is REPL)
- `--auto` becomes alias for default (backward compat)
- `--interactive` flag for REPL mode
- `--fast` flag to skip narration pauses
- `--seed N` for deterministic RNG
- Wire up `InMemoryKnowledgeStore`, `ReleaseKnowledgeSource`,
  `ExternalKnowledgeSyncer<ReleaseEvent>`, `ReplayNarrator`

### `SimulatedCommitLog.cs` — **Remove**

Replaced by `SimulatedReleaseHistory` which carries richer data
(files changed, PR links, card links).

## Rollback mechanics (Acts 5–6)

### What happens

1. **Pick a service to roll back.** In replay mode, always
   `reporting-backend` (the most dramatic incident — schema mismatch
   errors with clear before/after). In interactive mode, user chooses.

2. **Sync a rollback release event.** Create a new `ReleaseEvent`
   with the previous version and a single "rollback" change entry.
   Feed through `ExternalKnowledgeSyncer` — the knowledge store now
   has both the original deploy and the rollback.

3. **Update version overlay.** `SystemTopology.VersionOverrides` maps
   `reporting-backend → v2.4.0`. New log events carry the old version.

4. **Run more simulation ticks.** The `MongoDB Schema Mismatch After
   Deploy` scenario is in `suppressedScenarios` — it can't fire.
   Other scenarios still fire normally.

5. **Detect rollback effect.** Compare error rates per error type
   between pre-rollback and post-rollback windows. Commit a pattern
   when an error type drops to zero.

6. **Learn from the resolution.** The agent sees both the deploy→error
   and the rollback→resolved patterns. Tag overlap is very high.
   The learner reinforces both — **causal confirmation through
   intervention**, not just correlation.

### What the system learns (vs. what it doesn't)

**Learns:** "Rollback of reporting-backend resolves schema_mismatch."
A structural remediation pattern.

**Does NOT learn:** "The migration was deployed in the wrong order."
That's still a human insight.

**Over time, learns:** "Deploy-correlated errors that resolve after
rollback are likely deploy-caused." Emergent institutional knowledge
— the kind experienced SREs carry and that gets lost when they leave.

## Why the commit data must be innocent

- Developers write normal commit messages: "Add currency_code to reports"
- Nobody writes "WARNING: this introduces a backward-incompatible schema
  change that will cause deserialization failures"
- The human errors are **coordination failures**: deploying code before
  migration, changing defaults without load testing, adding required
  fields without backward compat
- These errors are **invisible in the commit data** — they only become
  visible when correlated with runtime errors
- The system's value is **surfacing the correlation**, not generating the
  explanation. A human (or LLM) looks at "v2.4.1 changed template schema
  files, schema_mismatch errors started 14 minutes later" and concludes
  "migration wasn't run first"

If the commit messages explained the failures, the demo would be a
trivial string match. The real demo is: "here's a changeset that looks
fine, here are errors that look unrelated, and the system connected them
through timing and file overlap."

## File inventory

| File | Action | Description |
|---|---|---|
| `Releases/ReleaseEvent.cs` | **New** | `ReleaseEvent` + `ChangeEntry` records |
| `Releases/SimulatedReleaseHistory.cs` | **New** | Static fake release/card data |
| `Releases/ReleaseKnowledgeSource.cs` | **New** | `IExternalKnowledgeSource<ReleaseEvent>` impl |
| `ReplayNarrator.cs` | **New** | 7-act narrated replay orchestrator |
| `ServiceDefinition.cs` | **Edit** | Add `Version` property |
| `SystemTopology.cs` | **Edit** | Add versions + mutable override for rollback |
| `LogEvent.cs` | **Edit** | Add `ServiceVersion` field |
| `LogSimulator.cs` | **Edit** | Stamp `ServiceVersion`; add `suppressedScenarios`; multi-call support |
| `RuleBasedPatternDetector.cs` | **Edit** | Release tags on patterns; `DetectRollbackEffectAsync` |
| `Explorer.cs` | **Edit** | Add `release`, `changes`, `rollback` commands; update `commits` |
| `Program.cs` | **Edit** | Replay-by-default; wire knowledge store + syncer + narrator |
| `SimulatedCommitLog.cs` | **Remove** | Replaced by `SimulatedReleaseHistory` |

## Data flow — end to end

```
SimulatedReleaseHistory         (fake CI/CD events — innocent commit messages)
        │                        + work cards (backlog items)
        ▼
ReleaseKnowledgeSource          (IExternalKnowledgeSource<ReleaseEvent>)
        │  ResolveAsync → KnowledgeDocument per release
        │  (factual inventory: files, authors, PRs, cards)
        ▼
ExternalKnowledgeSyncer         (framework)
        │  SyncBatchAsync
        ▼
InMemoryKnowledgeStore          (pre-materialized release context)
        │
        ├─── Act 5: narrator queries for recent releases
        │      "what changed in reporting-backend recently?"
        │
        ├─── Act 7: release correlation report
        │      "errors started at 08:12, v2.4.1 deployed at 08:00"
        │
        └─── Act 5: rollback release event (synced via same pipeline)
               "reporting-backend rolled back to v2.4.0"

LogSimulator                    (producer side)
        │  stamps ServiceVersion on every LogEvent
        │
        ├── Act 2: normal run (60 ticks) — all healthy
        ├── Act 3: error run (140 ticks) — failures fire
        └── Act 5: post-rollback (80 ticks) — scenario suppressed
        ▼
RuleBasedPatternDetector
        │  Act 3: detects errors, adds release:v2.4.1 tag
        │  Act 5: detects rollback effect (error rate drop)
        ▼
IEmpiricalMemory
        │  deploy→error pattern  (Act 3/4)
        │  rollback→resolved pattern  (Act 5/6)
        │  High tag overlap — strong correlation
        ▼
OfflineLearner
        │  Act 4: discovers deploy↔error correlation
        │  Act 6: confirms via rollback, learns remediation
        │  Emergent: "deploy-correlated errors that resolve
        │            after rollback are likely deploy-caused"
```

## What this demonstrates

1. **Narrated story over raw tool** — the default experience tells a
   complete incident lifecycle, making the framework's value obvious
   without requiring the viewer to know REPL commands
2. **`ExternalKnowledgeSyncer` in action** — the framework's ingestion
   contract feeding `IKnowledgeStore` from domain events (releases,
   cards, rollbacks — all go through the same pipeline)
3. **Producer/consumer split** — log events carry only `ServiceVersion`;
   all resolution happens consumer-side via knowledge store
4. **Correlation, not explanation** — the system surfaces *what changed*
   next to *what broke*; it doesn't claim causality
5. **Causal confirmation through intervention** — "deploy → errors" is
   correlation; "deploy → errors; revert → errors stop" is causal
   confirmation. The system learns the remediation, not just the failure
6. **Emergent learning** — `OfflineLearner` discovers file-path and
   change-pattern correlations that no one documented
7. **Two-store interplay** — `IKnowledgeStore` (factual release docs)
   and `IEmpiricalMemory` (learned patterns) work together
8. **Institutional knowledge** — the system builds SRE-level heuristics
   from observed incidents, surviving team turnover

## Sequencing

1. New types: `ReleaseEvent`, `SimulatedReleaseHistory`, `ReleaseKnowledgeSource`
2. Edit `ServiceDefinition` + `SystemTopology` + `LogEvent` + `LogSimulator`
   (including `suppressedScenarios` and multi-call support)
3. New: `ReplayNarrator` (7-act orchestrator)
4. Edit `RuleBasedPatternDetector` (release tags + `DetectRollbackEffectAsync`)
5. Edit `Explorer` (add `release`, `changes`, `rollback` commands)
6. Edit `Program.cs` (replay-by-default, wire everything)
7. Remove `SimulatedCommitLog`
8. Test: `dotnet run --project demos/LogEventsDemo`
9. Test: `dotnet run --project demos/LogEventsDemo -- --interactive`
10. Test: `dotnet run --project demos/LogEventsDemo -- --fast`
