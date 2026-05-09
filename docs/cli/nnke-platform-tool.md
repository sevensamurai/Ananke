<!-- topic: nnke-platform, tags: nnke-platform, dotnet-tool, federation, deployment, cloud, azure, google, anthropic, mesh, organics, local-loop, emulators, local-emulated, local-design-loop, foundry, gemini-enterprise, platform-native -->
# nnke-platform Tool Companion

`nnke-platform` is the operations CLI for Ananke federation: validate deployability, manage remote deployments, inspect live health, and work with platform-specific runtime concerns.

For the shared rationale behind the CLI split and the high-level difference between `nnke` and `nnke-platform`, start with [CLI Overview](overview.md).

---

## What `nnke-platform` Covers

Use `nnke-platform` when your workflow has left the local design loop and you need platform-aware operations.

Core responsibilities:
- validate deployability against a target platform
- deploy, monitor, and tear down remote workflows
- inspect live deployment status and health
- analyse trends, compare platforms, and manage federation state
- configure credentials and platform adapters

---

## Installation

### Prerequisites

`nnke-platform` targets `.NET 10`. Verify your SDK before installing:

```bash
dotnet --version
```

### Install the CLI

```bash
dotnet tool install -g nnke-platform
```

At this point you have the core CLI but no platform adapters. Validation, profiles, and capabilities
commands work immediately; `deploy` and `status` require an adapter to be installed first.

Update later with:

```bash
dotnet tool update -g nnke-platform
```

### Install Platform Adapters

Each adapter is a separate .NET tool. Install only the platforms you intend to deploy to:

```bash
# Azure AI Agent Service
dotnet tool install -g nnke-platform-azure

# Google Vertex AI / Gemini
dotnet tool install -g nnke-platform-google

# Anthropic Claude  (Preview — requires Anthropic managed-agents Beta access)
dotnet tool install -g nnke-platform-anthropic

# Or install all three at once
dotnet tool install -g nnke-platform-all
```

Installing an adapter copies its assemblies into `~/.ananke/adapters/`
(Windows: `%USERPROFILE%\.ananke\adapters\`). On every run, `nnke-platform` probes that
directory and loads whatever adapters are present — no configuration file or restart required.

Update adapters independently:

```bash
dotnet tool update -g nnke-platform-azure
```

Uninstall cleanly using the adapter's own `--uninstall` flag before removing the tool:

```bash
nnke-platform-azure --uninstall
dotnet tool uninstall -g nnke-platform-azure
```

### Verify the Install

```bash
nnke-platform --help
nnke-platform capabilities --platform azure-ai
```

---

## Platform Setup

Each platform requires credentials before you can deploy. `nnke-platform login` stores them
per platform and does not share them with `nnke`.

### Azure AI Agent Service (`azure-ai`)

| Variable | Description |
|---|---|
| `AZURE_AI_ENDPOINT` | Your Azure AI Foundry endpoint URL |

```bash
nnke-platform login --platform azure-ai
# or set the env var directly:
export AZURE_AI_ENDPOINT=https://<your-resource>.cognitiveservices.azure.com/
```

Authentication uses the Azure credential chain (managed identity in CI, `az login` locally).
No additional API key is needed when using Entra-based auth.

### Google Vertex AI / Gemini (`vertex-ai`)

| Variable | Description |
|---|---|
| `GOOGLE_CLOUD_PROJECT` | GCP project ID |
| `GOOGLE_CLOUD_LOCATION` | Region (e.g. `us-central1`) — defaults to `us-central1` if unset |

```bash
nnke-platform login --platform vertex-ai
# or:
export GOOGLE_CLOUD_PROJECT=my-gcp-project
export GOOGLE_CLOUD_LOCATION=us-central1
```

Authentication uses Application Default Credentials (`gcloud auth application-default login` locally,
Workload Identity in GCP).

### Anthropic Claude (`claude`) — Preview

> **Beta:** `nnke-platform-anthropic` depends on the Anthropic managed-agents API, which is currently
> in Beta. Expect API surface changes. Check the
> [Anthropic adapter README](src/nnke-platform-anthropic/README.md) for Beta access requirements.

| Variable | Description |
|---|---|
| `ANTHROPIC_API_KEY` | Your Anthropic API key |

```bash
nnke-platform login --platform claude
# or:
export ANTHROPIC_API_KEY=sk-ant-...
```

### Verify credentials

```bash
nnke-platform whoami
```

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
| `nnke-platform analyze <file>` | Analyze manifest complexity and runtime trends |
| `nnke-platform lineage <cell> <file>` | Show federated ancestor/descendant tree for a cell |
| `nnke-platform mesh <file>` | Mesh status with platform, deployment ID, and remote health |
| `nnke-platform apoptosis [--auto] <file>` | Identify (and optionally teardown) idle or aged cells |
| `nnke-platform compare <cell> --across <p,p>` | Compare token/latency metrics across platforms |
| `nnke-platform events [--follow]` | Stream mesh and deployment events |
| `nnke-platform login --platform <p>` | Configure credentials for a platform |
| `nnke-platform whoami` | Show configured platform identities |
| `nnke-platform adapters list` | List all installed adapters with status and version |
| `nnke-platform adapters doctor` | Report unhealthy adapters with remediation hints (exits 2 if any are degraded) |

All commands support `--json` for machine-readable output and `--in-memory` for dry-run / test mode
(no disk state written).

---

## Supported Platforms

| Platform flag | Service | Adapter package | Status |
|---|---|---|---|
| `azure-ai` | Azure AI Agent Service (Azure AI Foundry) | `nnke-platform-azure` | Stable |
| `vertex-ai` | Google Vertex AI / Gemini Agent Platform | `nnke-platform-google` | Stable |
| `claude` | Anthropic Claude (managed agents) | `nnke-platform-anthropic` | **Preview** |
| `local` | Local in-process execution (no cloud) | *(none)* | Stable |

### Platform identifier aliases

Post-May-2026 platform names are accepted everywhere a platform flag is accepted:

| Alias | Resolves to | Note |
|---|---|---|
| `foundry` | `azure-ai` | Emitted as diagnostic `FED060` (warning) |
| `gemini-enterprise` | `vertex-ai` | Emitted as diagnostic `FED060` (warning) |

Existing manifests using `azure-ai` or `vertex-ai` continue to work unchanged.

---

## Local Design Loop *(v-next)*

> **Planned — not yet available.** The `nnke-platform up --emulate <platform>` verb and
> `Ananke.Federation.LocalEmulators` package are scoped to the next release (ADR CLI-7).
> For now, use `nnke run` or `nnke serve` for federation-free local execution.
> Manifests that declare platform-native capabilities will fail fast with diagnostic `FED061`
> and a hint pointing here.

When released, workflows that declare `ToolExecutionMode.PlatformNative` capabilities will be
able to run and be tested locally — no cloud credentials, no adapter installation, no network.

### Planned CLI interface

```bash
# Register a local emulated deployment (validates + writes a DeploymentRecord)
nnke-platform up --emulate azure-ai my-workflow.ananke.yml

# Then run the workflow locally via nnke
nnke serve my-workflow.ananke.yml --port 5000

# Or deploy with a local target in CI (no credentials, produces DeploymentRecord)
nnke-platform deploy my-workflow.ananke.yml --target local --emulate azure-ai
```

### Emulator tiers

| Tier | Behaviour | Examples |
|---|---|---|
| **Real** | Makes actual network calls | `web_search`, `web_fetch`, `bash`, `code_execution`, `text_editor`, `file_search` |
| **In-process** | Shared in-memory state, no I/O | `memory`, `memory_bank`, `memory_search` |
| **Stub** | Deterministic fixture responses | `bing_search`, `azure_ai_search`, `computer_use`, `image_generation`, `deep_research` |

The validator emits `FED062` (warning) for stub-tier capabilities so you know which tools
return fixture data rather than real results.

### Routing with `local-emulated:<platform>`

`HybridRouter` accepts three target styles:

| Target | Meaning |
|---|---|
| `"local"` | Run in-process, no emulation layer |
| `"azure-ai"` / `"vertex-ai"` / `"claude"` | Deploy to the named managed platform |
| `"local-emulated:azure-ai"` | Run locally through registered emulators, simulating the named platform |

This lets you pin individual cells to local emulation while the rest of the mesh deploys remotely.

### Diagnostic codes

| Code | Severity | Meaning |
|---|---|---|
| `FED060` | Warning | Platform alias resolved (e.g. `foundry → azure-ai`) |
| `FED061` | Error | `PlatformNative` capability declared but no executor registered |
| `FED062` | Warning | Capability covered by a stub — results are deterministic, not real |

---

## Deployment Profiles

Deployment profiles let you define platform-specific tool bindings in your `.ananke.yml`. The same
workflow runs locally or on any cloud target — only the tool wiring changes, not the workflow code.

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

Apply a profile at deploy time:

```bash
nnke-platform deploy my-workflow.ananke.yml --platform azure-ai --profile azure-ai
```

---

## Quick Start

```bash
# 1. Install the CLI and an adapter
dotnet tool install -g nnke-platform
dotnet tool install -g nnke-platform-azure

# 2. Set credentials
export AZURE_AI_ENDPOINT=https://<your-resource>.cognitiveservices.azure.com/
nnke-platform whoami

# 3. See what capabilities Azure AI supports
nnke-platform capabilities --platform azure-ai

# 4. Validate your manifest
nnke-platform validate my-workflow.ananke.yml --platform azure-ai

# 5. List deployment profiles
nnke-platform profiles my-workflow.ananke.yml

# 6. Deploy
nnke-platform deploy my-workflow.ananke.yml --platform azure-ai --profile azure-ai

# 7. Check status
nnke-platform status --deployment-id <id>

# 8. Stream live events
nnke-platform events --follow
```

---

## Credentials

Before deploying, configure credentials for the target platform:

```bash
nnke-platform login --platform azure-ai
nnke-platform whoami
```

Credentials are stored per platform and are not shared with `nnke`.

---

## Mesh & Federation

`nnke-platform` has a federated view of the running cell mesh — it knows about remote deployments,
not just local topology. Use it when you need cross-platform visibility:

```bash
# Full lineage: which workflows spawned this cell and what it spawned
nnke-platform lineage bookstore-orders my-workflow.ananke.yml

# Mesh status including platform, deployment ID, and remote health
nnke-platform mesh my-workflow.ananke.yml
```

For local-only mesh inspection (no cloud SDK required), use `nnke mesh` instead.

---

## Machine-Readable Output

Every command supports `--json` for structured output that AI tools, CI pipelines, and scripts can consume directly:

```bash
nnke-platform status --json
nnke-platform capabilities --platform azure-ai --json
nnke-platform trends --deployment-id <id> --json
```

---

## Related Pages

- [nnke Tools Overview](nnke-tools.md)
- [nnke Tool Companion](nnke-tool.md) — local design, scaffolding, and diagnostics
- [09 — Distributed Systems](09-distributed.md) — Redis, MQTT, agent handoff
- [12 — MCP & Interop](12-mcp-and-interop.md) — MCP server, A2A protocol
- [16 — Agentic Patterns](16-agentic-patterns.md) — Smart Tool Router, organic patterns
- [FAQ — Organic Colony & Cell Division](faq/organics.md) — division, apoptosis, mesh lifecycle

---

← [Back to Welcome](welcome.md)
