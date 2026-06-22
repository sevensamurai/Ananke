# Ananke.Federation.Anthropic — Architecture

> Claude / Anthropic platform adapter for `Ananke.Federation`.

## Role

Provides the Anthropic-specific implementations of the federation contracts:
deployer, validator, credential provider, workflow host, remote cell monitor, model
mapper, tool schema translator, and system prompt compiler for Claude Managed Agents.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `ClaudeDeployer` — the `IFederationDeployer` implementation; deploy / teardown Claude
   Managed Agents (see platform adapter status for current limits) — `src/Ananke.Federation.Anthropic/ClaudeDeployer.cs`
2. `ClaudeCredentialProvider` — resolves the Anthropic API key; `IFederationCredentialProvider`
   implementation — `src/Ananke.Federation.Anthropic/ClaudeCredentialProvider.cs`
3. `ClaudeWorkflowHost` — manages Claude-hosted cell lifecycle (spawn = register remote,
   stop = deregister) — `src/Ananke.Federation.Anthropic/ClaudeWorkflowHost.cs`

---

## Dependencies

| Dependency | Why |
|---|---|
| `Ananke.Federation` | Implements all federation interfaces (`IFederationDeployer`, `IPlatformValidator`, `IFederationCredentialProvider`, `IRemoteCellMonitor`, `IWorkflowHost`, `ISystemPromptCompiler`, `IModelMapper`) |
| `Ananke.Orchestration.Anthropic` | `ClaudeDeployer` uses `AnthropicAgentModel` internals and the Anthropic SDK for any live agent API calls |

---

## Type Inventory

| Type | Implements | Purpose |
|---|---|---|
| `ClaudeDeployer` | `IFederationDeployer` | Deploy / teardown Claude Managed Agents. **See platform adapter status below.** |
| `ClaudeValidator` | `IPlatformValidator` | Live validation — credentials, model availability, Claude-specific tool constraints |
| `ClaudeCredentialProvider` | `IFederationCredentialProvider` | Resolves Anthropic API key. `ValidateAsync` **not yet implemented** (throws) |
| `ClaudeWorkflowHost` | `IWorkflowHost` | Manages Claude-hosted cell lifecycle (spawn = register remote, stop = deregister) |
| `ClaudeRemoteCellMonitor` | `IRemoteCellMonitor` | Polls Claude Managed Agent health and execution metrics |
| `ClaudeModelMapper` | `IModelMapper` | Maps Ananke model aliases to Claude model identifiers |
| `ClaudeToolSchemaTranslator` | — | Translates `ToolDefinition` to Claude tool input schema format |
| `ClaudeSystemPromptCompiler` | `ISystemPromptCompiler` | Compiles `WorkflowManifest` into a Claude-flavoured system prompt |

Platform identifier string: **`"claude"`**

---

## Deployer Lifecycle

```
ClaudeDeployer.ValidateAsync(manifest, toolKit)
  → ClaudeValidator.ValidateAsync()      (live: credentials + model + tool constraints)
  → DeployabilityValidator.Validate()    (offline: structural)
  → DeployabilityReport

ClaudeDeployer.DeployAsync(manifest, toolKit, options)
  → ClaudeCredentialProvider.GetCredentialAsync("claude")
  → registers DeploymentRecord in IDeploymentRegistry
  → ⚠ throws NotImplementedException    (actual Claude API deployment not yet implemented)

ClaudeDeployer.TeardownAsync(deploymentId)
  → ⚠ throws NotImplementedException    (not yet implemented)
```

---

## Platform Adapter Status

| Capability | Status | Notes |
|---|---|---|
| Offline structural validation | Supported | `ClaudeValidator` checks credentials, model availability, tool constraints |
| Credential resolution (`GetCredentialAsync`) | Supported | Resolves Anthropic API key |
| Credential validation (`ValidateAsync`) | **Unsupported** | Default DIM throws `NotImplementedException` |
| Deploy | **Preview / Unsupported** | `DeployAsync` registers a deployment record then throws — actual Claude agent lifecycle API not yet integrated |
| Teardown | **Unsupported** | `TeardownAsync` throws `NotImplementedException` |
| Remote cell health monitoring | Supported | `ClaudeRemoteCellMonitor.GetHealthAsync` / `GetMetricsAsync` implemented |
| Model mapping | Supported | `ClaudeModelMapper` covers claude-3-* and claude-3-5-* aliases |
| Tool schema translation | Supported | `ClaudeToolSchemaTranslator` |
| System prompt compilation | Supported | `ClaudeSystemPromptCompiler` |

> **Note:** The deploy and teardown paths are intentionally unfinished. The Claude Managed Agents API surface was not stable at time of implementation. These methods are marked `Preview` — do not use in production. Track progress via the issue linked in the `ClaudeDeployer` source.

---

## Extension Points

Swap any type by registering your own implementation of the corresponding interface in `Ananke.Federation` before building `FederatedWorkflowHost`.
