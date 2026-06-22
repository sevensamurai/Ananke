# Ananke.Federation.Google

> **Platform update (April 2026):** Google has rebranded Vertex AI as
> **[Gemini Enterprise Agent Platform](https://cloud.google.com/products/gemini-enterprise-agent-platform)**.
> All Vertex AI services and roadmap evolutions are now delivered exclusively through
> Agent Platform. The `VertexAI*` class names in this package are preserved for
> backwards compatibility; new platform capabilities are documented below under
> [New platform capabilities](#new-platform-capabilities-agent-platform-ga).

Gemini Enterprise Agent Platform adapter for Ananke Federation. Deploy Ananke workflow
manifests to [Agent Runtime](https://docs.cloud.google.com/gemini-enterprise-agent-platform/build/runtime),
monitor running agents via Agent Observability, and route executions through Gemini-hosted models.

## What this package provides

| Type | Purpose |
|---|---|
| `VertexAIDeployer` | `IFederationDeployer` — creates/updates/deletes agents via Agent Runtime REST API from a `WorkflowManifest` |
| `VertexAIWorkflowHost` | `IWorkflowHost` — manages cells as Agent Runtime agents; `StartAsync` deploys, `StopAsync` tears down |
| `VertexAIRemoteCellMonitor` | `IRemoteCellMonitor` — polls health and metrics via Cloud Trace v2 and Cloud Monitoring v3 |
| `VertexAICredentialProvider` | `IFederationCredentialProvider` — resolves a `Google.GenAI.Client` via Application Default Credentials (ADC) |
| `VertexAIModelMapper` | `IModelMapper` — maps manifest model references to Gemini model identifiers; Google models pass through directly |
| `VertexAIToolSchemaTranslator` | Translates `ToolDefinition`s to Agent Platform `Tool` instances (Function Declarations, Extensions, OpenAPI specs) |
| `VertexAISystemPromptCompiler` | `ISystemPromptCompiler` — compiles a system prompt from a manifest for Agent Runtime |
| `VertexAIValidator` | `IPlatformValidator` — live credential, model availability, and tool compatibility checks via Google Cloud APIs |
| `RemoteCellMonitorOptions` | Configures health thresholds (error rate, latency) and look-back window for `VertexAIRemoteCellMonitor` |

## Authentication

This adapter uses **Application Default Credentials (ADC)**. Provide your Google Cloud
project ID and region:

```csharp
var credentials = new VertexAICredentialProvider(
    project: "my-gcp-project",
    location: "us-central1");
```

In local development, run `gcloud auth application-default login`.
In CI/Cloud Run, the attached service account is picked up automatically.

The service account requires these IAM roles:

| Role | Used by |
|---|---|
| `roles/aiplatform.user` | `VertexAIDeployer` — Agent Runtime agents.create / agents.delete |
| `roles/cloudtrace.viewer` | `VertexAIRemoteCellMonitor` — Cloud Trace read |
| `roles/monitoring.viewer` | `VertexAIRemoteCellMonitor` — Cloud Monitoring read |

## Quick start

### Deploy a workflow

```csharp
var credentials = new VertexAICredentialProvider("my-gcp-project", "us-central1");
var registry    = new InMemoryDeploymentRegistry();
var deployer    = new VertexAIDeployer(credentials, registry);

DeploymentRecord record = await deployer.DeployAsync(manifest, toolKit, new DeployOptions
{
    Platform = "vertex-ai"   // or "gemini-agent-platform" — both accepted
});
// record.Status == DeploymentStatus.Active
// record.PlatformResourceId == "projects/my-gcp-project/locations/us-central1/agents/<id>"
```

### Monitor a deployment

```csharp
var monitor = new VertexAIRemoteCellMonitor(
    project: "my-gcp-project",
    options: new RemoteCellMonitorOptions
    {
        LookBackMinutes   = 30,
        ErrorRateThreshold = 0.05,   // unhealthy above 5% errors
        LatencyThresholdMs = 5_000   // unhealthy above 5 s avg latency
    });

RemoteCellHealth  health  = await monitor.GetHealthAsync(record.DeploymentId);
RemoteCellMetrics metrics = await monitor.GetMetricsAsync(record.DeploymentId);
```

## Tool translation

`VertexAIToolSchemaTranslator` groups tools by execution mode:

| Ananke mode | Agent Platform equivalent |
|---|---|
| `Callback`, `Mcp`, `OpenApi` | Function Declarations |
| `OpenApi` (with endpoint) | OpenAPI spec tool |
| `PlatformNative` | Platform Extension or pass-through |

`Local` tools throw `InvalidOperationException` — remote cells cannot call back
to the supervisor process (supervisor-only hybrid constraint).

## Model mapping

`VertexAIModelMapper` maps any Ananke model reference to its Gemini equivalent.
Google models pass through unchanged; OpenAI and Anthropic models are mapped to
the nearest Gemini equivalent.

**Current default mappings (updated for Agent Platform GA):**

| Source model | Mapped Gemini model |
|---|---|
| High-intelligence / GPT-4-class | `gemini-3.1-pro` |
| Fast / GPT-4o-mini-class | `gemini-3.1-flash` |
| Anthropic Claude Opus | `gemini-3.1-pro` |
| Anthropic Claude Sonnet / Haiku | `gemini-3.1-flash` |
| Any `gemini-*` model | Pass-through unchanged |

> Pinned 2.5-class model strings (e.g. `gemini-2.5-pro`) still pass through
> unchanged if you have an explicit reason to target them.

## Monitoring

`VertexAIRemoteCellMonitor` queries two Google Cloud APIs:

- **Cloud Trace v2** (`cloudtrace.googleapis.com/v2/projects/{p}/traces`) for
  per-invocation trace records. Each span is checked for error status and latency.
- **Cloud Monitoring v3** (`monitoring.googleapis.com/v3/projects/{p}/timeSeries`)
  for aggregated execution, token, tool-call, and error-count metrics over the
  configured `LookBackMinutes` window.

Health is computed as:

```
IsHealthy = errorRate ≤ ErrorRateThreshold AND avgLatencyMs ≤ LatencyThresholdMs
```

When the Observability API is unavailable (e.g. network error, missing IAM role),
`GetHealthAsync` returns `IsHealthy = false` and `GetMetricsAsync` returns zero
values — both degrade gracefully without throwing.

## Platform adapter status

| Method | Status | Notes |
|---|---|---|
| `VertexAIDeployer.ValidateAsync` | ✅ Implemented | Live credential + model + tool checks |
| `VertexAIDeployer.DeployAsync` | ✅ Implemented | Calls Agent Runtime REST API via ADC |
| `VertexAIDeployer.TeardownAsync` | ✅ Implemented | Calls Agent Runtime REST API via ADC |
| `VertexAIRemoteCellMonitor.GetHealthAsync` | ✅ Implemented | Cloud Trace v2 · graceful degradation |
| `VertexAIRemoteCellMonitor.GetMetricsAsync` | ✅ Implemented | Cloud Monitoring v3 · graceful degradation |
| `VertexAIWorkflowHost.StartAsync` | ✅ Implemented | Delegates to `VertexAIDeployer.DeployAsync` |
| `VertexAIWorkflowHost.StopAsync` | ✅ Implemented | Delegates to `VertexAIDeployer.TeardownAsync` |

## New platform capabilities (Agent Platform GA)

The following Agent Platform features are newly available and represent planned
or candidate work for this adapter:

### Agent Memory Bank

[Agent Memory Bank](https://docs.cloud.google.com/gemini-enterprise-agent-platform/scale/memory-bank)
provides persistent, long-term memory with Memory Profiles. This maps naturally
to Ananke's `IConversationMemory` and `InMemoryEmpiricalMemory` abstractions —
a `GoogleMemoryBankAdapter` could back both with durable, cross-session storage.

### Agent Sessions

[Agent Sessions](https://docs.cloud.google.com/gemini-enterprise-agent-platform/scale/sessions)
with Custom Session IDs allow Ananke checkpoint/resume IDs to be mapped directly
to Agent Platform session records, enabling end-to-end traceability from
`ICheckpointStore` to the Google Cloud audit trail.

### Agent Identity & Agent Registry

[Agent Identity](https://docs.cloud.google.com/gemini-enterprise-agent-platform/scale/runtime/agent-identity)
assigns a cryptographic ID to every deployed agent. Combined with the
[Agent Registry](https://docs.cloud.google.com/gemini-enterprise-agent-platform/govern/agent-registry)
(central library of approved agents, tools, and skills), this provides the
governance layer that complements Ananke's `IDivisionApprovalGate` and
`InMemoryCapabilityMap`.

### Agent Gateway

[Agent Gateway](https://docs.cloud.google.com/gemini-enterprise-agent-platform/govern/gateways/agent-gateway-overview)
acts as the control point for all agent traffic, enforcing Model Armor
protections against prompt injection and data leakage. Ananke Federation
deployments should route through Agent Gateway to inherit these enterprise
security guardrails.

### Agent Simulation & Evaluation

[Agent Simulation](https://docs.cloud.google.com/gemini-enterprise-agent-platform/optimize/evaluation/evaluate-simulated)
and [Agent Evaluation](https://docs.cloud.google.com/gemini-enterprise-agent-platform/optimize/evaluation/agent-evaluation)
provide automated quality scoring and multi-turn autoraters. These are
complementary to Ananke's `OfflineLearner` / `InMemoryEmpiricalMemory` — Google
scores task success and safety; Ananke scores domain outcomes.

### Agent Sandbox

[Agent Sandbox](https://docs.cloud.google.com/gemini-enterprise-agent-platform/scale/sandbox/code-execution-overview)
provides a hardened environment for model-generated code execution and
browser-based automation. Relevant for Ananke workflows that include
skill-catalog-style tool invocations (see `Ananke.Skills` / `LearningPrimitivesDemo`)
running on deployed cells.

### Bidirectional Streaming

Agent Platform supports WebSocket-based bidirectional streaming for live audio
and video. This aligns with Ananke's `IStreamingAgentModel` and opens a path for
real-time multimodal `AgentJob` scenarios on deployed cells.

---

## Migration notes (Vertex AI → Agent Platform)

| Old reference | New reference |
|---|---|
| `cloud.google.com/vertex-ai/docs/agents` | `docs.cloud.google.com/gemini-enterprise-agent-platform` |
| `Vertex AI Agent Engine` | `Agent Runtime` (part of Agent Platform) |
| `gemini-2.5-pro` / `gemini-2.5-flash` | `gemini-3.1-pro` / `gemini-3.1-flash` (defaults) |
| `Platform = "vertex-ai"` in `DeployOptions` | `Platform = "gemini-agent-platform"` (old value still accepted) |
| `new VertexAIRemoteCellMonitor()` | `new VertexAIRemoteCellMonitor("my-project")` |

No code changes are required in consuming applications — the `VertexAI*` class
names are stable. Update model strings in `secrets.json` / configuration if you
have pinned `gemini-2.5-*` identifiers.
