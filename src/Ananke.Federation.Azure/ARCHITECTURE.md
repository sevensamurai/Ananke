# Ananke.Federation.Azure — Architecture

> Azure AI Agent Service platform adapter for `Ananke.Federation`.

## Role

Provides the Azure-specific implementations of the federation contracts:
deployer, validator, credential provider, workflow host, remote cell monitor, model
mapper, tool schema translator, and system prompt compiler for Azure AI Agent Service.

---

## Dependencies

| Dependency | Why |
|---|---|
| `Ananke.Federation` | Implements all federation interfaces (`IFederationDeployer`, `IPlatformValidator`, `IFederationCredentialProvider`, `IRemoteCellMonitor`, `IWorkflowHost`, `ISystemPromptCompiler`, `IModelMapper`) |
| `Ananke.Orchestration.OpenAI` | Azure AI Agent Service uses the OpenAI wire protocol; `AzureAgentDeployer` builds on the OpenAI SDK with Azure credentials |

---

## Type Inventory

| Type | Implements | Purpose |
|---|---|---|
| `AzureAgentDeployer` | `IFederationDeployer` | Deploy / teardown Azure AI Agent Service agents |
| `AzureAgentValidator` | `IPlatformValidator` | Live validation — credentials, model availability, Azure-specific tool constraints |
| `AzureAgentCredentialProvider` | `IFederationCredentialProvider` | Resolves Azure credentials (`TokenCredential`, connection string, or API key) |
| `AzureWorkflowHost` | `IWorkflowHost` | Manages Azure-hosted cell lifecycle |
| `AzureRemoteCellMonitor` | `IRemoteCellMonitor` | Polls Azure AI Agent health and execution metrics |
| `AzureModelMapper` | `IModelMapper` | Maps Ananke model aliases to Azure OpenAI deployment names |
| `AzureToolSchemaTranslator` | — | Translates `ToolDefinition` to Azure AI tool schema format |
| `AzureSystemPromptCompiler` | `ISystemPromptCompiler` | Compiles `WorkflowManifest` into an Azure AI Agent system prompt |

Platform identifier string: **`"azure-ai"`**

---

## Deployer Lifecycle

```
AzureAgentDeployer.ValidateAsync(manifest, toolKit)
  → AzureAgentValidator.ValidateAsync()    (live: credentials + model + tool constraints)
  → DeployabilityValidator.Validate()      (offline: structural)
  → DeployabilityReport

AzureAgentDeployer.DeployAsync(manifest, toolKit, options)
  → AzureAgentCredentialProvider.GetCredentialAsync("azure-ai")
  → translate manifest → Azure AI Agent definition
  → translate toolKit  → Azure AI tool schema (AzureToolSchemaTranslator)
  → compile system prompt (AzureSystemPromptCompiler)
  → call Azure AI Agent Service API to create agent
  → IDeploymentRegistry.RegisterAsync(DeploymentRecord { Platform="azure-ai", ... })
  → return DeploymentRecord

AzureAgentDeployer.TeardownAsync(deploymentId)
  → IDeploymentRegistry.GetAsync(deploymentId)
  → AzureAgentCredentialProvider.GetCredentialAsync("azure-ai")
  → call Azure AI Agent Service API to delete agent
  → IDeploymentRegistry.UpdateStatusAsync(deploymentId, Stopped)
```

---

## Platform Adapter Status

| Capability | Status | Notes |
|---|---|---|
| Offline structural validation | Supported | `AzureAgentValidator` checks credentials, model availability, tool constraints |
| Credential resolution (`GetCredentialAsync`) | Supported | `TokenCredential` (Managed Identity, DefaultAzureCredential) and API key |
| Credential validation (`ValidateAsync`) | **Unsupported** | `AzureAgentCredentialProvider` does not override the default DIM — throws `NotImplementedException` |
| Deploy | Supported | Full Azure AI Agent Service create path implemented |
| Teardown | Supported | Azure AI Agent Service delete path implemented |
| Remote cell health monitoring | Supported | `AzureRemoteCellMonitor.GetHealthAsync` / `GetMetricsAsync` |
| Model mapping | Supported | `AzureModelMapper` covers gpt-4o, gpt-4o-mini, and o-series aliases |
| Tool schema translation | Supported | `AzureToolSchemaTranslator` |
| System prompt compilation | Supported | `AzureSystemPromptCompiler` |

---

## Extension Points

Swap any type by registering your own implementation of the corresponding interface in `Ananke.Federation` before building `FederatedWorkflowHost`.
