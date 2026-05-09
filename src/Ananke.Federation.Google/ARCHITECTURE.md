# Ananke.Federation.Google — Architecture

> Gemini Enterprise Agent Platform / Vertex AI Agent Runtime adapter for `Ananke.Federation`.

## Role

Provides the Google-specific implementations of the federation contracts:
deployer, validator, credential provider, workflow host, remote cell monitor, model
mapper, tool schema translator, and system prompt compiler for Vertex AI Agent Runtime /
Gemini Enterprise Agent Platform.

---

## Dependencies

| Dependency | Why |
|---|---|
| `Ananke.Federation` | Implements all federation interfaces (`IFederationDeployer`, `IPlatformValidator`, `IFederationCredentialProvider`, `IRemoteCellMonitor`, `IWorkflowHost`, `ISystemPromptCompiler`, `IModelMapper`) |

---

## Type Inventory

| Type | Implements | Purpose |
|---|---|---|
| `VertexAIDeployer` | `IFederationDeployer` | Deploy / teardown Gemini Enterprise Agent Platform agents |
| `VertexAIValidator` | `IPlatformValidator` | Live validation — credentials, model availability, Gemini-specific tool constraints |
| `VertexAICredentialProvider` | `IFederationCredentialProvider` | Resolves Google credentials (service-account JSON or ADC). `ValidateAsync` not yet overridden — falls back to default DIM (throws) |
| `VertexAIWorkflowHost` | `IWorkflowHost` | Manages Vertex AI hosted cell lifecycle |
| `VertexAIRemoteCellMonitor` | `IRemoteCellMonitor` | Polls Vertex AI Agent health and execution metrics |
| `VertexAIModelMapper` | `IModelMapper` | Maps Ananke model aliases to Gemini model identifiers |
| `VertexAIToolSchemaTranslator` | — | Translates `ToolDefinition` to Vertex AI / FunctionDeclaration schema format |
| `VertexAISystemPromptCompiler` | `ISystemPromptCompiler` | Compiles `WorkflowManifest` into a Gemini system instruction prompt |
| `AgentPlatformConstants` | — | Platform identifier constant and shared string literals |
| `RemoteCellMonitorOptions` | — | Configuration options for poll interval and metric window |

Platform identifier string: **`AgentPlatformConstants.Platform`** (`"vertex-ai"`)

---

## Deployer Lifecycle

```
VertexAIDeployer.ValidateAsync(manifest, toolKit)
  → VertexAIValidator.ValidateAsync()     (live: credentials + model + tool constraints)
  → DeployabilityValidator.Validate()     (offline: structural)
  → DeployabilityReport

VertexAIDeployer.DeployAsync(manifest, toolKit, options)
  → VertexAICredentialProvider.GetCredentialAsync("vertex-ai")
  → translate manifest → Vertex AI Agent definition
  → translate toolKit  → FunctionDeclaration schema (VertexAIToolSchemaTranslator)
  → compile system instruction (VertexAISystemPromptCompiler)
  → call Vertex AI Agent Runtime API to create agent
  → IDeploymentRegistry.RegisterAsync(DeploymentRecord { Platform="vertex-ai", ... })
  → return DeploymentRecord

VertexAIDeployer.TeardownAsync(deploymentId)
  → IDeploymentRegistry.GetAsync(deploymentId)
  → VertexAICredentialProvider.GetCredentialAsync("vertex-ai")
  → call Vertex AI Agent Runtime API to delete agent
  → IDeploymentRegistry.UpdateStatusAsync(deploymentId, Stopped)
```

---

## Platform Adapter Status

| Capability | Status | Notes |
|---|---|---|
| Offline structural validation | Supported | `VertexAIValidator` checks credentials, model availability, tool constraints |
| Credential resolution (`GetCredentialAsync`) | Supported | Service-account JSON or Application Default Credentials |
| Credential validation (`ValidateAsync`) | **Unsupported** | `VertexAICredentialProvider` does not override the default DIM — throws `NotImplementedException` |
| Deploy | Supported | Full Vertex AI Agent Runtime create path implemented |
| Teardown | Supported | Vertex AI Agent Runtime delete path implemented |
| Remote cell health monitoring | Supported | `VertexAIRemoteCellMonitor.GetHealthAsync` / `GetMetricsAsync` |
| Model mapping | Supported | `VertexAIModelMapper` covers gemini-2.0-* and gemini-1.5-* aliases |
| Tool schema translation | Supported | `VertexAIToolSchemaTranslator` (FunctionDeclaration format) |
| System prompt compilation | Supported | `VertexAISystemPromptCompiler` (system instruction format) |

---

## Extension Points

Swap any type by registering your own implementation of the corresponding interface in `Ananke.Federation` before building `FederatedWorkflowHost`. Use `RemoteCellMonitorOptions` to tune poll interval and metrics window.
