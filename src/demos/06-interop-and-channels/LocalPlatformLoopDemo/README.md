# LocalPlatformLoopDemo

Demonstrates the **local design loop**: a workflow that declares three
`PlatformNative` capabilities runs entirely in-process against three emulated
cloud targets — no API keys, no cloud SDK dependencies, no network required for
the core loop.

**No API keys required.** The core loop (validate → deploy → run) is fully offline.
The `web_search` and `web_fetch` emulators will make real HTTP calls if network is
available, but the rest of the demo works without it.

---

## What this demo shows

| Step | What happens |
|---|---|
| Build `ToolKit` | Three `PlatformNative` tools: `code_execution`, `web_search`, `memory_bank` |
| Build registry | `DefaultPlatformNativeExecutors.Register()` wires all built-in emulators |
| Load manifest | `local-platform-loop.ananke.yml` — four jobs, sequential pipeline |
| Loop × 3 targets | Validate coverage → patch ToolKit → deploy locally → run jobs |
| Alias demo | Shows `foundry → azure-ai` alias resolution (`FED060` warning) |

The three targets are:

| Target | Emulated platform |
|---|---|
| `local-emulated:azure-ai` | Azure AI Foundry |
| `local-emulated:claude` | Anthropic Claude |
| `local-emulated:vertex-ai` | Google Vertex AI |

---

## Emulator tiers in action

| Tool | Capability | Emulator tier |
|---|---|---|
| `run_code` | `code_execution` | **Real** — delegates to a bash subprocess |
| `search_web` | `web_search` | **Real** — queries DuckDuckGo Lite over HTTP |
| `store_memory` | `memory_bank` | **In-process** — shared `ConcurrentDictionary` |

The validator emits `FED062` (warning) for any capability covered by a stub
so you know which tools return fixture data vs real results.

---

## Quick start

```bash
cd src/demos/06-interop-and-channels/LocalPlatformLoopDemo
dotnet run
```

Expected output (abbreviated):

```
══════════════════════════════════════════════════════════════════
  LocalPlatformLoopDemo — Ananke local design loop
══════════════════════════════════════════════════════════════════

Registered 38 capability executors.

Manifest loaded: 'local-platform-loop'  (4 jobs)

── Target: Azure AI (Foundry) (local-emulated:azure-ai) ──────────
  ✅ Validation passed — all capabilities covered
  Patched 3/3 tools with local emulators
  Deployed  id=<id>  status=Active
  [search]   Ananke agent orchestration — 5 results found…
  [execute]  4
  [remember] stored key=demo:local-emulated:azure-ai
  [summarise] workflow complete for local-emulated:azure-ai

── Target: Anthropic Claude (local-emulated:claude) ─────────────
  ...

── Platform alias demo ──────────────────────────────────────────
  'foundry' alias resolved → FED060 warnings: 1

══════════════════════════════════════════════════════════════════
  Done. 3 local deployment records created.
══════════════════════════════════════════════════════════════════
```

---

## Key types

| Type | Package | Role |
|---|---|---|
| `PlatformNativeExecutorRegistry` | `Ananke.Federation` | Maps capability names to executors |
| `DefaultPlatformNativeExecutors` | `Ananke.Federation.LocalEmulators` | Registers all built-in emulators |
| `LocalPlatformValidator` | `Ananke.Federation` | Validates capability coverage for a local target |
| `LocalFederationDeployer` | `Ananke.Federation` | In-process deployer — no credentials needed |
| `DeployabilityValidator` | `Ananke.Federation` | Structural validation + alias resolution |

---

## Related

- [`Ananke.Federation.LocalEmulators` README](../../../Ananke.Federation.LocalEmulators/README.md) — full emulator catalogue
- [`Ananke.Federation` README](../../../Ananke.Federation/README.md) — local loop architecture overview
- [Guide 19 — Federation Local Loop](../../../../docs/guides/20-platform-recommendation.md)
