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
| `ICheckpointStore` | *(bring your own)* | `InMemoryCheckpointStore` |
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

    Assert.Equal(ExecutionStatus.Completed, result.Status);
    Assert.Equal("DATA", result.State.Clean);
}
```

---

## Testing Agent Workflows Without LLMs

Use `SimulatedAgentModel`. It is an `IStreamingAgentModel` that answers from a script instead of a
provider, so a workflow, a job or a whole demo runs with no key and no network.

```csharp
[Test]
public async Task Agent_workflow_processes_a_scripted_response()
{
    var model = SimulatedAgentModel.Json(new GatherResult { Summary = "Test summary" });

    var job = AgentJobFactory.Create<ResearchState, GatherResult>("gather", model)
        .WithPrompt(s => s.Query)
        .MapResult((s, r) => s with { Facts = r.Summary })
        .Build();

    var result = await new Workflow<ResearchState>("test")
        .Job("gather", job)
        .RunAsync(new ResearchState { Query = "test" });

    result.State.Facts.ShouldBe("Test summary");
}
```

There are four ways to say what it should answer:

| | |
|---|---|
| `SimulatedAgentModel.Fixed(text)` | the same reply to everything |
| `SimulatedAgentModel.Json(value)` | an object, serialized |
| `SimulatedAgentModel.Sequence([...])` | one reply per call, the last repeating |
| `new SimulatedAgentModel(request => ...)` | anything, computed from the request |

### Asserting on what the model was sent

Every request is recorded, which is usually the point of using a fake at all — that a contract
reached the model, that a tool result came back, that the second call saw the first one's output:

```csharp
var model = SimulatedAgentModel.Fixed("ok");
await job.ExecuteAsync(state);

model.Calls.ShouldBe(1);
model.Requests[0].SystemPrompt.ShouldContain("the contract");
```

### Scripts in a file

For anything longer than a couple of replies, put the script in a JSON file. A scripted run in a file
is a reviewable artifact; the same run written as a class is a code change that happens to alter what
a model says — which is the one thing a reviewer of a scripted run needs to see.

```json
{
  "responses": [
    {
      "when": "Parse the input",
      "whenIn": "systemPrompt",
      "replies": [
        { "summary": "the parser does not round-trip yet", "met": false },
        { "summary": "it round-trips", "met": true }
      ]
    },
    { "replies": ["anything else"] }
  ]
}
```

```csharp
var model = SimulatedAgentModel.FromFile("script.json");
```

Four things that matter about the matching:

- **Entries are tried in order and the first match wins**, so put specific ones first and a catch-all
  (no `When`) last.
- **`When` is matched against what the model was *sent*** — never against a label passed alongside
  the request. A real model gets a prompt and nothing else, so a script keyed on anything else is
  testing a channel that does not exist.
- **`WhenIn` narrows where to look** — `systemPrompt`, `messages`, or anywhere. Worth setting when the
  same text can appear in both: a job that pins a contract puts the goal in the system prompt and may
  render surrounding context, naming other work items, into the user turn.
- **Successive calls matching one entry get successive replies**, which is how you script work that
  fails, is fixed, and then passes.

A reply may be written as a JSON object rather than an escaped string — it is handed to the job
verbatim. And a request no entry describes is **refused**, with what was sent quoted in the message:
answering anyway would turn an unscripted request into a passing test that proves nothing.

### Declaring a context window

Set one whenever the run is being measured:

```csharp
var model = SimulatedAgentModel.Fixed("ok", new SimulatedModelOptions
{
    ModelName = "scripted",
    ContextWindowTokens = 8_000,
    InputTokens = 500,
    OutputTokens = 200
});
```

Context instruments report fill and truncation against a known window. A model that declares none
degrades every one of those figures to "no call knew its window", which reads like an instrument
fault rather than a missing declaration. `InputTokens` and `OutputTokens` do the same for anything
measuring cost or budget.

---

## Testing State Machines

```csharp
[Fact]
public async Task Ticket_follows_happy_path()
{
    var lockAndStore = new InMemoryDistributedLock();
    var machine = new TicketMachine(lockAndStore, lockAndStore);
    var ticket = new TicketContext("1");

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
    var lockAndStore = new InMemoryDistributedLock();
    var machine = new TicketMachine(lockAndStore, lockAndStore);
    var ticket = new TicketContext("1");

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
    Assert.Equal(ExecutionStatus.Interrupted, exec.Status);
    Assert.False(exec.State.Executed);

    // Resume with approval
    var resumed = await workflow.ResumeAsync(exec.Id,
        s => s with { Approved = true });
    Assert.Equal(ExecutionStatus.Completed, resumed.Status);
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
        correlationId: Guid.NewGuid().ToString(),
        new TicketHandoff { TicketId = "TK-001", Summary = "Test" },
        timeout: TimeSpan.FromSeconds(5));

    Assert.Equal("Resolved: Test", response.Resolution);
}
```

---

## Test Patterns Summary

| What you're testing | Key technique |
|---|---|
| Workflow logic | Pure state transitions — no mocks needed |
| Agent responses | `SimulatedAgentModel` — fixed, JSON, sequenced, or a script in a file |
| State machine | `InMemoryDistributedLock` — no Redis |
| Knowledge pipeline | `InMemoryKnowledgeStore` — no Qdrant |
| Checkpointing | `InMemoryCheckpointStore` — no files |
| Handoff | `InMemoryHandoffChannel` — no MQTT |
| Conversation memory | `InMemoryConversationMemory` — no Redis |

---

← [Back to Learning Path](../learning-path.md)
