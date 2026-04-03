# ADR-018 — Bidirectional Agent-User Dialogue: Agent-Pull Confirmation

| Field          | Value                                                                                                      |
|----------------|------------------------------------------------------------------------------------------------------------|
| **Status**     | Proposed                                                                                                   |
| **Date**       | 2025-07-29                                                                                                 |
| **Authors**    | —                                                                                                          |
| **Deciders**   | Ananke maintainers                                                                                         |
| **Tags**       | human-in-the-loop, interrupt, bidirectional, confirmation, tool-calls, conversational-fluency, sse         |
| **Relates to** | ADR-004 (interrupt propagation), ADR-017 (dynamic interrupt policy), `StreamingChatWorkflow`, `StateMachine`, `AdoptionMachine`, `InterruptPhase`, `ChatEndpoint` |

---

## Context

### The PetAdoptionDemo as reference scenario

The `PetAdoptionDemo` is the canonical example of Ananke's human-in-the-loop
implementation. Its interrupt flow is the concrete starting point for this ADR.

The current flow has two HTTP surfaces and one SSE stream:

```
Client                    Server
  │                          │
  ├─ POST /api/chat ─────────►  FireAsync(Start) → OnEnter(Searching)
  │                          │    StreamingChatWorkflow runs
  │◄── SSE: delta ───────────┤    agent streams text
  │◄── SSE: delta ───────────┤    agent calls search tool
  │◄── SSE: delta ───────────┤    agent calls start_adoption("Ziggy")
  │                          │      └─ FireAsync(StartPaperwork) → OnEnter(Paperwork)
  │◄── SSE: done ────────────┤
```

The interrupt path (introduced by ADR-004) adds a second HTTP surface:

```
Client                    Server
  │                          │
  │  [agent is streaming]    │
  ├─ POST /api/interrupt ────►  FireAsync(Interrupt, newMessage)
  │                          │    OnInterrupt: capture message
  │◄── SSE: "interrupted" ───┤    → enter Interrupted state
  │                          │    OnEnter(Interrupted): patch history,
  │◄── SSE: "resumed" ───────┤      add message, FireAsync(Resume)
  │◄── SSE: delta ───────────┤    → back to Searching, workflow restarts
```

### The asymmetry

Both paths are **user-initiated**:

| Direction | Trigger | Mechanism |
|---|---|---|
| User sends a message | `POST /api/chat` | `FireAsync(Start)` → `OnEnter` runs workflow |
| User interrupts mid-stream | `POST /api/interrupt` | `FireAsync(Interrupt, payload)` → interrupt stack |

There is no equivalent for the **agent-initiated** direction:

| Direction | Trigger | Mechanism | Status |
|---|---|---|---|
| Agent needs user confirmation before a tool | — | — | **Does not exist** |
| Agent surfaces a question mid-generation | — | — | **Does not exist** |

The agent can **stream text** to the user but cannot **await a reply** and
continue. When `start_adoption("Ziggy")` fires, the adoption is already
committed — there is no framework-level mechanism for the agent to first ask
"Adoption fee is $150. Shall I proceed?" and then conditionally continue or
abort based on the answer.

This is the **agent-pull** direction: the agent pauses, surfaces a question
to the user over SSE, and the user's response unblocks the agent's
in-progress execution.

---

## Problem Statement

A conversational agent often needs to **seek confirmation** before committing
to an irreversible or high-cost action. In the PetAdoptionDemo, three natural
confirmation points exist today with no framework support:

| Point | Current behaviour | Desired behaviour |
|---|---|---|
| `start_adoption` fires | Immediately commits; Paperwork phase starts | Agent asks "Shall I start the adoption for Ziggy ($150 fee)?" and waits |
| `submit_application` fires | Immediately submits | Agent recaps the application details and asks "Ready to submit?" |
| Payment phase starts | Charges the card | Agent confirms the amount and card last-4 before charging |

Without agent-pull, the developer must work around this by:
- Splitting each step into two separate chat turns (clunky UX)
- Embedding the confirmation in the system prompt and hoping the LLM always asks
  (fragile — not enforced by the framework)
- Pre-emptively intercepting tool calls via a bespoke middleware (ad-hoc, not reusable)

---

## Two Fundamental Interrupt Directions

Naming these clearly keeps the design distinct from ADR-017:

| Direction | Who initiates | Name | Existing support |
|---|---|---|---|
| **User-push** | User fires a new message into an executing session | `Interrupt` (ADR-004) | ✅ Full — `FireAsync(Interrupt, payload)` + interrupt stack |
| **Agent-pull** | Agent pauses mid-execution and awaits a reply | `ConfirmationRequest` | ❌ None |

ADR-017 addresses a different dimension: *whether* the framework should
automatically interrupt at job or tool boundaries. This ADR addresses who
initiates the pause. The two complement each other:

- ADR-017's `SessionGrantPolicy` can suppress an agent-pull request when the
  user has already granted permission ("just do it").
- This ADR's agent-pull mechanism is what fires when no grant is in scope.

Together they form a complete picture:

```
No grant + high risk  → agent-pull fires  → user answers  → agent continues
Grant present         → agent-pull skipped → agent auto-proceeds
```

---

## Design Direction

### Core primitive: `IAgentConfirmationGate`

The agent-pull direction needs exactly one new abstraction: a session-scoped
suspension handle that the agent (via a tool or framework hook) can call to
emit a question and await an answer.

```csharp
/// <summary>
/// Session-scoped gate that suspends agent execution, surfaces a confirmation
/// request to the user over the active SSE stream, and unblocks when the user
/// responds.
/// </summary>
public interface IAgentConfirmationGate
{
    /// <summary>
    /// Emits a confirmation request event to the client and suspends until
    /// <see cref="ReplyAsync"/> is called with the user's answer.
    /// </summary>
    /// <param name="question">The question to surface to the user.</param>
    /// <param name="options">
    /// Optional named choices (e.g. ["Yes", "No", "Change pet"]).
    /// When provided, the client may render these as buttons rather than a
    /// free-text input.
    /// </param>
    /// <returns>
    /// The user's reply — either the text of a selected option or free-form input.
    /// </returns>
    Task<string> RequestAsync(
        string question,
        IReadOnlyList<string>? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Delivers the user's response and unblocks the awaiting <see cref="RequestAsync"/>.
    /// Called by the confirmation endpoint when the user responds.
    /// </summary>
    Task ReplyAsync(string answer, CancellationToken ct = default);

    /// <summary>
    /// Whether a <see cref="RequestAsync"/> call is currently suspended and
    /// waiting for a reply.
    /// </summary>
    bool IsPendingReply { get; }
}
```

Implementation: a `TaskCompletionSource<string>` (or `Channel<string>`) held
in a session-scoped service. `RequestAsync` completes the source on reply.
`ReplyAsync` is called by a new `POST /api/confirm` endpoint.

### SSE contract

`RequestAsync` emits a new SSE event type before suspending:

```
event: confirm_request
data: { "question": "Adopt Ziggy for $150?", "options": ["Yes", "No"] }
```

The client renders this as a confirmation prompt. When the user responds, the
client posts to `/api/confirm`:

```
POST /api/confirm
{ "sessionId": "...", "answer": "Yes" }
```

The server calls `gate.ReplyAsync("Yes")`, the `RequestAsync` call returns
`"Yes"`, and the agent continues.

### Integration approach A: Confirmation tool (agent-driven)

The agent explicitly requests confirmation by calling a tool. No framework
changes are required beyond the gate itself.

```csharp
toolkit.AddTool(
    name: "confirm_with_user",
    description: "Ask the user for confirmation before an irreversible action. " +
                 "Returns the user's exact reply.",
    parameters: new { question = "string", options = "string[] (optional)" },
    execute: async (string question, string[]? options) =>
        await gate.RequestAsync(question, options, ct));
```

The agent calls `confirm_with_user` in its reasoning chain before calling
`start_adoption`, `submit_application`, or the payment tool. The framework
imposes nothing; the LLM decides when to ask.

**Fits the PetAdoptionDemo directly.** The system prompt for `SearchPhase`
already instructs the agent on tool selection — adding confirmation tool
guidance is natural.

Limitations:
- Depends on the LLM following the instruction to call the tool. Adversarial
  or misconfigured models may skip it.
- Not enforced for high-risk tools — a tool can fire without confirmation if
  the LLM chooses not to ask.

### Integration approach B: Pre-execution risk hook (framework-driven)

`IToolInterruptPolicy` from ADR-017 Tier 3 intercepts before each tool
invocation. When a `ToolRisk.Destructive` tool is about to execute, the
policy calls `gate.RequestAsync(...)` before allowing the tool to proceed.

This approach is **automatic and enforced** — it does not rely on the LLM
following a convention. The confirmation is mandatory for any tool marked
`Destructive`, regardless of what the LLM decided.

The agent can still call `confirm_with_user` for discretionary confirmations
(Approach A), and the policy provides the safety net for risk-annotated tools.
They compose cleanly:

```
Agent decides to confirm → calls confirm_with_user → gate suspends → user replies
Agent skips confirmation → framework intercepts start_adoption (Destructive) → gate suspends → user replies
User has said "just do it" → SessionGrantPolicy (ADR-017) → gate not called → tool executes
```

### Integration approach C: `AwaitingConfirmation` state (state-machine-driven)

Add a dedicated `AwaitingConfirmation` state to `AdoptionMachine`:

```
Searching ──[RequestConfirmation]──► AwaitingConfirmation ──[Confirm]──► Searching
                                                           ──[Reject]───► Searching
```

The `confirm_with_user` tool fires `FireAsync(RequestConfirmation, question)`
instead of calling the gate directly. `OnEnter(AwaitingConfirmation)` emits
the SSE event and the confirmation endpoint fires `Confirm` or `Reject`.

Advantages over A and B:
- The "awaiting confirmation" state is **explicit and visible** in the FSM —
  observable via `CurrentState`, auditable, resumable after a server restart
  (if the state is checkpointed).
- Confirmation history is part of the conversation state, not just in memory.

Trade-off: requires new states and transitions per workflow. Appropriate when
the confirmation point is a **named business step** (e.g., "confirm adoption
intent before paperwork begins"). Overkill for routine ad-hoc confirmations.

---

## Comparison of Approaches

| | Approach A (Tool) | Approach B (Policy Hook) | Approach C (FSM State) |
|---|---|---|---|
| Who decides when to ask | LLM | Framework (risk annotation) | LLM (via tool triggering state) |
| Enforced for high-risk tools | No | Yes | Only if tool always uses it |
| Framework changes required | None (gate + endpoint only) | ADR-017 Tier 3 | New states + transitions |
| State machine visibility | No | No | Yes |
| Resumable after restart | No | No | Yes (if checkpointed) |
| Implementation complexity | Low | Medium (after ADR-017) | Low-Medium |
| Best fit | Ad-hoc, agent-discretionary | Automatic safety net | Named, auditable business steps |

**Recommended path:** ship Approach A first (lowest friction, immediate value
in the demo), then layer Approach B once ADR-017 Tier 3 is in place, and
reserve Approach C for workflows where "awaiting confirmation" is a meaningful
named phase in the domain model.

---

## The Complete Bidirectional Picture

With all three approaches available, the conversation loop becomes fully
symmetric:

```
Client                       Server
  │                             │
  ├─ POST /api/chat ────────────►  agent starts streaming
  │◄── SSE: delta ──────────────┤  agent reasons...
  │                             │
  │   [agent calls confirm_with_user]
  │◄── SSE: confirm_request ────┤  gate suspends execution
  │                             │
  ├─ POST /api/confirm ─────────►  gate.ReplyAsync("Yes")
  │                             │  agent unblocks, continues reasoning
  │◄── SSE: delta ──────────────┤  agent calls start_adoption("Ziggy")
  │◄── SSE: done ───────────────┤
  │                             │
  │   [mid-stream, user changes mind]
  ├─ POST /api/interrupt ───────►  FireAsync(Interrupt, newMessage)
  │◄── SSE: interrupted / resumed ┤ interrupt stack + resume
  │◄── SSE: delta ──────────────┤
```

User-push (interrupt) and agent-pull (confirmation) coexist. They share the
same SSE channel but use distinct event types. The two endpoints —
`/api/interrupt` and `/api/confirm` — mirror each other structurally.

---

## Interaction with ADR-017 Safety Constraints

ADR-017 established that session grants must not cause **autonomy drift** — 
a grant for one action must not silently extend to others. The same constraint
applies here:

- `gate.RequestAsync` must be **re-evaluated on every call**, even within the
  same session. A "yes" to adopting Ziggy is not a standing approval for all
  future confirmations in that session.
- If `gate.ReplyAsync` is never called (client disconnects, timeout), the gate
  must unblock with a cancellation rather than silently auto-approving.
- A pending `RequestAsync` that receives a `User-push interrupt` from
  `/api/interrupt` should be cancelled and the interrupt allowed to proceed —
  the user's explicit new message takes priority over the pending confirmation.

---

## Open Questions

| Question | Notes |
|---|---|
| Timeout handling | If the user closes the browser while a `RequestAsync` is pending, the gate should cancel with `OperationCanceledException` so the tool returns an error to the agent. The agent can then decide to abort or assume rejection. |
| Multiple concurrent gates | Should a session allow only one pending `RequestAsync` at a time? Simplest answer: yes — the gate is session-scoped and rejects a second `RequestAsync` while one is already pending. |
| Conflict with user-push interrupt | If `POST /api/interrupt` fires while a `RequestAsync` is pending, the pending gate should be cancelled and the interrupt should proceed normally. This preserves ADR-004's semantics. |
| Audit trail | Should `confirm_request` / reply pairs be appended to the conversation history as synthetic messages? This would allow the LLM to see in subsequent turns that a confirmation was given (or denied). |
| SSE event naming | `confirm_request` and the matching `confirm_reply` (emitted after `ReplyAsync` to let the UI close the dialog) need to be defined in `ChatSessionEvent` or equivalent to avoid magic strings. |

---

## Decision

This ADR is **proposed**. No code changes are made here.

**Recommended first step:** implement `IAgentConfirmationGate` (gate + session
registration + `POST /api/confirm` endpoint) and wire it as a tool in the
`PetAdoptionDemo`'s `SearchPhase` as Approach A. This is a self-contained
change with no framework dependencies, demonstrates bidirectionality concretely,
and validates the SSE contract before committing to the policy-level
integration in ADR-017 Tier 3.
