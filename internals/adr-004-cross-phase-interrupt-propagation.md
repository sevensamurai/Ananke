# ADR-004: State-Machine-Driven Interrupt Propagation

| Field         | Value                                                          |
|---------------|----------------------------------------------------------------|
| **Status**    | Proposed                                                       |
| **Date**      | 2025-07-24                                                     |
| **Authors**   | —                                                              |
| **Deciders**  | Ananke maintainers                                             |
| **Tags**      | interrupts, workflow, tools, state-machine, streaming           |
| **Relates to**| ADR-002 (Layers 3–4), `StreamingChatWorkflow`, `ChatSessionHandle`, `Ananke.Bridge` |

---

## Context

### The developer experience goal

If a developer declares a state machine with interrupt-enabled transitions,
**everything else should flow** — the workflow engine, agent streaming, and
tool execution should all respect those interrupts without per-layer plumbing.

Today that is not the case. Ananke has **three independent interrupt
mechanisms** that do not talk to each other:

### Inventory of interrupt mechanisms

| # | Mechanism | Package | Scope | Signal type | Cancels in-flight work? |
|---|-----------|---------|-------|-------------|------------------------|
| 1 | **`ToInterrupt(S)` / `ToResume()`** | `Ananke.StateMachine` | Declarative FSM transitions with push/pop interrupt stack | `TransitionAsync()` → `TransitionResult.WasInterrupt` | ❌ No — transitions the *state*, but whatever async work the current state was doing keeps running |
| 2 | **`InterruptMode.Before` / `After`** | `Ananke.Orchestration` | Checkpoint-based pause between workflow jobs | Checkpoint + `ExecutionStatus.Interrupted` → `ResumeAsync()` | ❌ No — pauses *between* jobs; cannot interrupt a running job |
| 3 | **`ChatSessionHandle.InterruptAsync()`** | `Ananke.Orchestration` | Channel-based mid-stream interrupt in `BuildInterruptible()` | `Channel<AgentMessage>` + `CancellationTokenSource.Cancel()` | ⚠️ Partially — cancels the agent job's CTS, but not the tools job (disposed CTS bug) |

### How they relate today

```
                    ┌──────────────────────────────────────────┐
                    │        Ananke.StateMachine                │
                    │  ToInterrupt(S) / ToResume()              │
                    │  InterruptStack, IsInterrupted             │
                    │  Guards, Middleware, OnEnter/OnExit        │
                    └──────────┬──────────────┬────────────────┘
                               │              │
                   ┌───────────▼──┐    ┌──────▼──────────────┐
                   │ Bridge:      │    │ Bridge:              │
                   │ SM→Workflow  │    │ Workflow→SM          │
                   │ OnEnterRun   │    │ StateMachineTrigger  │
                   │ Workflow()   │    │ Job                  │
                   └───────────┬──┘    └──────┬──────────────┘
                               │              │
                    ┌──────────▼──────────────▼────────────────┐
                    │        Ananke.Orchestration               │
                    │  Workflow<T>, WorkflowRunner               │
                    │  InterruptMode.Before/After (checkpoint)   │
                    │  StreamingChatWorkflow.BuildInterruptible()│
                    │    └─ Channel<AgentMessage> (agent only)   │
                    │    └─ CTS per agent job (disposed bug)     │
                    └─────────────────────────────────────────-──┘
```

The bridges are **one-directional workflow triggers** — they fire transitions
or start workflows, but they do not propagate interrupt signals across the
boundary.

### Gaps identified from the Pet Adoption Demo

Testing the demo with the send-button fix revealed concrete symptoms:

**Gap 1 — Interrupts are invisible during tool execution.**
`interruptReader.TryRead()` is only called in the `"agent"` job's streaming
loop. The `"tools"` job never checks. When a user interrupts during
`browse_pets` (the most likely moment — tools have visible latency), the
message sits in the channel until tools finish. Then the next agent round
picks it up immediately with empty `partialText`.

**Gap 2 — Disposed CTS during tools.**
The `genCts` registered with `ChatSessionHandle` via `RegisterGenerationCts()`
is `using`-scoped to the agent job. When the agent job completes and the tools
job starts, `_currentGenerationCts` points to a disposed CTS. Calling
`Cancel()` from `InterruptAsync` is a no-op — it cannot cancel the running
tool.

**Gap 3 — No interrupt framing.**
The client sends raw text (`"also for granny"`) and the server injects it as a
bare `AgentMessage.User()`. The LLM sees it as a standalone query, not an
addendum, because there is no conversational framing.

**Gap 4 — `StreamingChatWorkflow` is sealed.**
Its internal channel, event writer, and CTS management are not extensible. A
developer who wants custom phases (searching → forms → payment) must rebuild
the entire streaming plumbing from scratch. The state machine's declarative
interrupt model cannot drive it.

### The deeper issue

These are not four independent bugs. They are symptoms of a missing
abstraction: **there is no unified interrupt signal that flows from the state
machine (which knows the protocol) through the workflow (which manages jobs)
into the running work (which holds the CancellationToken).**

The state machine already has the right primitives for declaring interrupts:

```csharp
// This already works:
.From(Responding).On(Interrupt).ToInterrupt(Clarifying)
    .When(() => AllowInterrupt)
.From(Clarifying).On(Resume).ToResume()
```

But firing `TransitionAsync(ctx, Interrupt)` only changes the FSM state. It
does not:
- Cancel whatever async work the `Responding` state was doing
- Deliver a message (the interrupt payload) to the interrupted work
- Signal the workflow engine to re-route

---

## Analysis: What would "interrupt flows from the state machine" require?

### Layer map

```
┌─────────────────────────────────────────────────────────────────┐
│  State Machine (declares WHAT)                                   │
│  "From Responding, on Interrupt, push stack, go to Clarifying"   │
│  Knows: valid transitions, guards, interrupt depth               │
│  Doesn't know: what work is running, how to cancel it            │
├─────────────────────────────────────────────────────────────────┤
│  Workflow Engine (manages HOW)                                    │
│  "Run job 'agent', then decide, then job 'tools', then loop"     │
│  Knows: job graph, checkpoints, fork/join, timeout cancellation   │
│  Doesn't know: FSM state, interrupt semantics                    │
├─────────────────────────────────────────────────────────────────┤
│  Job Execution (does THE WORK)                                    │
│  "Stream LLM tokens" / "Execute browse_pets" / "Process payment"  │
│  Knows: its CancellationToken                                     │
│  Doesn't know: FSM or workflow structure                          │
└─────────────────────────────────────────────────────────────────┘
```

For an interrupt to propagate top-to-bottom, we need:

| From → To | What must happen | Exists today? |
|-----------|------------------|---------------|
| **External → StateMachine** | `TransitionAsync(ctx, Interrupt)` with payload | ✅ Transition works, ❌ no payload carrier |
| **StateMachine → Workflow** | Cancel the current workflow job, deliver interrupt message | ❌ Bridge is fire-and-forget, no back-channel |
| **Workflow → Job** | Cancel the `CancellationToken`, deliver message to the job | ⚠️ `InterruptMode` pauses between jobs; channel exists for agent but not tools; CTS bug |
| **Job → Workflow** | Return `WasInterrupted = true` so the router re-routes | ⚠️ Agent job does this; tools job does not |

### Gap inventory across layers

#### StateMachine layer

| Gap | Description |
|-----|-------------|
| **No interrupt payload** | `ToInterrupt(S)` transitions to a state but carries no data. There is no `AgentMessage` or arbitrary payload on the transition. The interrupt *reason* (the user's text) has nowhere to live. |
| **No CTS ownership** | The state machine has no concept of "the async work being done in the current state." It cannot cancel anything. |
| **`OnExit` is fire-and-forget** | State exit actions (`OnExitAction`) are `Func<Task>` with no cancellation and no way to signal "stop what you're doing." |

#### Bridge layer

| Gap | Description |
|-----|-------------|
| **Unidirectional** | `StateMachineTriggerJob` (workflow → SM) and `WorkflowTriggerAction` (SM → workflow) fire-and-forget. Neither propagates cancellation or interrupt signals back. |
| **No `InterruptTriggerJob`** | There is no bridge primitive that says "when the FSM enters an interrupt state, cancel the running workflow job and deliver this message." |
| **`WorkflowCompletionTrigger` doesn't handle interrupts** | If the workflow is interrupted mid-execution, the completion trigger never fires. |

#### Workflow / StreamingChat layer

| Gap | Description |
|-----|-------------|
| **`InterruptMode` is checkpoint-based** | `Before`/`After` persists and stops. It doesn't cancel a running job — it only fires between jobs. |
| **Agent-only channel** | `BuildInterruptible()` creates a `Channel<AgentMessage>` read only in the agent job. |
| **Disposed CTS** | `RegisterGenerationCts()` is called in the agent job with a `using var genCts`. The tools job inherits a stale reference. |
| **No interrupt framing** | Raw message injection with no contextual wrapper. |

#### Tool execution layer

| Gap | Description |
|-----|-------------|
| **Tools ignore the interrupt channel** | `executor.ExecuteAsync(args, jobCt)` uses the job's CT, not a linked CTS that the interrupt can cancel. |
| **No cooperative interrupt check** | No `TryRead()` between sequential tool calls. |

---

## Decision

We will **not** attempt a full top-to-bottom state-machine-driven interrupt in
one step. The gap analysis shows this requires changes across four packages
(`StateMachine`, `Bridge`, `Orchestration`, and the demo). Instead, we
sequence the work bottom-up so each increment is independently shippable and
the demo gets better immediately.

### Increment 1 — Fix the tools job (bottom-up, immediate)

Make the `"tools"` job in `BuildInterruptible()` cooperative with the existing
interrupt channel. This fixes Gaps 1–3 without any cross-package changes.

**Changes to `StreamingChatWorkflow.Builder.BuildInterruptible()`:**

| Component | Change |
|-----------|--------|
| **tools job: CTS** | Create a `using var toolsCts = CancellationTokenSource.CreateLinkedTokenSource(jobCt)` and call `handle.RegisterGenerationCts(toolsCts)` so `InterruptAsync` can cancel in-flight tools. |
| **tools job: channel check** | Call `interruptReader.TryRead()` before each tool dispatch. On interrupt: add the message to history, emit `InterruptedEvent`, return `WasInterrupted = true`. |
| **tools job: catch cancel** | Wrap `ExecuteAsync` in `try/catch(OperationCanceledException)` — on cancellation by interrupt (not workflow shutdown), read the channel and handle as above. |
| **agent job: framing** | When injecting the interrupt message into `state.Messages`, wrap it: `"[The user interrupted to refine their request]: {original.Content}"`. Make the framing template configurable via a new `BuildInterruptible` overload parameter. |

**Proposed tools job:**

```csharp
.Job("tools", async (state, jobCt) =>
{
    using var toolsCts = CancellationTokenSource.CreateLinkedTokenSource(jobCt);
    handle.RegisterGenerationCts(toolsCts);

    state.Messages.Add(AgentMessage.Assistant(
        state.LastResponse!.Text ?? string.Empty,
        state.LastResponse.ToolCalls));

    foreach (var call in state.LastResponse.ToolCalls!)
    {
        // Check for interrupt before dispatching each tool
        if (interruptReader.TryRead(out var interruptMsg))
        {
            state.Messages.Add(FrameInterrupt(interruptMsg));
            await eventWriter.WriteAsync(new InterruptedEvent(string.Empty), jobCt);
            return state with { WasInterrupted = true, LastResponse = null,
                                PartialText = null, FullText = string.Empty };
        }

        await eventWriter.WriteAsync(
            new ToolCallEvent(call.FunctionName, call.Arguments), jobCt);

        var args = ParseToolArgs(call.Arguments);
        ToolResult toolResult;
        try
        {
            toolResult = toolKit!.Tools.TryGetValue(call.FunctionName, out var executor)
                ? await executor.ExecuteAsync(args, toolsCts.Token)
                : ToolResult.Error($"Unknown tool: {call.FunctionName}");
        }
        catch (OperationCanceledException) when (!jobCt.IsCancellationRequested)
        {
            if (interruptReader.TryRead(out var msg))
            {
                state.Messages.Add(FrameInterrupt(msg));
                await eventWriter.WriteAsync(new InterruptedEvent(string.Empty), jobCt);
                return state with { WasInterrupted = true, LastResponse = null,
                                    PartialText = null, FullText = string.Empty };
            }
            throw;
        }

        await eventWriter.WriteAsync(
            new ToolResultEvent(call.FunctionName, toolResult.Value), jobCt);
        state.Messages.Add(AgentMessage.ToolResult(call.Id, toolResult.Value));
    }

    return state with { LastResponse = null, FullText = string.Empty,
                         ToolRounds = state.ToolRounds + 1 };
})
```

### Increment 2 — Interrupt payload on the state machine

Add an optional data carrier to interrupt transitions so the FSM can hold the
interrupt reason/message alongside the state change.

**Changes to `Ananke.StateMachine`:**

| Component | Change |
|-----------|--------|
| `PersistedContext<S>` | Add `object? InterruptPayload` alongside the `InterruptStack`. |
| `TransitionResult<S>` | Add `object? InterruptPayload` — populated when `WasInterrupt = true`. |
| `IActionStateMachine` | Add `TransitionAsync(C context, T transition, object? payload)` overload. |
| `TryExecuteTransitionAsync` | When `config.IsInterrupt`, store the payload in `PersistedContext`. On `IsResume`, restore it (or discard). |

This keeps the state machine focused on what it's good at (declarative
protocol) while enabling it to carry the interrupt *reason*.

### Increment 3 — Bidirectional bridge with CTS propagation

Create a new bridge primitive that connects a state machine interrupt
transition to the cancellation of a running workflow job.

**New: `InterruptableBridge<TWorkflowState, ...>`**

Concept: the bridge holds a `CancellationTokenSource` that is threaded into
the workflow's job execution. When the FSM fires an interrupt transition, the
bridge:

1. Cancels the CTS (stopping in-flight work)
2. Extracts the interrupt payload from `TransitionResult`
3. Writes it to the workflow's interrupt channel (for `BuildInterruptible`)
   or sets `WasInterrupted` on the workflow state (for custom workflows)

```csharp
// Conceptual API — not final
var bridge = new InterruptableBridge<ChatState, ConvoCtx, ConvoState, ConvoAction, ConvoNotify>(
    stateMachine,
    interruptTransition: ConvoAction.Interrupt,
    resumeTransition: ConvoAction.Resume,
    onInterrupt: (workflowState, payload) =>
        workflowState with { WasInterrupted = true, InterruptMessage = payload });

// The bridge exposes a CTS that the workflow uses:
var handle = StreamingChatWorkflow.Create("chat", model)
    .WithTools(tools)
    .BuildInterruptible(messages, bridge.Token);  // bridge owns the CTS

// External interrupt fires through the FSM:
await stateMachine.TransitionAsync(ctx, ConvoAction.Interrupt,
    payload: AgentMessage.User("also for granny"));
// → bridge.CTS is cancelled
// → bridge writes to the interrupt channel
// → tools job catches OperationCanceledException
// → agent re-generates with framed interrupt message
```

### Increment 4 — Composable streaming phases (future ADR)

With Increments 1–3 in place, a developer can define a full conversational
state machine where each state maps to a workflow phase, and interrupts
propagate automatically:

```csharp
enum Phase { Searching, Details, Form, Payment, Done }
enum Action { Search, Select, Fill, Pay, Interrupt, Resume, Complete }

// State machine declares the protocol:
.From(Phase.Searching).On(Action.Select).To(Phase.Details)
.From(Phase.Details).On(Action.Fill).To(Phase.Form)
.From(Phase.Form).On(Action.Pay).To(Phase.Payment)
.FromAny(Phase.Searching, Phase.Details, Phase.Form)
    .On(Action.Interrupt).ToInterrupt(Phase.Searching)  // any phase → interrupt
.From(Phase.Searching).On(Action.Resume).ToResume()
.From(Phase.Payment).On(Action.Complete).To(Phase.Done)

// Workflow maps each phase to work:
new Workflow<AdoptionState>("adoption")
    .Job("searching", searchingJob)       // uses StreamingChatWorkflow internally
    .Job("details",   detailsJob)
    .Job("form",      formJob)
    .Job("payment",   paymentJob)
    .Then("searching", Workflow.Decide<AdoptionState>(s => /* FSM-driven routing */))
    ...

// Bridge connects them:
var bridge = new InterruptableBridge<...>(stateMachine, ...);
// Any phase is interruptible because the FSM says so.
```

---

## Architecture

### Current: three disconnected interrupt mechanisms

```
  StateMachine                    Workflow                  Job
┌──────────────┐            ┌─────────────────┐     ┌──────────────┐
│ ToInterrupt() │            │ InterruptMode   │     │ Channel<Msg> │
│ ToResume()    │            │ Before/After    │     │ (agent only) │
│ InterruptStack│            │ (checkpoint)    │     │ CTS (stale)  │
└──────┬───────┘            └────────┬────────┘     └──────┬───────┘
       │                             │                      │
       │  ── no connection ──        │  ── no connection ── │
       │                             │                      │
```

### Target: state machine drives, workflow propagates, job cancels

```
  StateMachine                    Bridge                   Workflow + Job
┌──────────────┐            ┌─────────────────┐     ┌──────────────────────┐
│ ToInterrupt() │───payload──►│ Interruptable  │──►  │ Cancel CTS           │
│   + payload   │            │ Bridge          │     │ Write to channel     │
│ ToResume()    │◄──resume───│                 │◄──  │ WasInterrupted=true  │
│ InterruptStack│            │ Owns shared CTS │     │ Re-route to agent    │
└──────────────┘            └─────────────────┘     └──────────────────────┘
       │                             │                      │
       └─────── single signal path ──┴──────────────────────┘
```

### Detailed flow: interrupt during tool execution

```
Browser                 Server (FSM+Bridge)           Workflow (tools job)
───────                 ───────────────────           ──────────────────────
 [type "also for       POST /api/interrupt
  granny", hit Send]   ─► TransitionAsync(
                           ctx, Interrupt,
                           payload: "also for granny")
                        ─► FSM: push Responding,
                           move to Clarifying,
                           return WasInterrupt=true
                        ─► Bridge: cancel shared CTS
                           + write AgentMessage
                           to interrupt channel
                                                      ─► browse_pets throws
                                                         OperationCanceledException
                                                      ─► catch: TryRead → got msg
                                                      ─► FrameInterrupt(msg)
                                                      ─► emit InterruptedEvent
                                                      ─► return WasInterrupted=true
                                                      ─► router → back to agent job
                                                      ─► agent re-generates with
                                                         full context + framed msg
```

---

## Alternatives Considered

### A. Make the streaming chat channel the sole interrupt mechanism

Have the workflow's `Channel<AgentMessage>` be the only interrupt path, and
skip the state machine entirely.

**Rejected:** This is what we have today (minus the tools gap). It works for
the simple `agent → tools → agent` loop but doesn't generalize to custom
multi-phase workflows. There is no protocol-level validation (any message can
"interrupt" at any time), no interrupt stack, no guard conditions, and no
formal resume. The state machine already provides all of these.

### B. Replace the channel with FSM transitions entirely

Remove `Channel<AgentMessage>` from `BuildInterruptible` and have all
interrupts go through `TransitionAsync`.

**Rejected for now:** The channel provides a lightweight, in-process signal
path that doesn't require distributed locking. For the common single-session
chat scenario, it's simpler. The bridge should **connect** the FSM to the
channel, not replace it. Developers who don't need a state machine can still
use the channel directly.

### C. Add `CancellationToken` to `OnExitAction`

Make state exit actions cancellation-aware so the FSM can cancel in-flight
work when leaving a state.

**Partially useful but insufficient:** `OnExitAction` fires after the state
has already transitioned. We need to cancel *before* the exit, while the
state's work is still running. The bridge's CTS approach handles this better.

### D. Implement everything in Increment 1

Ship the full FSM-driven interrupt propagation as one big change.

**Rejected:** Too much cross-package risk. The bottom-up approach lets us fix
the demo immediately (Increment 1), then build the abstraction layers
incrementally with the demo validating each step.

---

## Consequences

### Positive

- **"Declare once, interrupt everywhere":** A developer who sets up a state
  machine with `ToInterrupt(S)` gets interrupt propagation through the workflow
  and into running jobs without additional wiring.
- **Backward compatible:** Increment 1 is internal to `BuildInterruptible()`.
  Increments 2–3 add new APIs but don't break existing ones. Developers who
  don't use a state machine are unaffected.
- **Immediate demo improvement:** Increment 1 alone fixes the tools-phase
  blind spot and the framing issue in the Pet Adoption Demo.
- **Testable protocol:** The state machine's guards, middleware, and tracing
  apply to interrupts, making them auditable and testable.

### Negative / Risks

- **Partial tool results on interrupt:** If 2 of 3 tools have completed when
  an interrupt arrives, the conversation contains incomplete tool context. The
  LLM must handle this. Acceptable — the user chose to interrupt.
- **Tool side effects are not rolled back:** A tool that wrote to a database
  before the interrupt cannot be undone by the framework. Tools with side
  effects should document this.
- **Bridge complexity:** The `InterruptableBridge` holds shared mutable state
  (CTS, channel reference). Correct lifecycle management across
  `async`/distributed boundaries needs careful design.
- **Two interrupt paths coexist:** Until Increment 3 ships, the channel-based
  and FSM-based paths are independent. Documentation must be clear about when
  to use which.

---

## Implementation Plan

| Phase | Scope | Package | Effort | Dependencies | Status |
|-------|-------|---------|--------|-------------|--------|
| **1a** | Tools job: CTS registration + interrupt channel check + catch cancel | `Ananke.Orchestration` | S | None | ✅ Done |
| **1b** | Agent/tools job: interrupt message framing (configurable template) | `Ananke.Orchestration` | S | None | ✅ Done |
| **1c** | Pet Adoption Demo: system prompt guidance + client-side framing | Demo | XS | 1b | ✅ Done |
| **2** | `TransitionAsync` payload overload + `TransitionResult.InterruptPayload` | `Ananke.StateMachine` | S | None | ✅ Done |
| **3** | `InterruptableBridge`: CTS ownership + channel write on FSM interrupt | `Ananke.Bridge` | M | 1a, 2 | ✅ Done |
| **4** | `InterruptableSession` extraction + composable streaming phases | `Ananke.Orchestration` | M | 3 | ✅ Done |

Phases 1a–1c can ship together as a single PR.
Phases 2 and 3 can follow independently once validated on the demo.
