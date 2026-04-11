<!-- topic: testing, tags: testing, in-memory, integration, unit, simulated-model -->
# 14 — Testing

Test workflows, agents, state machines, and infrastructure without LLMs or
external services using Ananke's in-memory implementations.

---

## Design Principle

Every infrastructure contract in Ananke has a zero-config in-memory
implementation. Integration tests run in milliseconds with no API keys,
no Docker containers, and no network access.

---

## In-Memory Implementations

| Contract | Production | Test / Dev |
|---|---|---|
| `IDistributedLock` | `RedisDistributedLock` | `InMemoryDistributedLock` |
| `IKnowledgeStore` | `QdrantKnowledgeStore` | `InMemoryKnowledgeStore` |
| `IKnowledgeCatalog` | `QdrantKnowledgeCatalog` | `InMemoryKnowledgeCatalog` |
| `IConversationMemory` | `RedisConversationMemory` | `InMemoryConversationMemory` |
| `ICheckpointStore` | `FileCheckpointStore` | `InMemoryCheckpointStore` |
| `IHandoffChannel` | `MqttHandoffChannel` | `InMemoryHandoffChannel` |
| `IKeyValueDataAdapter` | `RedisDataAdapter` | (in-memory via `Dictionary`) |

---

## Testing Workflows

Workflows are pure functions over typed state — no mocking needed:

```csharp
[Fact]
public async Task Pipeline_produces_expected_output()
{
    var workflow = new Workflow<PipelineState>("test-pipeline")
        .Job("fetch",     async (s, ct) => s with { Raw = "data" })
        .Job("transform", async (s, ct) => s with { Clean = s.Raw.ToUpperInvariant() })
        .Chain("fetch", "transform")
        .Then("transform", Workflow.End);

    var result = await workflow.RunAsync(new PipelineState());

    Assert.Equal(WorkflowStatus.Completed, result.Status);
    Assert.Equal("DATA", result.State.Clean);
}
```

---

## Testing Agent Workflows Without LLMs

Create a test model that returns predictable responses:

```csharp
public class FakeAgentModel : IStreamingAgentModel
{
    private readonly string _response;

    public FakeAgentModel(string response) => _response = response;

    public Task<AgentResponse> GenerateAsync(AgentRequest request,
        CancellationToken ct = default)
    {
        return Task.FromResult(new AgentResponse { Text = _response });
    }

    public async IAsyncEnumerable<StreamingAgentChunk> GenerateStreamAsync(
        AgentRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new StreamingAgentChunk { Text = _response };
    }
}
```

```csharp
[Fact]
public async Task Agent_workflow_processes_fake_response()
{
    var fakeModel = new FakeAgentModel("{\"Summary\": \"Test summary\"}");

    var job = new AgentJob<ResearchState, GatherResult>
        .Builder("gather", fakeModel)
        .WithSystemPrompt("Test")
        .WithPrompt(s => s.Query)
        .MapResult((s, r) => s with { Facts = r.Summary })
        .Build();

    var workflow = new Workflow<ResearchState>("test")
        .Job("gather", job)
        .Then("gather", Workflow.End);

    var result = await workflow.RunAsync(new ResearchState { Query = "test" });

    Assert.Equal("Test summary", result.State.Facts);
}
```

---

## Testing State Machines

```csharp
[Fact]
public async Task Ticket_follows_happy_path()
{
    var machine = new TicketMachine(new InMemoryDistributedLock());
    var ticket = new TicketContext(1);

    // Open → InProgress
    var r1 = await machine.TransitionAsync(ticket, TicketTransition.Assign);
    Assert.True(r1.Success);
    Assert.Equal(TicketState.InProgress, r1.CurrentState);

    // InProgress → Resolved (with guard)
    machine.ResolutionNote = "Fixed";
    var r2 = await machine.TransitionAsync(ticket, TicketTransition.Resolve);
    Assert.True(r2.Success);
    Assert.Equal(TicketState.Resolved, r2.CurrentState);
}

[Fact]
public async Task Guard_blocks_resolve_without_note()
{
    var machine = new TicketMachine(new InMemoryDistributedLock());
    var ticket = new TicketContext(1);

    await machine.TransitionAsync(ticket, TicketTransition.Assign);

    machine.ResolutionNote = null;
    var result = await machine.TransitionAsync(ticket, TicketTransition.Resolve);
    Assert.False(result.Success);
}
```

---

## Testing Human-in-the-Loop

```csharp
[Fact]
public async Task Interrupt_and_resume_works()
{
    var store = new InMemoryCheckpointStore();

    var workflow = new Workflow<ApprovalState>("test-approval")
        .Job("analyze", async (s, ct) => s with { Analysis = "done" })
        .Job("execute", async (s, ct) => s with { Executed = true })
        .Chain("analyze", "execute")
        .Then("execute", Workflow.End)
        .InterruptBefore("execute")
        .UseCheckpointing(store);

    // First run — pauses
    var exec = await workflow.RunAsync(new ApprovalState());
    Assert.Equal(WorkflowStatus.Interrupted, exec.Status);
    Assert.False(exec.State.Executed);

    // Resume with approval
    var resumed = await workflow.ResumeAsync(exec.Id,
        s => s with { Approved = true });
    Assert.Equal(WorkflowStatus.Completed, resumed.Status);
    Assert.True(resumed.State.Executed);
    Assert.True(resumed.State.Approved);
}
```

---

## Testing Knowledge Pipeline

```csharp
[Fact]
public async Task Document_ingestion_and_search()
{
    var embedding = OpenAIEmbeddingModel.Create(apiKey);  // or a fake
    var store = new InMemoryKnowledgeStore(embedding);
    var processor = new DocumentProcessor(
        new HttpClient(),
        [new MarkdownExtractor()],
        new SlidingWindowChunker(),
        store);

    var markdown = "# Guide\n\nThis is about distributed systems.";
    using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(markdown));
    var result = await processor.ProcessAsync(stream, ".md", "test-doc");

    Assert.True(result.Chunks > 0);

    var hits = await store.SearchAsync("distributed systems");
    Assert.NotEmpty(hits);
}
```

---

## Testing Handoff Channels

```csharp
[Fact]
public async Task Handoff_round_trip()
{
    var channel = new InMemoryHandoffChannel();

    channel.RegisterHandler<TicketHandoff, SpecialistResult>(
        "test-queue",
        async ticket => new SpecialistResult
        {
            Resolution = $"Resolved: {ticket.Summary}",
            HandledBy = "test-agent"
        });

    var response = await channel.SendAsync<TicketHandoff, SpecialistResult>(
        "test-queue",
        new TicketHandoff { TicketId = "TK-001", Summary = "Test" });

    Assert.Equal("Resolved: Test", response.Resolution);
}
```

---

## Test Patterns Summary

| What you're testing | Key technique |
|---|---|
| Workflow logic | Pure state transitions — no mocks needed |
| Agent responses | Fake `IStreamingAgentModel` with canned responses |
| State machine | `InMemoryDistributedLock` — no Redis |
| Knowledge pipeline | `InMemoryKnowledgeStore` — no Qdrant |
| Checkpointing | `InMemoryCheckpointStore` — no files |
| Handoff | `InMemoryHandoffChannel` — no MQTT |
| Conversation memory | `InMemoryConversationMemory` — no Redis |

---

← [Back to Learning Path](../learning.md)
