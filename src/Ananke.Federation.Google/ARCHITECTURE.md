# Ananke.Federation.Google — Architecture

> Gemini Enterprise Agent Platform / Vertex AI Agent Runtime adapter for `Ananke.Federation`.

## Role

Provides the Google-specific implementations of the federation contracts:
deployer, validator, credential provider, workflow host, remote cell monitor, model
mapper, tool schema translator, and system prompt compiler for Vertex AI Agent Runtime /
Gemini Enterprise Agent Platform.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `AgentRuntimeDeployer` — the `IFederationDeployer` implementation; full deploy / teardown
   path against Gemini Enterprise Agent Platform agents — `src/Ananke.Federation.Google/AgentRuntimeDeployer.cs`
2. `AgentRuntimeCredentialProvider` — resolves Google credentials (service-account JSON or ADC)
   — `src/Ananke.Federation.Google/AgentRuntimeCredentialProvider.cs`
3. `AgentRuntimeWorkflowHost` — manages Vertex AI hosted cell lifecycle — `src/Ananke.Federation.Google/AgentRuntimeWorkflowHost.cs`

---

## Dependencies

| Dependency | Why |
|---|---|
| `Ananke.Federation` | Implements all federation interfaces (`IFederationDeployer`, `IPlatformValidator`, `IFederationCredentialProvider`, `IRemoteCellMonitor`, `IWorkflowHost`, `ISystemPromptCompiler`, `IModelMapper`) |

---

## Type Inventory

| Type | Implements | Purpose |
|---|---|---|
| `AgentRuntimeDeployer` | `IFederationDeployer` | Deploy / teardown Gemini Enterprise Agent Platform agents |
| `AgentRuntimeValidator` | `IPlatformValidator` | Live validation — credentials, model availability, Gemini-specific tool constraints |
| `AgentRuntimeCredentialProvider` | `IFederationCredentialProvider` | Resolves Google credentials (service-account JSON or ADC). `ValidateAsync` not yet overridden — falls back to default DIM (throws) |
| `AgentRuntimeWorkflowHost` | `IWorkflowHost` | Manages Vertex AI hosted cell lifecycle |
| `AgentRuntimeRemoteCellMonitor` | `IRemoteCellMonitor` | Polls Vertex AI Agent health and execution metrics |
| `AgentRuntimeModelMapper` | `IModelMapper` | Maps Ananke model aliases to Gemini model identifiers |
| `AgentRuntimeToolSchemaTranslator` | — | Translates `ToolDefinition` to Vertex AI / FunctionDeclaration schema format |
| `AgentRuntimeSystemPromptCompiler` | `ISystemPromptCompiler` | Compiles `WorkflowManifest` into a Gemini system instruction prompt |
| `AgentPlatformConstants` | — | Platform identifier constant and shared string literals |
| `RemoteCellMonitorOptions` | — | Configuration options for poll interval and metric window |

Platform identifier string: **`AgentPlatformConstants.Platform`** (`"vertex-ai"`)

---

## Deployer Lifecycle

```
AgentRuntimeDeployer.ValidateAsync(manifest, toolKit)
  → AgentRuntimeValidator.ValidateAsync()     (live: credentials + model + tool constraints)
  → DeployabilityValidator.Validate()     (offline: structural)
  → DeployabilityReport

AgentRuntimeDeployer.DeployAsync(manifest, toolKit, options)
  → AgentRuntimeCredentialProvider.GetCredentialAsync("vertex-ai")
  → translate manifest → Vertex AI Agent definition
  → translate toolKit  → FunctionDeclaration schema (AgentRuntimeToolSchemaTranslator)
  → compile system instruction (AgentRuntimeSystemPromptCompiler)
  → call Vertex AI Agent Runtime API to create agent
  → IDeploymentRegistry.RegisterAsync(DeploymentRecord { Platform="vertex-ai", ... })
  → return DeploymentRecord

AgentRuntimeDeployer.TeardownAsync(deploymentId)
  → IDeploymentRegistry.GetAsync(deploymentId)
  → AgentRuntimeCredentialProvider.GetCredentialAsync("vertex-ai")
  → call Vertex AI Agent Runtime API to delete agent
  → IDeploymentRegistry.UpdateStatusAsync(deploymentId, Stopped)
```

---

## Platform Adapter Status

| Capability | Status | Notes |
|---|---|---|
| Offline structural validation | Supported | `AgentRuntimeValidator` checks credentials, model availability, tool constraints |
| Credential resolution (`GetCredentialAsync`) | Supported | Service-account JSON or Application Default Credentials |
| Credential validation (`ValidateAsync`) | **Unsupported** | `AgentRuntimeCredentialProvider` does not override the default DIM — throws `NotImplementedException` |
| Deploy | Supported | Full Vertex AI Agent Runtime create path implemented |
| Teardown | Supported | Vertex AI Agent Runtime delete path implemented |
| Remote cell health monitoring | Supported | `AgentRuntimeRemoteCellMonitor.GetHealthAsync` / `GetMetricsAsync` |
| Model mapping | Supported | `AgentRuntimeModelMapper` covers gemini-2.0-* and gemini-1.5-* aliases |
| Tool schema translation | Supported | `AgentRuntimeToolSchemaTranslator` (FunctionDeclaration format) |
| System prompt compilation | Supported | `AgentRuntimeSystemPromptCompiler` (system instruction format) |

---

## Extension Points

Swap any type by registering your own implementation of the corresponding interface in `Ananke.Federation` before building `FederatedWorkflowHost`. Use `RemoteCellMonitorOptions` to tune poll interval and metrics window.
