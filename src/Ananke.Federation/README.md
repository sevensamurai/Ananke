# Ananke.Federation

Federation hub for Ananke. Provides the core abstractions, default implementations,
and shared infrastructure that platform adapters (`Ananke.Federation.Azure`,
`Ananke.Federation.Google`, `Ananke.Federation.Anthropic`) are built on.

Also ships the **local design loop** infrastructure — run and test workflows that
declare `ToolExecutionMode.PlatformNative` capabilities on your developer machine
or in CI without credentials. See [Local design loop](#local-design-loop) below,
and [`Ananke.Federation.LocalEmulators`](../Ananke.Federation.LocalEmulators/README.md)
for the full emulator catalogue.

## What this package provides

| Area | Key types |
|---|---|
| **Deployment** | `IFederationDeployer`, `IDeploymentRegistry`, `InMemoryDeploymentRegistry`, `LocalFederationDeployer`, `DeploymentRecord`, `DeploymentProfile`, `DeployOptions` |
| **Validation** | `IDeployabilityValidator`, `DeployabilityValidator`, `IPlatformValidator`, `LocalPlatformValidator`, `IModelMapper`, `DeployabilityReport`, `DeployDiagnostic` |
| **Execution** | `IPlatformNativeExecutor`, `PlatformNativeExecutorRegistry` |
| **Monitoring** | `IRemoteCellMonitor`, `RemoteCellMetrics`, `RemoteCellHealth`, `MetricsSample`, `RemoteCellTrend`, `RemoteMetricsTracker` |
| **Hosting** | `FederatedComplexityMonitor`, `FederatedWorkflowHost`, `HybridRouter` |
| **Division** | `FederatedDivisionPolicy`, `PlatformDivisionApprovalGate` |
| **Recommendation** | `IPlatformRecommender`, `PlatformRecommender` — scores platforms by capability fit, cost/latency band, telemetry-calibrated via `RemoteMetricsTracker` |
| **Prompts** | `ISystemPromptCompiler`, `ManifestSystemPromptCompiler` |
| **Credentials** | `IFederationCredentialProvider` |

## Architecture

Federation follows the **supervisor-only hybrid** model:

- Remote cells are self-contained — no cross-boundary tool callbacks.
- The local `OrganicHost` supervises all cells (local and remote) through `FederatedComplexityMonitor`.
- `FederatedDivisionPolicy` wraps an inner `IDivisionPolicy` and sets `TargetPlatform` on child specs.
- `PlatformDivisionApprovalGate` always requires human oversight before any cross-boundary deployment.
- `RemoteMetricsTracker` emits OTEL gauges via the `Ananke.Federation` meter name.

## Quick start

```csharp
// 1. Validate a manifest against a target platform
var validator = new DeployabilityValidator();
DeployabilityReport report = await validator.ValidateAsync(manifest, "azure-ai");
if (report.HasErrors) { /* handle */ }

// 2. Deploy (via a platform adapter)
var deployer = new AzureAgentDeployer(credentials, registry);
DeploymentRecord record = await deployer.DeployAsync(manifest, toolKit, options);

// 3. Wrap your local monitor for federated complexity tracking
var monitor = new FederatedComplexityMonitor(
    localMonitor: myLocalMonitor,
    registry: registry,
    remoteMonitors: [new AzureRemoteCellMonitor()],
    metricsTracker: metricsTracker);

// 4. Use the nnke-platform CLI for day-2 operations
// nnke-platform validate manifest.ananke.yml
// nnke-platform deploy manifest.ananke.yml
// nnke-platform trends --deployment-id <id>
// nnke-platform analyze manifest.ananke.yml --deployment-id <id>
```

## Local design loop

Workflows that target a managed platform can be **authored, executed, and tested
locally** before any deployment. This enables offline authoring, CI without
credentials, and fast iteration on platform-native capability usage.

### How it works

1. Declare capabilities as usual with `ToolExecutionMode.PlatformNative`.
2. Register emulators from `Ananke.Federation.LocalEmulators`:

```csharp
using Ananke.Federation.Execution;
using Ananke.Federation.LocalEmulators;

var registry = new PlatformNativeExecutorRegistry();
DefaultPlatformNativeExecutors.Register(registry);

// Patch a ToolKit so platform-native tools execute locally
registry.ApplyTo(toolKit, platform: "azure-ai");
```

3. Deploy locally using `LocalFederationDeployer` — no network, no credentials:

```csharp
var deployer = new LocalFederationDeployer(new InMemoryDeploymentRegistry());
var record = await deployer.DeployAsync(manifest, toolKit, new DeployOptions { Platform = "local" });
// record.Platform == "local", record.Status == DeploymentStatus.Active
```

4. Validate capability coverage with `LocalPlatformValidator`:

```csharp
var validator = new LocalPlatformValidator(registry, new DeployabilityValidator());
var report = await validator.ValidateAsync(manifest, toolKit, "local-emulated:azure-ai");
// FED061 — capability declared but no executor registered
// FED062 — capability is covered by a stub (deterministic, not real)
```

### Routing tier

`HybridRouter` supports three routing targets:

| Target | Meaning |
|---|---|
| `"local"` | Run entirely in-process on the local machine |
| `"azure-ai"` / `"vertex-ai"` / `"claude"` | Deploy to the named managed platform |
| `"local-emulated:azure-ai"` | Run locally through registered emulators, simulating the named platform |

```csharp
// Pin a cell to local emulation of Foundry
var rule = RoutingRule.EmulateAll("foundry");  // target: "local-emulated:azure-ai"
```

### Platform identifier aliases

The post-May-2026 platform names are accepted everywhere:

| Alias | Resolves to | Diagnostic |
|---|---|---|
| `foundry` | `azure-ai` | `FED060` (warning) |
| `gemini-enterprise` | `vertex-ai` | `FED060` (warning) |

Existing manifests authored with `azure-ai` / `vertex-ai` continue to work unchanged.

### Diagnostic codes (local loop)

| Code | Severity | Meaning |
|---|---|---|
| `FED060` | Warning | Platform identifier alias resolved (e.g. `foundry → azure-ai`) |
| `FED061` | Error | `PlatformNative` capability declared but no executor registered for local target |
| `FED062` | Warning | Capability is covered by a stub — results are deterministic, not real |

## Deployment profiles

A `DeploymentProfile` describes a named target environment and rebinds tools for
platform-native execution. Profiles are declared in `.ananke.yml` under `profiles:`
and parsed by the `Ananke.Design` manifest parser.

```yaml
profiles:
  - id: prod-azure
    platform: azure-ai
    tools:
      - name: web_search
        binding: bing_grounding
```

## OTEL integration

`RemoteMetricsTracker` emits observable gauge measurements. Add the meter to your
OpenTelemetry pipeline to export trends to Prometheus, Grafana, or any OTLP backend:

```csharp
builder.AddMeter(RemoteMetricsTracker.MeterName); // "Ananke.Federation"
```

## Credentials

Platform credentials are never stored in manifests. See
[`architecture/federation-credentials.md`](../../architecture/federation-credentials.md)
for the full credentials matrix: credential types, required scopes, rotation procedures,
and per-platform `IFederationCredentialProvider` implementation status.

## Release checklist

Before each release, update the platform capabilities list in
[`Validation/platform-capabilities.json`](Validation/platform-capabilities.json):

1. **Azure AI Agent Service** — check the `Azure.AI.Projects.Agents` SDK for new
   tool types (`*Tool` classes). The SDK changelog and
   [Azure AI Agent Service docs](https://learn.microsoft.com/en-us/azure/ai-services/agents/)
   list newly GA'd capabilities.
2. **Vertex AI** — check the `Google.Cloud.AIPlatform.V1` SDK and
   [Vertex AI Agent Engine docs](https://cloud.google.com/vertex-ai/docs/agents)
   for new tool types.
3. **Claude** — check the
   [Anthropic tool use docs](https://docs.anthropic.com/en/docs/agents-and-tools/tool-use)
   for new built-in tools.

The JSON file is embedded as a resource and loaded by `DeployabilityValidator` at
startup. Unknown capabilities produce a **warning** (FED003), not an error — the
platform API validates at deploy time. This list only improves pre-deploy DX.
