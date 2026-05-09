# Ananke.Federation.Anthropic

Claude Managed Agents adapter for Ananke Federation. Deploy Ananke workflow manifests
to [Anthropic Claude](https://docs.anthropic.com/en/docs/agents-and-tools/tool-use),
monitor running managed agents, and route executions through Claude-hosted models.

> **⚠️ Preview — depends on Anthropic Beta API**
>
> `ClaudeDeployer` and `ClaudeManagedAgentsClient` call the
> [Anthropic Managed Agents Beta](https://docs.anthropic.com/en/docs/agents-and-tools/tool-use)
> endpoints (`/v1/environments`, `/v1/agents`) and send the
> `anthropic-beta: agents-2025-05-14` header on every request.
> The Beta API shape may change without notice. When the official
> [Anthropic .NET SDK](https://github.com/anthropics/anthropic-sdk-dotnet) exposes
> typed managed-agents support, `ClaudeManagedAgentsClient` will be removed and
> replaced with the SDK equivalent. Pin your dependency on this package to a
> specific minor version until the API is stable.

## What this package provides

| Type | Purpose |
|---|---|
| `ClaudeDeployer` | `IFederationDeployer` — creates/updates/deletes Claude managed agents from a `WorkflowManifest` |
| `ClaudeWorkflowHost` | `IWorkflowHost` — manages cells as Claude managed agents; `Start` deploys, `StopAsync` tears down |
| `ClaudeRemoteCellMonitor` | `IRemoteCellMonitor` — polls health and metrics from Claude managed agent deployments |
| `ClaudeCredentialProvider` | `IFederationCredentialProvider` — resolves the Anthropic API key from config or `ANTHROPIC_API_KEY` env var |
| `ClaudeModelMapper` | `IModelMapper` — maps manifest model references to Claude model identifiers; Anthropic models pass through directly |
| `ClaudeToolSchemaTranslator` | Translates `ToolDefinition`s to Claude tool_use JSON fragments (custom tools and built-in tools) |
| `ClaudeSystemPromptCompiler` | `ISystemPromptCompiler` — compiles an XML-structured system prompt from a manifest for Claude |
| `ClaudeValidator` | `IPlatformValidator` — live credential and tool compatibility checks against the Anthropic API |

## Authentication

This adapter authenticates with an **API key**. Pass it directly or rely on the
`ANTHROPIC_API_KEY` environment variable:

```csharp
// From environment variable (recommended for CI/production)
var credentials = new ClaudeCredentialProvider();

// Or explicit key
var credentials = new ClaudeCredentialProvider(apiKey: "sk-ant-...");
```

## Quick start

```csharp
var credentials = new ClaudeCredentialProvider();
var registry = new InMemoryDeploymentRegistry();

var deployer = new ClaudeDeployer(credentials, registry);
DeploymentRecord record = await deployer.DeployAsync(manifest, toolKit, new DeployOptions
{
    DeploymentId = "my-agent-v1",
    Platform = "claude"
});
```

## Tool translation

`ClaudeToolSchemaTranslator` handles both custom tools and Claude's built-in tools.
Use the `Capabilities` constants for discoverability:

```csharp
var tool = ToolBuilder.Define("web_search")
    .PlatformNative("claude", ClaudeToolSchemaTranslator.Capabilities.WebSearch)
    .Build();
```

Well-known Claude built-in capabilities: `web_search`, `code_execution`,
`computer_use`, `text_editor`, `bash`. Any other string is passed through verbatim.

## Model mapping

`ClaudeModelMapper` maps any Ananke model reference to its Claude equivalent.
Anthropic models pass through unchanged; OpenAI and Google models are mapped to
the nearest Claude equivalent (`claude-sonnet-4`, `claude-haiku-3-5`, etc.).

## System prompt format

`ClaudeSystemPromptCompiler` emits XML-structured prompts following Anthropic's
recommended format for agentic workflows, including `<tools>`, `<context>`, and
`<instructions>` sections derived from the manifest.

## Monitoring

`ClaudeRemoteCellMonitor` currently returns baseline health metrics. Anthropic
monitoring API integration for latency, token usage, and error rate trend data
will be added when the API becomes available.

## Platform adapter status

| Method | Status | Notes |
|---|---|---|
| `ClaudeDeployer.ValidateAsync` | ✅ Implemented | Live credential + tool compatibility checks |
| `ClaudeDeployer.DeployAsync` | ✅ Preview | Calls Beta endpoints via `ClaudeManagedAgentsClient`; pinned to `agents-2025-05-14` |
| `ClaudeDeployer.TeardownAsync` | ✅ Preview | Deletes agents then environment via Beta endpoints |
| `ClaudeRemoteCellMonitor.GetHealthAsync` | ⚠️ Stub | Returns safe defaults; Anthropic monitoring API pending |
| `ClaudeRemoteCellMonitor.GetMetricsAsync` | ⚠️ Stub | Returns safe defaults; Anthropic monitoring API pending |
| `ClaudeWorkflowHost.StartAsync` | ✅ Preview | Delegates to `ClaudeDeployer.DeployAsync` |
| `ClaudeWorkflowHost.StopAsync` | ✅ Preview | Delegates to `ClaudeDeployer.TeardownAsync` |

**Preview methods** call `ClaudeManagedAgentsClient` which targets the Anthropic
Beta REST API directly (no SDK dependency). Once the official SDK stabilises this
surface, the internal client will be dropped and these methods will be promoted to
`✅ Implemented`.
