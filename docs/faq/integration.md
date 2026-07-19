<!-- topic: faq-integration, tags: faq, human-in-the-loop, mcp, a2a, interop, approval, checkpoint -->
# FAQ — Integration & Interop

← [Back to all FAQs](../faq.md)

---

## Human-in-the-Loop

### How does human-in-the-loop work?

Mark any workflow job with `.InterruptBefore("job-name")` or `.InterruptAfter("job-name")`.
When execution reaches that point, the workflow:

1. Checkpoints the full typed state to `ICheckpointStore`
2. Returns `ExecutionStatus.Interrupted` to the caller
3. Waits until `workflow.ResumeAsync(executionId, stateModifier)` is called

The human reviews the state, optionally modifies it, then resumes.

```csharp
// First run: pauses before "execute"
var execution = await workflow.RunAsync(initialState);
// execution.Status == Interrupted

// Human approves — inject their decision
var resumed = await workflow.ResumeAsync(
    execution.Id,
    state => state with { Approved = true });
// resumed.Status == Completed
```

### How is state persisted across process restarts?

`ICheckpointStore` serializes the full workflow state. Two implementations are provided:

- `InMemoryCheckpointStore` — for tests and single-process scenarios

Implement `ICheckpointStore` to back checkpoints with a database, filesystem, or cloud storage.

---

## MCP & Interoperability

### What is MCP and does Ananke support it?

[MCP](https://modelcontextprotocol.io/) (Model Context Protocol) is a standard for connecting
LLM clients to external tools and data. Ananke supports both directions:

- **Expose** — turn any `ToolKit` or `Workflow` into an MCP server with `WithAnankeTools()`
  and `WithAnankeWorkflow<T>()`. Compatible with VS Code Copilot, Claude Desktop, and any
  MCP-compliant client.
- **Consume** — import tools from any external MCP server into a `ToolKit` via
  `AddMcpServerToolsAsync()`.

See [MCP & Interop](../guides/12-mcp-and-interop.md).

### What is A2A and does Ananke support it?

[A2A](https://a2a-protocol.org/) (Agent-to-Agent) is a protocol for direct agent-to-agent
communication over HTTP + JSON-RPC. Ananke supports both directions:

- **Client** — `A2AAgentModel` calls any remote A2A agent as a drop-in `IStreamingAgentModel`.
  Use it directly in workflows and `AgentJob`s just like any local model.
- **Server** — expose Ananke workflows as A2A-compliant endpoints that any A2A client
  (including non-.NET clients) can call.

> **A2A** is for agent-to-agent communication. **MCP** is for agent-to-tool communication.
> Ananke supports both.

### Can I call a remote agent from inside a workflow?

Yes. Wrap the remote agent with `A2AAgentModel` (for A2A) or use the `IStreamingAgentModel`
it produces. Pass it to any `AgentJob` in the workflow — the workflow has no knowledge of
whether the model is local or remote.

---

← [Back to all FAQs](../faq.md) · [Feature Index](../reference/features.md) · [Getting Started](../guides/01-getting-started.md)
