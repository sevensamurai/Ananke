# nnke-platform

CLI tool for Ananke federation — validate, deploy, and manage workflow deployments to cloud platforms.

## Install

```bash
dotnet tool install -g nnke-platform
```

## Commands

| Command | Description |
|---|---|
| `nnke-platform validate <file> --platform <p>` | Validate a manifest's deployability to a target platform |
| `nnke-platform capabilities [--platform <p>]` | List known platform-native tool capabilities |
| `nnke-platform eval <file>` | Score a manifest against candidate platforms and recommend the best fit |
| `nnke-platform profiles <file> [profile]` | List or inspect deployment profiles in a manifest |
| `nnke-platform deploy <file> --platform <p>` | Deploy a workflow to a target platform |
| `nnke-platform status [--deployment-id <id>]` | Show deployment status |
| `nnke-platform teardown --deployment-id <id>` | Tear down a deployed workflow |
| `nnke-platform trends [--deployment-id <id>]` | Show metrics trends for deployments |
| `nnke-platform analyze <file>` | Analyze manifest complexity + runtime trends |
| `nnke-platform lineage <cell> <file>` | Show federated ancestor/descendant tree for a cell |
| `nnke-platform mesh <file>` | Mesh status with platform, deploymentId, and remote health |
| `nnke-platform apoptosis [--auto] <file>` | Identify (and optionally teardown) idle/aged cells |
| `nnke-platform compare <cell> --across <p,p>` | Compare token/latency metrics across platforms |
| `nnke-platform events [--follow]` | Stream mesh and deployment events |
| `nnke-platform login --platform <p>` | Configure credentials for a platform |
| `nnke-platform whoami` | Show configured platform identities |
| `nnke-platform adapters list` | List adapters found in the probe directory and their load status |
| `nnke-platform adapters doctor` | Report adapter health — version mismatches, missing manifests, load failures |

All commands support `--json` for machine-readable output.

Exit codes: `0` success, `1` usage or I/O error (missing file, unknown platform or profile),
`2` the command ran but the answer is negative (manifest not deployable, adapter unhealthy).

## Quick Start

```bash
# Check what capabilities Azure AI supports
nnke-platform capabilities --platform azure-ai

# Validate a manifest for Azure AI deployment
nnke-platform validate my-workflow.ananke.yml --platform azure-ai

# Validate with a specific deployment profile
nnke-platform validate my-workflow.ananke.yml --platform azure-ai --profile azure-ai

# List profiles in a manifest
nnke-platform profiles my-workflow.ananke.yml

# Deploy (once platform adapters are wired)
nnke-platform deploy my-workflow.ananke.yml --platform azure-ai --profile azure-ai
```

## Deployment Profiles

Define platform-specific tool bindings in your `.ananke.yml`:

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
  # gemini-agent-platform is the current name; vertex-ai is accepted as a back-compat alias
  vertex-ai:
    tools:
      search: { platform: google_search }
      code:   { platform: code_execution }
```

Same workflow, different tool wiring per environment. No code changes needed.

## Architecture boundary

`nnke-platform` is the **federation ops tool** — it carries cloud SDK dependencies.

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

For developer scaffolding and local inspection, install `nnke`:
```bash
dotnet tool install -g nnke
```
