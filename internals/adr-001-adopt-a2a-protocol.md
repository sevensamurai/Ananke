# ADR-001: Adopt the A2A (Agent-to-Agent) Protocol

| Field | Value |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-03-05 |
| **Deciders** | Ananke maintainers |
| **Relates to** | Ananke.MCP, Ananke.Orchestration |

## Context

Ananke is a .NET 10 library for building agentic applications. It already supports **agent-to-tool** interoperability via MCP (`Ananke.MCP`), but has no standardized mechanism for **agent-to-agent** communication with external, opaque agents built on different frameworks (LangGraph, CrewAI, Semantic Kernel, etc.).

The [Agent2Agent (A2A) Protocol](https://a2a-protocol.org/latest/) is an open standard (Apache 2.0, Linux Foundation) designed for exactly this: enabling independent AI agents to discover each other, negotiate interaction modalities, manage collaborative tasks, and securely exchange information — without sharing internal state, memory, or tools.

A mature [C#/.NET SDK](https://github.com/a2aproject/a2a-dotnet) exists as NuGet packages `A2A` and `A2A.AspNetCore`.

### Current Gaps

- `HandoffJob` / `IHandoffChannel` enable delegation to external services, but use proprietary message formats with no standard discovery or capability negotiation.
- `IAgentModel` abstracts LLM providers, but cannot represent a remote A2A-compliant agent as a callable model.
- There is no standard way for external callers to discover and invoke Ananke-hosted agents.

## Decision

Adopt the A2A protocol and create an `Ananke.A2A` integration package, following the same pattern established by `Ananke.MCP`.

## Rationale

### Architectural Alignment

| Ananke Concept | A2A Equivalent | Fit |
|---|---|---|
| `IAgentModel` / `AgentRequest` / `AgentResponse` | `Message` → `Task` → `Artifact` | Strong — request/response maps to message-send/task-result |
| `IStreamingAgentModel` / `StreamingChatWorkflow` | `SendStreamingMessage` (SSE) | Strong — both use async streaming paradigms |
| `HandoffJob` / `IHandoffChannel` | `SendMessage` + Task lifecycle | Very strong — handoff already models "publish, await correlated response" |
| `ModelRouter` / `CapabilityModelRouter` | AgentCard discovery + capability matching | Strong — AgentCard skills/capabilities map to `ModelProfile` / `TaskRequirements` |
| `ToolKit` / `ToolDefinition` | `AgentSkill` (in AgentCard) | Moderate — tools can be advertised as skills, but granularity differs |
| `Ananke.MCP` | Complementary | A2A spec explicitly defines A2A and MCP as complementary (agent↔agent vs agent↔tool) |

### SDK Assessment

| Factor | Assessment |
|---|---|
| Target framework | .NET Standard 2.0 + .NET 8+ (compatible with .NET 10) |
| License | Apache 2.0 (same as Ananke) |
| Maturity | Protocol v0.2.6 implemented, v1.0 RC in progress |
| Key classes | `A2AClient`, `TaskManager`, `A2ACardResolver`, `ITaskStore` |
| ASP.NET Core | `A2A.AspNetCore` provides `MapA2A()` / `MapHttpA2A()` |
| Contributors | Microsoft (.NET team: stephentoub, adamsitnik), Google-origin |
| Activity | 200+ stars, 23 contributors, 179 commits, actively maintained |

### Value Proposition

- Enables Ananke agents to interoperate with any A2A-compliant agent across frameworks and vendors.
- Completes the interoperability picture: MCP (agent↔tool) + A2A (agent↔agent).
- Formalizes the handoff pattern already present in `HandoffJob` with standard discovery, lifecycle, and error handling.
- Low integration effort (~500–800 LOC) following the established `Ananke.MCP` template.

### Key Mapping Challenges

| Challenge | Mitigation |
|---|---|
| A2A `Part[]` (text/file/data) vs Ananke's `string Content` | Map `AgentMessage.Content` ↔ `TextPart`; file/data parts via metadata or future extension |
| A2A Task lifecycle is richer (submitted → working → input_required → completed) | Map to `ExecutionStatus`; `input_required` maps to HandoffJob "await response" |
| A2A is opaque by design — agents don't expose tools | Ananke tools stay internal; only *skills* are advertised via AgentCard |
| Push notifications require webhook endpoints | Optional capability — defer; streaming covers most real-time cases |
| Protocol is pre-1.0 | Pin SDK version; breaking changes are SDK-level, not Ananke-level |

### Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| API churn before v1.0 | Medium | Low | Pin NuGet version; thin adapter layer absorbs changes |
| Low ecosystem adoption | Low | Medium | Protocol backed by Linux Foundation, Google, Microsoft; growing community |
| Impedance mismatch for complex Part types | Low | Low | Start with TextPart only; extend incrementally |

## Alternatives Considered

1. **Custom inter-agent HTTP API** — Would duplicate what A2A standardizes, without ecosystem interop.
2. **gRPC-only integration** — More performant but less portable; A2A supports gRPC as a binding option anyway.
3. **Wait for v1.0** — Delays value delivery; the current SDK is functional and changes can be absorbed in a thin wrapper.
4. **Use MCP for agent-to-agent** — MCP is designed for tool connectivity, not peer-to-peer agent collaboration; using it for A2A scenarios would be a misuse of the protocol.

## Consequences

### Positive

- Ananke gains a standard agent-to-agent communication layer.
- Any Ananke workflow can call or be called by external A2A agents.
- The `HandoffJob` pattern is formalized with discovery, error codes, and lifecycle management.
- Positions Ananke in the emerging multi-agent interoperability ecosystem.

### Negative

- New dependency on pre-1.0 NuGet packages (`A2A`, `A2A.AspNetCore`).
- Maintenance burden of tracking protocol evolution until v1.0 stabilizes.
- Additional concepts (AgentCard, Task lifecycle) for users to understand.

### Neutral

- No changes to existing packages; `Ananke.A2A` is purely additive.
- Existing `HandoffJob` / `IHandoffChannel` remain unchanged and usable independently.

## References

- [A2A Protocol Specification](https://a2a-protocol.org/latest/specification/)
- [A2A .NET SDK (GitHub)](https://github.com/a2aproject/a2a-dotnet)
- [A2A NuGet Package](https://www.nuget.org/packages/A2A/)
- [A2A and MCP Relationship](https://a2a-protocol.org/latest/topics/a2a-and-mcp/)
- [Ananke.MCP Integration](../Ananke.MCP/) — template for the integration pattern
