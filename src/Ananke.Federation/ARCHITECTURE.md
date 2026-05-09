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
| `Ananke.Federation.Deployment` | `IFederationDeployer`, `IDeploymentRegistry`, `InMemoryDeploymentRegistry`, `DeploymentRecord`, `DeploymentProfile`, `DeploymentStatus`, `DeployOptions` |
| `Ananke.Federation.Division` | `FederatedDivisionPolicy`, `PlatformDivisionApprovalGate` |
| `Ananke.Federation.Hosting` | `FederatedWorkflowHost`, `HybridRouter`, `FederatedComplexityMonitor` |
| `Ananke.Federation.Monitoring` | `IRemoteCellMonitor`, `RemoteCellHealth`, `RemoteCellMetrics`, `RemoteCellTrend`, `RemoteMetricsTracker`, `MetricsSample` |
| `Ananke.Federation.Prompts` | `ISystemPromptCompiler`, `ManifestSystemPromptCompiler` |
| `Ananke.Federation.Validation` | `IDeployabilityValidator`, `DeployabilityValidator`, `IPlatformValidator`, `DeployabilityReport`, `DeployDiagnostic`, `DeployDiagnosticSeverity`, `IModelMapper` |

---

## Key Abstractions

| Type | Kind | Purpose |
|---|---|---|
| `IFederationDeployer` | `interface` | Platform deployer — `ValidateAsync`, `DeployAsync`, `TeardownAsync`. One implementation per platform package. |
| `IFederationCredentialProvider` | `interface` | Resolves platform credentials at runtime. `GetCredentialAsync` returns an opaque credential object. See [ValidateAsync status](#credential-validation-status). |
| `IDeploymentRegistry` | `interface` | Tracks active deployments (`Register`, `Get`, `List`, `UpdateStatus`). Default: `InMemoryDeploymentRegistry`. |
| `IDeployabilityValidator` | `interface` | Offline structural validation (no credentials, no network). Returns `DeployabilityReport` with FED001–FED023 diagnostic codes. |
| `IPlatformValidator` | `interface` | Live platform validation (credentials + quota checks). Implemented by each provider package. |
| `IRemoteCellMonitor` | `interface` | Polls remote cell health and execution metrics. One implementation per platform package. |
| `ISystemPromptCompiler` | `interface` | Compiles a `WorkflowManifest` into a platform system prompt. Default: `ManifestSystemPromptCompiler`. |
| `FederatedWorkflowHost` | `sealed class` | Composite `IWorkflowHost` — routes `StartAsync`/`StopAsync` to local or platform-specific hosts via `HybridRouter`. Falls back to local if no rule matches. |
| `HybridRouter` | `sealed class` | Rule-based routing. Decisions are sticky for cell lifetime; migration = teardown + re-deploy. |
| `FederatedDivisionPolicy` | `sealed class` | `IDivisionPolicy` decorator — enriches inner policy's `DivisionPlan` with `ChildSpec.TargetPlatform` based on deployment profiles and metrics trends. |
| `PlatformDivisionApprovalGate` | `sealed class` | Requires human approval for platform-targeted divisions; delegates local-only divisions to an inner gate. |
| `FederatedComplexityMonitor` | `sealed class` | `IHealthMonitor` + `IRemoteCellSource` — bridges local telemetry and remote platform metrics into unified `ComplexitySnapshot`. |
| `RemoteMetricsTracker` | `sealed class` | Accumulates `MetricsSample` streams per deployment and computes `RemoteCellTrend` (Stable / Improving / Degrading). |

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

`IFederationCredentialProvider.ValidateAsync()` has a **default interface implementation that throws `NotImplementedException`**. This is intentional — providers that have not yet implemented live credential validation will fail loudly rather than silently. Each platform adapter is expected to override this method. Current status:

| Provider | `ValidateAsync` status |
|---|---|
| `ClaudeCredentialProvider` (Anthropic) | Not yet implemented — throws |
| `AzureAgentCredentialProvider` (Azure) | See provider package |
| `VertexAICredentialProvider` (Google) | See provider package |

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
