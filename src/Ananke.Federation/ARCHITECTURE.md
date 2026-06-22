# Ananke.Federation — Architecture

> Core federation layer — deployer, registry, credential provider, validator, monitor,
> and hybrid routing abstractions for multi-platform agent deployment.

## Role

`Ananke.Federation` provides the **platform-agnostic contracts and shared infrastructure**
for deploying Ananke workflow cells to remote AI agent platforms (Azure AI Agent Service,
Vertex AI Agent Runtime, Claude Managed Agents). It also extends the organic mesh with
federation-aware division and monitoring.

Platform-specific implementations live in separate packages:
`Ananke.Federation.Anthropic`, `Ananke.Federation.Azure`, `Ananke.Federation.Google`.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `IFederationDeployer` — the contract every platform package implements; start here to
   understand the deploy/validate/teardown lifecycle — `src/Ananke.Federation/Deployment/IFederationDeployer.cs`
2. `IDeployabilityValidator` — the offline check a manifest must pass before any deploy is
   attempted — `src/Ananke.Federation/Validation/IDeployabilityValidator.cs`
3. `IFederationCredentialProvider` — how every platform resolves secrets at runtime — `src/Ananke.Federation/Credentials/IFederationCredentialProvider.cs`
4. `FederatedWorkflowHost` — the runtime composition root that routes a cell to a local or
   platform-specific host — `src/Ananke.Federation/Hosting/FederatedWorkflowHost.cs`

---

## Dependencies

| Dependency | Why |
|---|---|
| `Ananke.Organics` | `FederatedWorkflowHost` implements `IWorkflowHost`; `FederatedDivisionPolicy` implements `IDivisionPolicy`; `PlatformDivisionApprovalGate` implements `IDivisionApprovalGate`; `FederatedComplexityMonitor` implements `IHealthMonitor` |
| `Ananke.Design` | `WorkflowManifest` is the unit of deployment — deployers translate manifests to platform-specific agent definitions |

---

## Vertical Slice Map

```
Ananke.Federation/
  Credentials/      Runtime credential resolution
  Deployment/       Deployer interface, registry, deployment records and options
  Division/         Federation-aware division policy and approval gate
  Hosting/          FederatedWorkflowHost, HybridRouter, FederatedComplexityMonitor
  Monitoring/       Remote cell health, metrics, and trend tracking
  Prompts/          System prompt compilation from manifests
  Validation/       Structural deployability validation (offline, no credentials needed)
```

---

## Namespace → Folder Map

| Namespace | Key Types |
|---|---|
| `Ananke.Federation.Credentials` | `IFederationCredentialProvider` |
| `Ananke.Federation.Adapters` | `AdapterManifest`, `AdapterDiagnostics` — sidecar JSON manifest written by adapter installers; read by `PlatformHost` to validate compatibility before loading the assembly |
| `Ananke.Federation.Agents` | `IManagedAgentClient` — CRUD abstraction over platform managed-agent resources; implemented by each provider package |
| `Ananke.Federation.Execution` | `IPlatformNativeExecutor`, `PlatformNativeExecutorRegistry` — executes workflow steps using the platform's native execution API |
| `Ananke.Federation.Paths` | `AnankePaths` — well-known file/directory path constants for federation artefacts |
| `Ananke.Federation.Recommendation` | `IPlatformRecommender`, `PlatformRecommender`, `PlatformFitScore`, `PlatformFitReport`, `FitReason`, `FitReasonKind`, `PlatformProfiles`, `RecommendationWeights` — scores and ranks deployment platforms for a given manifest |
| `Ananke.Federation.Deployment` | `IFederationDeployer`, `IDeploymentRegistry`, `InMemoryDeploymentRegistry`, `JsonFileDeploymentRegistry`, `DeploymentRecord`, `DeploymentProfile`, `DeploymentStatus`, `DeployOptions`, `LocalFederationDeployer`, `FederationDeployerRegistry` |
| `Ananke.Federation.Division` | `FederatedDivisionPolicy`, `PlatformDivisionApprovalGate` |
| `Ananke.Federation.Hosting` | `FederatedWorkflowHost`, `HybridRouter`, `FederatedComplexityMonitor`, `PlatformWorkflowHostBase` |
| `Ananke.Federation.Monitoring` | `IRemoteCellMonitor`, `RemoteCellHealth`, `RemoteCellMetrics`, `RemoteCellTrend`, `RemoteMetricsTracker`, `MetricsSample` |
| `Ananke.Federation.Prompts` | `ISystemPromptCompiler`, `ManifestSystemPromptCompiler` |
| `Ananke.Federation.Validation` | `IDeployabilityValidator`, `DeployabilityValidator`, `IPlatformValidator`, `DeployabilityReport`, `DeployDiagnostic`, `DeployDiagnosticSeverity`, `IModelMapper` |

---

## Key Abstractions

| Type | Kind | Purpose | Source |
|---|---|---|---|
| `IFederationDeployer` | `interface` | Platform deployer — `ValidateAsync`, `DeployAsync`, `TeardownAsync`. One implementation per platform package. | `src/Ananke.Federation/Deployment/IFederationDeployer.cs` |
| `IFederationCredentialProvider` | `interface` | Resolves platform credentials at runtime. `GetCredentialAsync` returns an opaque credential object. See [ValidateAsync status](#credential-validation-status). | `src/Ananke.Federation/Credentials/IFederationCredentialProvider.cs` |
| `IDeploymentRegistry` | `interface` | Tracks active deployments (`RegisterAsync`, `GetAsync`, `ListAsync`, `UpdateStatusAsync`, `UpdateAsync`). Default: `InMemoryDeploymentRegistry`. | `src/Ananke.Federation/Deployment/IDeploymentRegistry.cs` |
| `IDeployabilityValidator` | `interface` | Offline structural validation (no credentials, no network). Returns `DeployabilityReport` with FED001–FED023 diagnostic codes. | `src/Ananke.Federation/Validation/IDeployabilityValidator.cs` |
| `IPlatformValidator` | `interface` | Live platform validation (credentials + quota checks). Implemented by each provider package. | `src/Ananke.Federation/Validation/IPlatformValidator.cs` |
| `IRemoteCellMonitor` | `interface` | Polls remote cell health and execution metrics. One implementation per platform package. | `src/Ananke.Federation/Monitoring/IRemoteCellMonitor.cs` |
| `ISystemPromptCompiler` | `interface` | Compiles a `WorkflowManifest` into a platform system prompt. Default: `ManifestSystemPromptCompiler`. Federation-local interface — distinct from `Ananke.Abstractions.Providers.ISystemPromptCompiler`. | `src/Ananke.Federation/Prompts/ISystemPromptCompiler.cs` |
| `FederatedWorkflowHost` | `sealed class` | Composite `IWorkflowHost` — routes `StartAsync`/`StopAsync` to local or platform-specific hosts via `HybridRouter`. Falls back to local if no rule matches. | `src/Ananke.Federation/Hosting/FederatedWorkflowHost.cs` |
| `HybridRouter` | `sealed class` | Rule-based routing. Decisions are sticky for cell lifetime; migration = teardown + re-deploy. | `src/Ananke.Federation/Hosting/HybridRouter.cs` |
| `FederatedDivisionPolicy` | `sealed class` | `IDivisionPolicy` decorator — enriches inner policy's `DivisionPlan` with `ChildSpec.TargetPlatform` based on deployment profiles and metrics trends. | `src/Ananke.Federation/Division/FederatedDivisionPolicy.cs` |
| `PlatformDivisionApprovalGate` | `sealed class` | Requires human approval for platform-targeted divisions; delegates local-only divisions to an inner gate. | `src/Ananke.Federation/Division/PlatformDivisionApprovalGate.cs` |
| `FederatedComplexityMonitor` | `sealed class` | `IHealthMonitor` + `IRemoteCellSource` — bridges local telemetry and remote platform metrics into unified `ComplexitySnapshot`. | `src/Ananke.Federation/Hosting/FederatedComplexityMonitor.cs` |
| `RemoteMetricsTracker` | `sealed class` | Accumulates `MetricsSample` streams per deployment and computes `RemoteCellTrend` (Stable / Improving / Degrading). | `src/Ananke.Federation/Monitoring/RemoteMetricsTracker.cs` |

---

## Deployer Lifecycle

```
IFederationDeployer.ValidateAsync(manifest, toolKit)
  → IDeployabilityValidator.Validate(manifest, toolKit, platform)   (offline, structural)
  → IPlatformValidator.ValidateAsync(manifest, toolKit)              (live, credentials + quota)
  → DeployabilityReport { IsDeployable, Diagnostics[] }

IFederationDeployer.DeployAsync(manifest, toolKit, options)
  → translate manifest → platform agent definition
  → translate toolKit  → platform tool schema
  → compile system prompt via ISystemPromptCompiler
  → call platform deploy API
  → IDeploymentRegistry.RegisterAsync(DeploymentRecord)
  → return DeploymentRecord { DeploymentId, Platform, Status=Active }

IFederationDeployer.TeardownAsync(deploymentId)
  → IDeploymentRegistry.GetAsync(deploymentId)
  → call platform teardown API
  → IDeploymentRegistry.UpdateStatusAsync(deploymentId, Stopped)
```

---

## Validation Lifecycle

Two-stage validation is always performed before deployment:

1. **Offline / structural** (`IDeployabilityValidator`) — checks manifest completeness, tool count limits, job type compatibility, model alias presence, execution mode compatibility. No credentials needed.
2. **Live / platform** (`IPlatformValidator`) — checks credentials, model availability on the target platform, quota, and tool schema compliance. Requires network access.

`IFederationDeployer.ValidateAsync` composes both stages and returns a unified `DeployabilityReport`.

---

## Credential Provider Behaviour

`IFederationCredentialProvider` resolves secrets at runtime (never stored in manifests).

`GetCredentialAsync(platform)` — returns an opaque `object?`. Provider implementations return platform-specific credential types (API keys, `TokenCredential` for Azure, service-account JSON for Google).

### Credential validation status

`IFederationCredentialProvider.ValidateAsync()` is a plain interface member — there is no
default implementation, so every provider supplies its own. All three are implemented today:

| Provider | `ValidateAsync` behaviour |
|---|---|
| `ClaudeCredentialProvider` (Anthropic) | Without a `clientFactory`, returns `true` when `ANTHROPIC_API_KEY` (or the constructor-supplied key) is present and non-empty. With a `clientFactory` supplied, performs a live API round-trip (`PingAsync`). |
| `AzureAgentCredentialProvider` (Azure) | Calls `GetCredentialAsync(Platform, ct)` and returns whether the result is non-null. |
| `VertexAICredentialProvider` (Google) | Calls `GetCredentialAsync(Platform, ct)` and returns whether the result is non-null. |

---

## Hybrid Routing

`HybridRouter` evaluates an ordered list of `RoutingRule` entries at cell start time:

- First matching rule wins and returns the platform identifier (e.g. `"azure-ai"`)
- If no rule matches, the cell runs locally (`null` → `_localHost`)
- Routing is **sticky** — a cell's platform does not change after start
- Migration is a deliberate teardown + re-deploy operation

`FederatedWorkflowHost` maps the returned platform identifier to the corresponding `IWorkflowHost` in its `_platformHosts` dictionary.

---

## Extension Points

| Interface | Default | Purpose |
|---|---|---|
| `IFederationDeployer` | _(none — per platform)_ | Platform deployment |
| `IFederationCredentialProvider` | _(none — per platform)_ | Credential resolution |
| `IDeploymentRegistry` | `InMemoryDeploymentRegistry` | Deployment record persistence |
| `IDeployabilityValidator` | `DeployabilityValidator` | Offline structural validation |
| `IPlatformValidator` | _(none — per platform)_ | Live platform validation |
| `IRemoteCellMonitor` | _(none — per platform)_ | Remote health / metrics |
| `ISystemPromptCompiler` | `ManifestSystemPromptCompiler` | System prompt generation |
| `IModelMapper` | _(per platform)_ | Ananke model alias → platform model ID translation |

---

## Production Readiness

| Capability | Status | Notes |
|---|---|---|
| Offline structural validation | Supported | `DeployabilityValidator`, FED001–FED023 codes |
| Deployment registry | Supported | `InMemoryDeploymentRegistry` — swap for persistent store in production |
| Hybrid routing | Supported | Rule-based, sticky per cell |
| Federated division policy | Supported | Platform targeting enrichment |
| Platform approval gate | Supported | Human-in-the-loop for platform-targeted divisions |
| Credential resolution | Supported | Per-provider implementations |
| Credential validation | **Preview** | Default DIM throws — providers must override |
| Remote metrics tracking | Supported | `RemoteMetricsTracker` with trend analysis |
| Live platform deploy / teardown | **Provider-dependent** | See individual provider packages |
