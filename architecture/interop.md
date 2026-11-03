# Architecture: Interop — MCP, A2A, Skills

> Part of the [Architecture Guide](../ARCHITECTURE.md). Covers protocol bridges and the external skill catalog.

---

## MCP (Model Context Protocol)

`Ananke.MCP` exposes Ananke capabilities as an MCP server. Depends on `Ananke.Orchestration`.

```mermaid
flowchart LR
    subgraph Ananke
        TK[ToolKit] --> ADAPT[AnankeToolAdapter]
        WF[Workflow] --> WFADAPT[WorkflowToolAdapter]
    end

    subgraph MCP Server
        ADAPT --> MCP_SRV[MCP Server<br/>stdio or HTTP]
        WFADAPT --> MCP_SRV
    end

    MCP_SRV --> CLIENT[MCP Client<br/>VS Code Copilot · Claude Desktop]
```

| Type | Purpose |
|---|---|
| `AnankeToolAdapter` | Wraps `ToolKit` tools as MCP tool capabilities |
| `WorkflowToolAdapter` | Wraps a `Workflow<T>` as an MCP tool |
| `McpServerBuilderExtensions` | DI integration for MCP server setup |
| `McpToolInvoker` | Invokes tools on external MCP servers and returns typed results — used when importing tools from a remote MCP server into a `ToolKit` |
| `ToolKitMcpExtensions` | Import tools from external MCP servers into `ToolKit` |

---

## A2A (Agent-to-Agent Protocol)

`Ananke.A2A` enables cross-framework agent interoperability. Depends on `Ananke.Orchestration`.

```mermaid
flowchart LR
    subgraph Client Side
        AGENT[Ananke Agent] --> A2A_CLIENT[A2AAgentModel<br/>implements IAgentModel]
        A2A_CLIENT --> REMOTE[Remote A2A Agent<br/>any framework]
    end

    subgraph Server Side
        INCOMING[A2A Client Request] --> A2A_SRV[WorkflowTaskAdapter]
        A2A_SRV --> WF[Ananke Workflow]
    end
```

| Type | Purpose |
|---|---|
| `A2AAgentModel` | Call remote A2A agents as drop-in `IAgentModel` |
| `A2AAgentModelOptions` | Endpoint URL, auth config |
| `A2AAgentDiscovery` | Discover remote agents via A2A protocol |
| `WorkflowTaskAdapter` | Expose Ananke workflows as A2A endpoints |
| `AgentCardBuilder` | Build A2A agent card metadata |
| `A2AHandoffChannel` | Cross-agent handoff via A2A |

---

## External Skill Catalog

`Ananke.Skills` connects to external tool registries. Depends on `Ananke.Orchestration`.

```mermaid
flowchart LR
    QUERY["toolkit.AddFromCatalogAsync<br/>('airbnb search lodging')"] --> CAT[ISkillCatalog<br/>OpenClawCatalog]
    CAT --> REGISTRY[ClawHub Registry<br/>clawhub.io]
    REGISTRY --> DESC[SkillDescriptor]
    DESC --> RESOLVE[Resolve to ToolDefinition]
    RESOLVE --> EXEC[CliProcessRunner<br/>uvx · npx · docker · shell]
```

| Type | Purpose |
|---|---|
| `ISkillCatalog` | Search for external tools by natural language |
| `OpenClawCatalog` | ClawHub registry implementation with local JSON cache |
| `SkillDescriptor` | Tool metadata (name, description, install method, params) |
| `SkillInstallMethod` | How to run: `uvx`, `npx`, `docker`, `shell` |
| `CliProcessRunner` | Execute CLI tools as child processes |
| `ISkillScoreStore` / `JsonFileScoreStore` | Local voting and reliability scoring |
| `SkillScore` | Success rate, vote count, last used |
| `ToolKitSkillExtensions` | `toolkit.AddFromCatalogAsync()` convenience methods |
| `SkillCatalogMemorySync` | Synchronises discovered skill catalog entries into `IToolMemory` so the smart router can recall and score them across sessions |
