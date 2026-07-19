# Ananke.Federation.Azure

Azure AI Agent Service adapter for Ananke Federation. Deploy Ananke workflow manifests
to [Azure AI Foundry](https://learn.microsoft.com/en-us/azure/ai-services/agents/),
monitor running agents, and route executions through Azure-hosted models.

## What this package provides

| Type | Purpose |
|---|---|
| `AzureAgentDeployer` | `IFederationDeployer` — creates/updates/deletes Azure AI agents from a `WorkflowManifest` via `AgentAdministrationClient` |
| `AzureWorkflowHost` | `IWorkflowHost` — manages cells as Azure AI agents; `Start` deploys, `StopAsync` tears down |
| `AzureRemoteCellMonitor` | `IRemoteCellMonitor` — polls health and metrics from Azure AI agent deployments |
| `AzureAgentCredentialProvider` | `IFederationCredentialProvider` — resolves `AgentAdministrationClient` via `DefaultAzureCredential` (Entra ID) |
| `AzureModelMapper` | `IModelMapper` — maps manifest model references to Azure-hosted model deployment names; OpenAI models pass through directly |
| `AzureToolSchemaTranslator` | Translates `ToolDefinition`s to `DeclarativeAgentDefinition` JSON fragments (`function`, `code_interpreter`, `bing_grounding`, `file_search`) |
| `AzureSystemPromptCompiler` | `ISystemPromptCompiler` — compiles a system prompt from a manifest for Azure AI Agent Service |
| `AzureAgentValidator` | `IPlatformValidator` — live credential and model availability checks against Azure APIs |

## Authentication

This adapter uses **Entra ID** via `DefaultAzureCredential`. Provide your Azure AI
Foundry project endpoint:

```csharp
var credentials = new AzureAgentCredentialProvider(
    new Uri("https://<resource>.services.ai.azure.com/api/projects/<project>"));
```

In local development, `az login` or a service principal set via environment variables
(`AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, `AZURE_TENANT_ID`) both work.

## Quick start

```csharp
var credentials = new AzureAgentCredentialProvider(new Uri("https://..."));
var registry = new InMemoryDeploymentRegistry();

var deployer = new AzureAgentDeployer(credentials, registry);
DeploymentRecord record = await deployer.DeployAsync(manifest, toolKit, new DeployOptions
{
    DeploymentId = "my-agent-v1",
    Platform = "azure-ai"
});
```

## Platform-native tools

Use `ToolBuilder` + `AzureToolSchemaTranslator.Capabilities` constants for
discoverable platform-native tool wiring:

```csharp
var tool = ToolBuilder.Define("web_search")
    .PlatformNative("azure-ai", AzureToolSchemaTranslator.Capabilities.BingGrounding)
    .Build();
```

Platform-native capabilities not in the `Capabilities` class are passed through
verbatim — Azure validates them at deploy time.

## Model mapping

`AzureModelMapper` maps any Ananke model reference to its Azure-hosted equivalent.
OpenAI models pass through unchanged; other providers are mapped to the nearest
OpenAI equivalent available on Azure AI Foundry.

## Monitoring

`AzureRemoteCellMonitor` currently returns baseline health metrics. Azure Monitor
integration for token/latency/error trend data will be added in a future release.
