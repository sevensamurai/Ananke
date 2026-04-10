# Ananke.A2A — Architecture

> Agent-to-Agent protocol — call remote A2A agents as `IAgentModel`
> and expose local workflows as A2A endpoints.

## Role

Implements Google's Agent-to-Agent (A2A) protocol for Ananke:
1. **Client:** `A2AAgentModel` wraps a remote A2A agent as an
   `IAgentModel`/`IStreamingAgentModel` — agents can call other agents
   transparently.
2. **Server:** Expose Ananke workflows as A2A-compatible endpoints
   via `WorkflowTaskAdapter` and `AgentCardBuilder`.

## Dependencies

- `Ananke.Orchestration` (project)
- `A2A` (NuGet — A2A protocol SDK, preview)

## Namespace → Folder Map

| Namespace | Contents |
|-----------|----------|
| `Ananke.A2A.Client` | `A2AAgentModel`, `A2AAgentModelOptions`, `A2AAgentDiscovery` |
| `Ananke.A2A.Server` | `WorkflowTaskAdapter`, `AgentCardBuilder` |
| `Ananke.A2A.Channels` | `A2AHandoffChannel` |

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `A2AAgentModel` | Class | `IStreamingAgentModel` that delegates to a remote A2A agent endpoint |
| `A2AAgentModelOptions` | Record | Configuration: agent URL, API key, timeout |
| `A2AAgentDiscovery` | Class | Discovers remote agents via A2A agent card protocol |
| `WorkflowTaskAdapter` | Class | Exposes a `Workflow<TState>` as an A2A task handler |
| `AgentCardBuilder` | Class | Builds A2A agent card metadata for advertising capabilities |
| `A2AHandoffChannel` | Class | `IHandoffChannel` that delegates handoffs over A2A protocol |
