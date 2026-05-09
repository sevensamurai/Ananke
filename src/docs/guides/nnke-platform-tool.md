# nnke-platform — Federation CLI

`nnke-platform` is the federation operations tool for Ananke. It lets you deploy workflow manifests to cloud AI platforms (Azure AI Agent Service, Google Vertex AI / Gemini, Anthropic Claude), monitor live deployments, inspect metrics, and manage the federated mesh — all from the terminal.

Platform adapters are distributed as **separate companion tools** and discovered at runtime via module initializers, so `nnke-platform` itself stays lean and each adapter can be installed, updated, and versioned independently.

---

## Install

```bash
# Core CLI
dotnet tool install -g nnke-platform

# Choose one adapter (or install all)
dotnet tool install -g nnke-platform-azure
dotnet tool install -g nnke-platform-google
dotnet tool install -g nnke-platform-anthropic

# Or install every adapter at once
dotnet tool install -g nnke-platform-all
```

Installing an adapter tool copies its assemblies into `~/.nnke-platform/adapters/`. On every run, `nnke-platform` probes that directory and loads any adapters present — no configuration file required.

---

## Commands

| Command | Description |
|---|---|
| `nnke-platform validate <file> --platform <p>` | Validate a manifest's deployability to a target platform |
| `nnke-platform capabilities [--platform <p>]` | List known platform-native tool capabilities |
| `nnke-platform profiles <file> [profile]` | List or inspect deployment profiles in a manifest |
| `nnke-platform deploy <file> --platform <p>` | Deploy a workflow to a target platform |
| `nnke-platform status [--deployment-id <id>]` | Show deployment status |
| `nnke-platform teardown --deployment-id <id>` | Tear down a deployed workflow |
| `nnke-platform trends [--deployment-id <id>]` | Show metrics trends for deployments |
| `nnke-platform analyze <file>` | Analyze manifest complexity + runtime trends |
| `nnke-platform lineage <cell> <file>` | Show federated ancestor/descendant tree for a cell |
| `nnke-platform mesh <file>` | Mesh status with platform, deployment ID, and remote health |
| `nnke-platform apoptosis [--auto] <file>` | Identify (and optionally tear down) idle/aged cells |
| `nnke-platform compare <cell> --across <p,p>` | Compare token/latency metrics across platforms |
| `nnke-platform events [--follow]` | Stream mesh and deployment events |
| `nnke-platform login --platform <p>` | Configure credentials for a platform |
| `nnke-platform whoami` | Show configured platform identities |

All commands support `--json` for machine-readable output and `--in-memory` for dry-run/test mode (no disk state).

---

## Quick Start

```bash
# 1. Install the CLI and an adapter
dotnet tool install -g nnke-platform
dotnet tool install -g nnke-platform-azure

# 2. Configure credentials
nnke-platform login --platform azure-ai

# 3. Check what capabilities Azure AI supports
nnke-platform capabilities --platform azure-ai

# 4. Validate your manifest
nnke-platform validate my-workflow.ananke.yml --platform azure-ai

# 5. Deploy
nnke-platform deploy my-workflow.ananke.yml --platform azure-ai --profile azure-ai

# 6. Monitor
nnke-platform status
nnke-platform trends
```

---

## Deployment Profiles

Define platform-specific tool bindings in your `.ananke.yml` manifest:

```yaml
profiles:
  local:
    tools:
      search: { execute: local }
      code:   { execute: local }
  azure-ai:
    tools:
      search: { platform: bing_search }
      code:   { platform: code_interpreter }
  vertex-ai:
    tools:
      search: { platform: google_search }
      code:   { platform: code_execution }
  claude:
    tools:
      search: { platform: brave_search }
      code:   { platform: bash }
```

The same workflow manifest targets any platform by selecting a profile — no code changes needed.

---

## Adapter Discovery

Each companion tool (`nnke-platform-azure`, etc.) installs a `[ModuleInitializer]` assembly into `~/.nnke-platform/adapters/`. When `nnke-platform` starts, it:

1. Scans `~/.nnke-platform/adapters/` for `*.dll` files
2. Loads them via `Assembly.LoadFrom` — triggering each module initializer
3. Each initializer calls `FederationDeployerRegistry.RegisterFactory(platform, factory)`
4. After loading, `MaterializeFactories(deploymentRegistry)` turns each registered factory into a live `IFederationDeployer`

This means you can add or remove adapters simply by installing/uninstalling the companion tool, with no restart or configuration change needed.

---

## Adapter Status

| Adapter | Platform key | Status |
|---|---|---|
| `nnke-platform-azure` | `azure-ai` | Stable |
| `nnke-platform-google` | `vertex-ai` | Stable |
| `nnke-platform-anthropic` | `claude` | **Preview** — requires Anthropic managed-agents Beta access |
| `nnke-platform-all` | (all) | Meta-installer; installs Azure + Google + Anthropic |

---

## Architecture Boundary

| Concern | Tool |
|---|---|
| Deploy to Azure / Google / Anthropic | `nnke-platform` |
| Live remote deployment status | `nnke-platform` |
| Metrics trends, apoptosis, compare | `nnke-platform` |
| Platform credentials / login | `nnke-platform` |
| Mesh lineage (federated view) | `nnke-platform lineage` |
| Mesh status (with platform info) | `nnke-platform mesh` |
| Scaffold projects & manifests | `nnke` |
| Validate, diagram, inspect | `nnke` |
| Browse docs, patterns, error codes | `nnke` |
| Inspect local mesh snapshots | `nnke mesh` |

---

## Related

- [`nnke` Tool Companion](00-nnke-tool.md) — design-time CLI
- [`Ananke.Federation` README](../../src/Ananke.Federation/README.md) — federation library API
- [Anthropic adapter README](../../src/nnke-platform-anthropic/README.md) — Beta / preview notes
