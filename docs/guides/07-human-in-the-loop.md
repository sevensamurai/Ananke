<!-- topic: human-in-the-loop, tags: interrupt, checkpoint, resume, approval, hitl, ask, awaitinput, conversational -->
# 07 — Human-in-the-Loop

Pause workflow execution at any step for human review, checkpoint the full state,
and resume with optional modifications.

**Demo:** [AgenticWebDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/05-applications/AgenticWebDemo)

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
using Ananke.Orchestration.Workflows;

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

## Input-Collecting Turns (`AwaitInput`)

`InterruptBefore`/`InterruptAfter` pause for an approval — a yes/no (or modify-and-continue)
decision. A different shape is a **turn**: pause to collect a free-text reply, then fold it into
state. `AwaitInput` marks a job as exactly that — it pauses before the job like `InterruptBefore`,
plus records it in `WorkflowDefinition.InputJobs`:

```csharp
var workflow = new Workflow<InterviewState>("ask-name")
    .Job("ask_question", async (state, ct) => state)   // no-op anchor for the turn
    .Then("greet", "ask_question")
    .AwaitInput("ask_question")          // pauses before ask_question, like InterruptBefore...
    .UseCheckpointing(checkpointStore);  // ...plus marks it in WorkflowDefinition.InputJobs
```

`InputJobs` is how a host (a Slack bot, a chat UI) tells an input-collecting turn apart from a
plain approval gate when it sees `ExecutionStatus.Interrupted` — useful when a workflow has both
kinds of pause point.

### Resuming a turn

`ResumeAsync(executionId, stateTransform)` still works, but `WorkflowInputExtensions` provides a
channel-agnostic shortcut that folds the reply into state for you:

```csharp
using Ananke.Orchestration.Workflows;

// fold: (state, reply, ct) -> next state, e.g. appending to a transcript
var resumed = await workflow.ResumeWithInputAsync(
    execution.Id, execution.State, userReply, fold);
```

The adapter's only job is correlating the inbound message to `execution.Id` (by
conversation/thread id) — fold-then-resume happens in one call.

### Multi-turn conversations

For a full interview — welcome → icebreaker → a loop of `AwaitInput` turns, with expand/skip/
update navigation over a question agenda — use `AgenticPattern.Interview<TState>` instead of
wiring `AwaitInput` by hand. See [Guide 16 — Agentic Patterns](16-agentic-patterns.md#interview-conversational).

---

## Full Example — Content Approval

From the [AgenticWebDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/05-applications/AgenticWebDemo):

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
| *(custom)* `ICheckpointStore` | Implement to persist across restarts (database, blob storage, etc.) |

```csharp
// In-memory (default for dev)
var store = new InMemoryCheckpointStore();
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

---

## Work-Review Gates

Beyond the generic interrupt/resume pattern, `Ananke.Organics` provides a typed review layer for **work products** — structured items (diffs, documents, plans) that need an explicit approve/revise/reject decision before a workflow continues.

### Core abstractions

```csharp
// The item under review
var item = new WorkItem
{
    Id      = "wi-1",
    Title   = "Add login endpoint",
    Kind    = WorkItemKind.PullRequest,
    Payload = "<diff content>",
};

// A gate decides what to do with it
WorkReviewDecision decision = await gate.ReviewAsync(item, ct);
// decision.Outcome  == WorkReviewOutcome.Approved | Revised | Rejected
// decision.Comment  == reviewer note
```

### Built-in gate implementations

| Gate | Behaviour |
|---|---|
| `AutoWorkReviewGate` | Always approves — useful for automated pipelines |
| `CallbackWorkReviewGate` | Delegates to an `async Func` — wire any custom logic |
| `LlmWorkReviewGate` | Uses a configured `IAgentModel` to review the payload |
| `QuorumWorkReviewGate` | Wraps multiple gates; requires a configurable quorum to approve |

### A note on "gates" — work review vs. division approval

`IWorkReviewGate` above reviews **work items** (diffs, documents, plans) and returns a
`WorkReviewOutcome`. A separate, similarly-named interface, `IDivisionApprovalGate`
(`Ananke.Organics.Division.Approval`), reviews a proposed **cell division** — the governance
checkpoint between `IDivisionPolicy` (which proposes splitting an overloaded cell) and
`IWorkflowDivider` (which executes the split) — and returns a `DivisionApproval`. Both are
human-in-the-loop "gates" in spirit, but they review different things and are not
interchangeable. `BudgetApprovalGate` is a division-approval gate, not a work-review gate:

```csharp
var meter = new InMemoryBudgetMeter(); // Ananke.Abstractions.Budget — rolling 1-hour window by default
var gate  = new BudgetApprovalGate(meter, tokenCap: 100_000);

DivisionApproval approval = await gate.ReviewAsync(plan, snapshot, ct);
// approval.IsApproved == false once the cell's workflow has consumed >= 100_000 tokens
// in the current window — blocks the division rather than a work item.
```

The other built-in `IDivisionApprovalGate` implementations are `AutoApprovalGate` (always
approves — the default), `CallbackApprovalGate` (delegates to an `async Func`), and
`LlmApprovalGate` (uses an `IAgentModel` to review the plan).

---

## Async Review Parking

Some review decisions don't arrive immediately — a Slack reaction, an email reply, or a webhook might come back hours later. The **parking pattern** handles this cleanly:

1. `ReviewAsync` parks the request and returns `WorkReviewOutcome.Pending` immediately
2. The caller records the parking id (returned in `outcome.Comment`)
3. When the decision arrives (e.g. via a Slack block-action webhook), call `ResumeAsync`

```csharp
// Register the parking store (e.g. in DI)
IWorkReviewParkingStore store = new InMemoryWorkReviewParkingStore();
var gate = new ParkingCallbackWorkReviewGate(store, gateId: "content-review");

// In the workflow job
var decision = await gate.ReviewAsync(item, ct);
if (decision.Outcome == WorkReviewOutcome.Pending)
{
    var parkingId = decision.Comment;   // store this with your job execution id
    // workflow suspends here — caller is responsible for checkpointing
}

// Later — when the human decision arrives (e.g. from a Slack interaction handler)
await gate.ResumeAsync(parkingId, WorkReviewDecision.Approve("Looks good", reviewerId: "alice"));
```

### Surfacing the review request

Use `WorkItemReviewNotifier` (in `Ananke.Platforms`) to post the notification to any channel before parking:

```csharp
var notifier = new WorkItemReviewNotifier(responseSink);
await notifier.NotifyAsync(
    workItemId: item.Id,
    title:      item.Title,
    kind:       item.Kind.ToString(),
    payload:    item.Payload,
    channelId:  "C_REVIEWERS",
    threadId:   null);
// Then park:
var outcome = await gate.ReviewAsync(item, ct);
```

The notifier is transport-neutral — it calls `IPlatformResponseSink.SendMessageAsync` and works with any platform. Slack-specific rendering (Block Kit approval buttons via `SlackApprovalBlocks`) is handled by the Slack response sink or a decorator.

---

## What's Next

| Next guide | What you'll learn |
|---|---|
| [08 — State Machine](08-state-machine.md) | Production FSM with distributed locking |
| [09 — Distributed](09-distributed.md) | Redis, MQTT, and agent handoff |

---

← [Back to Learning Path](../learning-path.md)
