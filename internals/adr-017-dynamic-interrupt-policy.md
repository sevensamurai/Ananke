# ADR-017 — Dynamic Interrupt Policy: Context-Sensitive Human-in-the-Loop

| Field          | Value                                                                                                   |
|----------------|---------------------------------------------------------------------------------------------------------|
| **Status**     | Proposed                                                                                                |
| **Date**       | 2025-07-29                                                                                              |
| **Authors**    | —                                                                                                       |
| **Deciders**   | Ananke maintainers                                                                                      |
| **Tags**       | human-in-the-loop, interrupt, checkpoint, tool-calls, conversational-fluency, policy, intent-inference |
| **Relates to** | ADR-004 (interrupt propagation), ADR-016 (agentic harness patterns), `InterruptMode`, `JobDescriptor`, `WorkflowRunner`, `AgentJob`, `IJobMiddleware`, `GuardrailAgentModelMiddleware` |

---

## Context

### The current model

Ananke expresses human-in-the-loop as a **static, binary declaration** on a
workflow job:

```csharp
.InterruptBefore("SendEmail")
.InterruptAfter("GenerateReport")
```

`JobDescriptor.Interrupt` holds an `InterruptMode?`. Inside `WorkflowRunner`,
the interrupt check is unconditional:

```csharp
if (descriptor.Interrupt == InterruptMode.Before && !skipFirstInterrupt)
{
    execution.Status = ExecutionStatus.Interrupted;
    await _checkpointStore.SaveAsync(interruptCheckpoint, ct);
    return execution;
}
```

The checkpoint is always written. Execution always pauses. The decision is
made at **workflow definition time**, not **runtime**.

### The friction this creates

This model is correct for high-stakes, well-defined approval gates (e.g.,
"confirm before sending the email to 10,000 users"). For conversational
agents — where the loop between user and agent is tight and informal — blanket
checkpoints create friction that degrades the experience:

| Scenario | Problem |
|---|---|
| User says "go ahead and run all the tests, don't ask me again" | Agent still pauses at every `InterruptBefore("RunTests")` checkpoint |
| Agent is executing a sequence of low-risk read-only tool calls | Each tool invocation could theoretically gate on approval — every pause breaks flow |
| User has already approved a category of action in this session | The framework has no concept of a **session-scoped grant** — the agent asks again anyway |
| Destructive tool buried in a toolkit alongside safe tools | The current model applies interrupts at the **job level**, not the **tool level** — no granularity |

ADR-016 identified the "YOLO" implied-intent classifier pattern (a lightweight
model that decides per-tool-call whether to auto-approve) as the most novel
concept in its source analysis, and flagged it as warranting a dedicated ADR.

This is that ADR.

---

## Two Distinct Interrupt Surfaces

Before designing a solution, it is important to recognise that Ananke's current
interrupt model conflates two fundamentally different questions:

| Surface | Question | Granularity | Current mechanism |
|---|---|---|---|
| **Job-level** | Should execution pause *between* workflow jobs? | Coarse — one checkpoint per job boundary | `InterruptMode.Before` / `After` on `JobDescriptor` |
| **Tool-level** | Should execution pause when the agent *calls a specific tool*? | Fine — one decision per tool invocation inside a running job | None |

The described YOLO classifier operates at the **tool level** — it intercepts
individual tool calls mid-job and decides whether to auto-approve or escalate.
This is different from pausing between workflow stages. Both surfaces matter and
both are improved by a dynamic policy, but they require different integration
points.

---

## Problem Statement

A practical conversational agent needs:

1. **Session-scoped grants** — when the user explicitly expresses intent to
   proceed autonomously ("just do it"), that grant should propagate forward
   within the session without requiring re-declaration at every checkpoint.

2. **Tool-level risk classification** — a toolkit contains tools of varying
   risk. Read-only queries, reversible writes, and irreversible/external-side-effect
   operations should be treated differently, with the interrupt decision
   proportional to risk.

3. **Runtime policy evaluation** — the interrupt decision should be a
   *computation* that can inspect the conversation transcript, the current
   workflow state, any in-session grants, and the identity of the tool being
   called. The decision is not fixed at definition time.

4. **Non-accumulating autonomy** — auto-approvals granted for low-risk
   operations must not cascade into auto-approval of high-risk operations.
   The policy must be **asymmetric**: it can be freely permissive on safe
   operations and conservative on dangerous ones regardless of prior
   approvals in the session.

---

## Design Direction

### Core abstraction: `IInterruptPolicy<TState>`

The interrupt decision becomes a pluggable policy evaluated at runtime:

```csharp
/// <summary>
/// Determines at runtime whether a checkpoint should pause execution and
/// await human approval, or whether implied intent allows autonomous continuation.
/// </summary>
public interface IInterruptPolicy<TState>
{
    /// <summary>
    /// Evaluates whether execution should interrupt at this point.
    /// </summary>
    /// <returns>
    /// <see cref="InterruptDecision.Interrupt"/> to pause and checkpoint;
    /// <see cref="InterruptDecision.AutoApprove"/> to continue without pausing.
    /// </returns>
    Task<InterruptDecision> EvaluateAsync(InterruptContext<TState> context, CancellationToken ct = default);
}

public enum InterruptDecision { Interrupt, AutoApprove }

public sealed record InterruptContext<TState>
{
    /// <summary>The name of the job at which the interrupt was declared.</summary>
    public required string JobName { get; init; }

    /// <summary>The mode that triggered evaluation (Before or After).</summary>
    public required InterruptMode Mode { get; init; }

    /// <summary>Current workflow state at the point of evaluation.</summary>
    public required TState State { get; init; }

    /// <summary>
    /// Conversation messages available at evaluation time.
    /// Populated when an <see cref="IConversationMemory"/> is wired to the runner.
    /// </summary>
    public IReadOnlyList<AgentMessage> ConversationHistory { get; init; } = [];

    /// <summary>
    /// Permission grants accumulated during this session
    /// (e.g. from a prior <see cref="SessionGrantPolicy{TState}"/> evaluation).
    /// </summary>
    public IReadOnlyList<string> SessionGrants { get; init; } = [];

    /// <summary>Metadata carried by the current workflow execution.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}
```

`JobDescriptor` gains an optional policy alongside the existing `Interrupt`
field. When a policy is present, the runner calls it instead of
unconditionally interrupting:

```csharp
public record JobDescriptor<TState>
{
    // ... existing fields ...
    public InterruptMode? Interrupt { get; init; }

    /// <summary>
    /// When non-null, replaces the unconditional interrupt with a runtime
    /// policy evaluation. <see cref="Interrupt"/> must still be set to
    /// declare the interrupt surface; the policy then decides whether to act.
    /// </summary>
    public IInterruptPolicy<TState>? InterruptPolicy { get; init; }
}
```

The runner check becomes:

```csharp
if (descriptor.Interrupt == InterruptMode.Before && !skipFirstInterrupt)
{
    var shouldInterrupt = descriptor.InterruptPolicy is null
        || await descriptor.InterruptPolicy.EvaluateAsync(context, ct)
              == InterruptDecision.Interrupt;

    if (shouldInterrupt)
    {
        // ... existing checkpoint + return ...
    }
}
```

The change is **backwards-compatible**: no policy → unconditional interrupt
(current behaviour). A policy is opt-in.

---

### Tool-level surface: `IToolInterruptPolicy`

For tool-call gating inside `AgentJob`, a parallel interface operates at the
tool invocation level:

```csharp
/// <summary>
/// Evaluates per tool call whether execution should surface the proposed
/// invocation to the user before running it.
/// </summary>
public interface IToolInterruptPolicy
{
    /// <summary>
    /// Called before each tool invocation in the agent's tool-call loop.
    /// Return <see cref="InterruptDecision.Interrupt"/> to pause and await
    /// human confirmation; <see cref="InterruptDecision.AutoApprove"/> to proceed.
    /// </summary>
    Task<InterruptDecision> EvaluateAsync(ToolInterruptContext context, CancellationToken ct = default);
}

public sealed record ToolInterruptContext
{
    /// <summary>The name of the tool the agent has requested.</summary>
    public required string ToolName { get; init; }

    /// <summary>The raw argument payload from the model's tool call.</summary>
    public required string ArgumentsJson { get; init; }

    /// <summary>Risk level annotated on the tool's definition.</summary>
    public required ToolRisk Risk { get; init; }

    /// <summary>Conversation history at the point of this tool call.</summary>
    public IReadOnlyList<AgentMessage> ConversationHistory { get; init; } = [];

    /// <summary>Session grants from prior approvals.</summary>
    public IReadOnlyList<string> SessionGrants { get; init; } = [];
}

/// <summary>
/// Annotates the risk level of a tool for interrupt-policy consumers.
/// </summary>
public enum ToolRisk
{
    /// <summary>Read-only, idempotent, no external side effects.</summary>
    Safe,

    /// <summary>Writes local state but the effect is reversible.</summary>
    Reversible,

    /// <summary>
    /// Writes external state, incurs cost, or is not reversible.
    /// Requires explicit human approval unless a grant is in scope.
    /// </summary>
    Destructive
}
```

`ToolRisk` is added as an optional field on `ToolDefinition` or `AgentTool`
(the descriptor, not the executor). The tool-interrupt policy is registered
on `AgentJob` and consulted in the tool-call loop before each execution.

---

### Built-in policy implementations

Four default policies cover the common cases without requiring a custom
implementation:

| Policy | Behaviour | Use when |
|---|---|---|
| `AlwaysInterruptPolicy` | Always returns `Interrupt` | Current behaviour. Safe default when no policy is configured. |
| `NeverInterruptPolicy` | Always returns `AutoApprove` | Full autonomy. Appropriate for automated pipelines with no human in the loop. |
| `SessionGrantPolicy` | Scans `ConversationHistory` for grant phrases ("go ahead", "proceed without asking", "just do it") and returns `AutoApprove` if found | Interactive sessions where the user has explicitly granted permission |
| `ToolRiskPolicy` | Returns `AutoApprove` for `ToolRisk.Safe` and `ToolRisk.Reversible`; `Interrupt` for `ToolRisk.Destructive` | Toolkit-level risk gating without conversational analysis |

Policies compose naturally via `CompositeInterruptPolicy`, which runs a list
of policies in order and takes the most conservative result:

```csharp
var policy = new CompositeInterruptPolicy<MyState>(
    new SessionGrantPolicy<MyState>(...),
    new ToolRiskPolicy<MyState>()
);
```

This means: auto-approve if the user granted permission **and** the tool is
not destructive. Either condition failing → interrupt.

---

### Relationship to `GuardrailAgentModelMiddleware`

`GuardrailAgentModelMiddleware` already gates on **model outputs** after a
response is generated. The tool interrupt policy gates on **model decisions**
before a tool is executed. They are complementary, not overlapping:

```
User input → [Agent reasoning] → [ToolInterruptPolicy] → [Tool execution] → [Guardrail] → Response
```

The interrupt policy should not be implemented as a guardrail variant — the
two hooks have different semantic contracts and different failure modes.

---

## Safety Constraints

These constraints must be non-negotiable in any implementation:

### No autonomy drift

Session grants must be **scoped** and **explicit**. They must not
generalise:

- A grant for `ToolRisk.Safe` tools does not imply a grant for `ToolRisk.Destructive`.
- A grant issued in an earlier conversation turn does not grow in scope over
  the session. The policy re-evaluates on every call with the same rules.
- `CompositeInterruptPolicy` is **pessimistic**: the most conservative member
  wins. There is no "any member says AutoApprove → approve" composition mode.

### Grant extraction is read-only

Session grants are extracted from `ConversationHistory` — they are not
written back by the policy. The policy has no side effects on the
conversation. It observes; it does not modify. This prevents the analogue of
ADR-009's Recall → Reinforce loop applied to the interrupt layer: auto-approvals
do not make future calls easier to auto-approve.

### Checkpointing on doubt

When a policy implementation is uncertain — or when it encounters an error — the
default fallback must be `InterruptDecision.Interrupt`. Policies should be
written with an explicit `try/catch` that returns `Interrupt` on failure.
The framework should enforce this by wrapping policy invocations in a
`SafePolicyWrapper` that catches unhandled exceptions and returns `Interrupt`
with a log entry.

---

## Implementation Tiers

The full design above can be shipped in progressive tiers without breaking
changes between them:

| Tier | Scope | API surface | Value delivered |
|---|---|---|---|
| **0 — Baseline** | No change | `InterruptMode` enum, `JobDescriptor.Interrupt` | Current behaviour preserved |
| **1 — Policy slot** | `IInterruptPolicy<TState>` on `JobDescriptor`; `AlwaysInterruptPolicy` and `NeverInterruptPolicy`; `SafePolicyWrapper` | New interface + two trivial implementations | Enables opt-in dynamic behaviour without changing runner logic significantly |
| **2 — Session grants** | `SessionGrantPolicy<TState>` using `IConversationMemory`; phrase list configurable | One non-trivial implementation | Makes interactive sessions conversationally fluent — users can grant permission in natural language |
| **3 — Tool risk** | `ToolRisk` on `ToolDefinition`; `ToolRiskPolicy`; `IToolInterruptPolicy` on `AgentJob`; tool-loop integration | Extends two existing types + new hook in `AgentJob` | Granular tool-level gating — replaces job-level bluntness |
| **4 — LLM classifier** | An `IInterruptPolicy` backed by a fast `IAgentModel` call over the conversation transcript | New implementation only | True YOLO semantics — implied intent inferred by the model |

Tiers 1 and 2 are low-risk and high-value. Tier 3 requires care in `AgentJob`'s
tool-call loop. Tier 4 is optional and should not be pursued until Tiers 1–3 are
stable — the classifier adds latency to every tool call and the simple phrase-
matching of Tier 2 will cover the vast majority of real cases.

---

## Open Questions

| Question | Notes |
|---|---|
| Where does `IConversationMemory` get injected into `InterruptContext`? | `WorkflowRunner` does not currently hold a memory reference. It could be passed via `WorkflowDefinition` metadata or as a new constructor dependency. |
| Should `ToolRisk` live on `ToolDefinition` or on `AgentTool`? | `AgentTool` is the LLM-facing descriptor; `ToolDefinition` is the execution descriptor. Risk is a property of the execution concern, suggesting `ToolDefinition`. |
| Should tool-level interrupt produce a checkpoint? | Tool-level interrupts inside a running job require a different persistence contract than job-level checkpoints — the in-progress agent reasoning state is not captured by `Checkpoint<TState>`. This may need a separate `ToolCallCheckpoint` concept, or the tool interrupt may simply block (not persist) and resume inline. |
| Should `CompositeInterruptPolicy` support `Any` (optimistic) composition? | Currently proposed as pessimistic-only. An optimistic variant ("any member AutoApproves → approve") has legitimate uses (e.g., whitelist-OR-grant) but risks being misused as a permission escalation path. Held for later. |

---

## Decision

This ADR is **proposed**. No code changes are made here.

The design is directionally sound and does not require breaking changes to
existing interfaces. The progressive tier structure means Tier 1 can be
implemented independently and deliver immediate value without committing to
the full surface area.

**Recommended first step:** Tier 1 only — add `IInterruptPolicy<TState>?` to
`JobDescriptor`, implement `AlwaysInterruptPolicy` / `NeverInterruptPolicy` /
`SafePolicyWrapper`, and wire the conditional check into `WorkflowRunner`. This
is a small, safe change that unlocks the rest of the design without requiring
any decisions on the open questions above.
