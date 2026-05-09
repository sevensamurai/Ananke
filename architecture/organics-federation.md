# Architecture: Organics & Federation

> Part of the [Architecture Guide](../ARCHITECTURE.md). Covers organic colony self-organization and cross-cloud federation.

---

## Organic Colony Architecture

`Ananke.Organics` implements a **biological cell division metaphor** for self-organizing workflow ecosystems. Depends on `Ananke.Learning` + `Ananke.Design`.

```mermaid
flowchart TD
    subgraph Colony["OrganicHost (Colony)"]
        SENSE[Sensing Layer<br/>ICapabilityMap · IRequestRouter]
        WF1[OrganicWorkflow A<br/>domain: billing]
        WF2[OrganicWorkflow B<br/>domain: support]
        WF3[OrganicWorkflow C<br/>domain: onboarding]
    end

    REQ[Incoming Request] --> SENSE
    SENSE --> WF1

    subgraph Division["Cell Division"]
        MON[WorkflowExecutionMonitor<br/>IComplexityMonitor]
        POL[IDivisionPolicy<br/>threshold or experience-driven]
        GATE[IDivisionApprovalGate<br/>auto · LLM · callback]
        DIV[IWorkflowDivider<br/>ToolKit cluster strategy]
    end

    WF1 --> MON
    MON --> POL
    POL -->|threshold exceeded| GATE
    GATE -->|approved| DIV
    DIV --> WF4[New child workflow]
    WF4 --> Colony
```

### Key Types

| Type | Purpose |
|---|---|
| `OrganicHost` | Colony manager — hosts workflows, routes requests, triggers division |
| `OrganicWorkflow` | Workflow wrapper with complexity monitoring and domain affinity |
| `IWorkflowHost` / `InProcessWorkflowHost` | Host abstraction for workflow lifecycle |
| `IWorkflowReplicator` | Create new workflow instances during division |

### Sensing

| Type | Purpose |
|---|---|
| `ICapabilityMap` / `InMemoryCapabilityMap` | Registry of what each workflow can handle |
| `IRequestRouter` / `KeywordRequestRouter` | Route requests to best-matching workflow |
| `SensedCapability` | Capability descriptor with confidence score |
| `WorkflowSignal` | Heartbeat/load signal from a running workflow |

### Division

| Type | Purpose |
|---|---|
| `IDivisionPolicy` | Decide when to divide |
| `ThresholdDivisionPolicy` | Divide when complexity exceeds threshold |
| `ExperienceDrivenDivisionPolicy` | Use empirical memory to decide |
| `IComplexityMonitor` / `WorkflowExecutionMonitor` | Track workflow load/complexity |
| `IWorkflowDivider` / `ToolKitClusterStrategy` | Determine how to split tools across children |
| `IDivisionApprovalGate` | Human/LLM/auto approval before division |
| `IDivisionOutcomeTracker` | Track division success for learning |
| `DomainAffinityMemory` | Remember which domains map to which workflows |
| `StructuralProfile` / `StructuralProfileFactory` | Analyze workflow structure for division |

### Snapshots

| Type | Purpose |
|---|---|
| `HostSnapshot` / `HostSnapshotExporter` | Serialize colony state |
| `WorkflowSnapshotBuilder` | Capture workflow topology + tools |
| `PromptWorkflowDesigner` | LLM-powered workflow design for new children |
| `WorkflowActivator` | Instantiate workflow from snapshot |

---

## Federation

`Ananke.Federation` extends Organics to **cross-cloud deployment**. Depends on `Ananke.Organics` + `Ananke.Design`.

```mermaid
flowchart TD
    subgraph Local["Local Colony (OrganicHost)"]
        HOST[FederatedWorkflowHost]
        HYBRID[HybridRouter<br/>local vs remote]
    end

    subgraph Validation["Pre-Deploy Validation"]
        VAL[IDeployabilityValidator]
        PLAT[IPlatformValidator]
        MAP[IModelMapper]
    end

    subgraph Deployment["Cloud Deployment"]
        DEP[IFederationDeployer]
        REG[IDeploymentRegistry]
        CRED[IFederationCredentialProvider]
    end

    subgraph Monitoring["Remote Monitoring"]
        MON[IRemoteCellMonitor]
        METRICS[RemoteMetricsTracker]
        HEALTH[RemoteCellHealth]
    end

    HOST --> HYBRID
    HYBRID -->|local| LOCAL_WF[Local workflow]
    HYBRID -->|remote| REMOTE[Remote cloud agent]

    HOST --> VAL
    VAL --> PLAT
    VAL --> MAP
    VAL -->|valid| DEP
    DEP --> REG
    DEP --> CRED

    REMOTE --> MON
    MON --> METRICS
    METRICS --> HEALTH
```

### Platform-Specific Packages

| Package | Target Platform |
|---|---|
| `Ananke.Federation.Google` | Google Vertex AI |
| `Ananke.Federation.Anthropic` | Anthropic Claude Managed Agents |
| `Ananke.Federation.Azure` | Azure AI |

### Key Types

| Type | Purpose |
|---|---|
| `FederatedWorkflowHost` | Hosts workflows with federation awareness |
| `HybridRouter` | Routes requests to local or remote workflows |
| `FederatedComplexityMonitor` | Includes remote cell metrics in complexity assessment |
| `IDeployabilityValidator` | Pre-flight checks before deployment |
| `DeployabilityReport` / `DeployDiagnostic` | Validation results |
| `IFederationDeployer` | Deploy workflow to cloud platform |
| `IDeploymentRegistry` / `InMemoryDeploymentRegistry` | Track deployed workflows |
| `DeploymentRecord` / `DeploymentProfile` | Deployment metadata |
| `IRemoteCellMonitor` / `RemoteMetricsTracker` | Health + performance monitoring |
| `ISystemPromptCompiler` / `ManifestSystemPromptCompiler` | Generate system prompts for remote agents |
| `FederatedDivisionPolicy` | Division policy aware of remote capacity |
| `PlatformDivisionApprovalGate` | Platform-specific approval for division |
