<!-- topic: human-in-the-loop, tags: interrupt, checkpoint, resume, approval, hitl -->
# 07 — Human-in-the-Loop

Pause workflow execution at any step for human review, checkpoint the full state,
and resume with optional modifications.

**Demo:** [AgenticWebDemo](../../src/demos/AgenticWebDemo/)

---

## Core Concepts

Human-in-the-loop in Ananke works through three mechanisms:

1. **Interrupt** — mark a job as requiring human approval before or after it runs
2. **Checkpoint** — persist the full workflow state so it survives process restarts
3. **Resume** — continue execution from the checkpoint, optionally modifying state

---

## Interrupt Before a Job

Pause the workflow before a specific job executes:

```csharp
using Ananke.Orchestration;
using Ananke.Orchestration.Checkpointing;

var checkpointStore = new InMemoryCheckpointStore();

var workflow = new Workflow<ApprovalState>("trade-approval")
    .Job("analyze", async (state, ct) =>
        state with { Analysis = "Stock looks promising" })
    .Job("review", async (state, ct) =>
        state with { ReviewNotes = "Pending human approval" })
    .Job("execute", async (state, ct) =>
        state with { Executed = true })
    .Chain("analyze", "review", "execute")
    .Then("execute", Workflow.End)
    .InterruptBefore("execute")          // ← pause here
    .UseCheckpointing(checkpointStore);

// First run: executes analyze → review → pauses before execute
var execution = await workflow.RunAsync(new ApprovalState());
// execution.Status == Interrupted
```

---

## Resume with Modified State

After the human reviews and approves, resume from the checkpoint:

```csharp
// Human approves — inject their decision into state
var resumed = await workflow.ResumeAsync(
    execution.Id,
    state => state with { Approved = true });

// resumed.Status == Completed
// resumed.State.Executed == true
```

The `ResumeAsync` callback receives the checkpointed state and returns a
modified version. The workflow continues from exactly where it paused.

---

## Interrupt After a Job

Pause after a job completes (useful for reviewing results before proceeding):

```csharp
var workflow = new Workflow<ContentState>("content-pipeline")
    .Chain("draft", "review", "publish")
    .Then("publish", Workflow.End)
    .InterruptAfter("review")    // pause after review completes
    .UseCheckpointing(checkpointStore);
```

---

## Full Example — Content Approval

From the [ExtendedFlowDemo](../../src/demos/ExtendedFlowDemo/):

```csharp
var checkpointStore = new InMemoryCheckpointStore();

var workflow = new Workflow<State>("content-approval")
    .Job("draft", async (state, ct) =>
    {
        Console.WriteLine("  [draft] Writing initial draft...");
        return state with { Draft = "Here is the article draft about Ananke." };
    })
    .Job("review", async (state, ct) =>
    {
        Console.WriteLine("  [review] Auto-reviewing draft...");
        return state with { ReviewNotes = "Looks good — pending human approval." };
    })
    .Job("publish", async (state, ct) =>
    {
        Console.WriteLine("  [publish] Publishing approved content...");
        return state with { Published = true };
    })
    .Chain("draft", "review", "publish")
    .Then("publish", Workflow.End)
    .InterruptBefore("publish")
    .UseCheckpointing(checkpointStore);

// Run 1: pauses before publish
var execution = await workflow.RunAsync(new State());
Console.WriteLine($"  Status: {execution.Status}");  // Interrupted

// Human reviews and approves...

// Run 2: resumes and completes
var resumed = await workflow.ResumeAsync(
    execution.Id,
    state => state with { Approved = true });
Console.WriteLine($"  Published: {resumed.State.Published}");  // true
```

---

## Checkpoint Stores

| Implementation | Use case |
|---|---|
| `InMemoryCheckpointStore` | Dev/test — state lives in memory |
| `FileCheckpointStore` | Persist across restarts via local files |

```csharp
// In-memory (default for dev)
var store = new InMemoryCheckpointStore();

// File-based (survives process restarts)
var store = new FileCheckpointStore("./checkpoints");
```

---

## Web Integration — SSE with Approval

In a web API, the interrupt/resume pattern maps naturally to HTTP:

```csharp
// POST /api/workflow/start → returns execution ID + interrupted state
// POST /api/workflow/{id}/approve → resumes with approval

app.MapPost("/api/workflow/{id}/approve", async (string id, ApprovalRequest req) =>
{
    var resumed = await workflow.ResumeAsync(id,
        state => state with { Approved = req.Approved, ApproverNotes = req.Notes });
    return Results.Ok(resumed.State);
});
```

---

## What's Next

| Next guide | What you'll learn |
|---|---|
| [08 — State Machine](08-state-machine.md) | Production FSM with distributed locking |
| [09 — Distributed](09-distributed.md) | Redis, MQTT, and agent handoff |

---

← [Back to Learning Path](../learning.md)
