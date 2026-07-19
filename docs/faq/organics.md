<!-- topic: faq-organics, tags: faq, organics, colony, division, scaling, self-organization, workflows -->
# FAQ — Organic Colony & Cell Division

← [Back to all FAQs](../faq.md)

---

## The Problem

### My agent workflow works fine. Why would I need this?

It works fine *now*. The problem is what happens next.

Every successful agent workflow follows the same trajectory: it starts simple
(3–4 tools, one domain), proves its value, and then grows. The business adds
tools for new capabilities. The agent accumulates empirical memory across
domains. Six months later the workflow has 12 tools, handles search, payments,
recommendations, and order tracking — and its error rate is climbing.

This is the **generalist ceiling**. LLMs degrade when their context is packed
with too many tool definitions. The agent picks `process_payment` when the user
is just browsing. It recommends books when the user is asking about order
status. The more tools, the more confusion.

The only fix today is for a human to manually redesign the workflow: split it
into multiple specialized workflows, set up routing between them, migrate
knowledge, redeploy. Then do it again when the next split is needed.

`Ananke.Organics` makes this automatic.

### What specific problem does cell division solve?

**Tool confusion in generalist agents.**

When an LLM has too many tools in its context window, three things happen:

1. **Selection accuracy drops** — the model increasingly picks the wrong tool
   for the task
2. **Context budget shrinks** — tool definitions consume tokens that could be
   used for reasoning and conversation
3. **Latency climbs** — more tools means more tokens per request, slower
   responses, higher cost

These are structural problems — they come from the workflow's *shape*, not from
bugs in the tools or the model. You can't fix them by improving individual tools
or switching to a better model. You fix them by **specializing**: giving each
agent fewer tools that are more relevant to its domain.

Division is the mechanism that does this automatically.

### When does division actually trigger?

Division triggers on **surface tension** — structural complexity metrics — not
on failure. This is a critical design choice.

A failing agent is a *dying* agent. It might not survive long enough to perform
the complex operation of dividing. Division must happen *proactively*, while the
agent is still healthy and capable:

| Signal | Meaning | Response |
|---|---|---|
| High surface tension, low failure | **Division window** — divide now | `IDivisionPolicy` triggers |
| High failure, high complexity | Too late — should have divided earlier | Lesson for memory |
| High failure, low complexity | Agent is sick, not complex | Fix tools/model, don't divide |
| High load, low complexity | Agent is overwhelmed but focused | Replicate, don't divide |

Surface tension is measured by `ComplexitySnapshot`:

| Metric | What it measures |
|---|---|
| **Tool count** | Raw number of tools bound to the agent |
| **Tag cluster count** | Distinct domain groups forming (e.g., "search tools" vs. "payment tools") |
| **Routing entropy** | How spread out the agent's decisions are across tools |
| **Resource span** | How many external backends the agent reaches through |
| **Context utilization** | Fraction of context window consumed by tool definitions alone |

---

## How It Works

### What happens when a workflow divides?

The parent **dies**. Two (or more) specialized peers emerge. There is no
parent-child hierarchy.

```
Before:
  bookstore-general (8 tools: search, details, cart, payment, inventory, tracking, recommend, coupon)

After:
  bookstore-browse  (3 tools: search, details, recommend)  ← alive
  bookstore-orders  (4 tools: cart, payment, tracking, coupon) ← alive
  bookstore-general ← DEAD
```

A triage router (infrastructure, not a workflow) senses both new cells through
their heartbeat signals and routes requests to the appropriate domain.

### What about knowledge? Does each child start from scratch?

No. Memory is **shared**, not partitioned. All cells read from and write to the
same `IEmpiricalMemory`. Each child gets a `DomainAffinityMemory` decorator
that biases recall toward its domain without excluding cross-domain knowledge.

Before dying, the parent also exports a **seed package** (RNA) for each child
via `ISkillPackager` — a curated subset of high-confidence, domain-relevant
knowledge. Children start warm, not cold.

The colony's own division knowledge (tagged `"division"`) is included in every
seed. Bad division strategies that were learned from are filtered out
automatically.

### What is the difference between division and replication?

| | Division | Replication |
|---|---|---|
| **Trigger** | Complexity (too many tools) | Demand (too much load) |
| **Original** | Dies | Lives |
| **Children** | Different tools, different domains | Same tools, same domain |
| **Purpose** | Specialization | Scaling |

Division is mitosis with differentiation. Replication is mitosis without
differentiation. Both use the same infrastructure (`IWorkflowHost`, `ISkillPackager`,
`ICapabilityMap`).

### Does the colony learn from its division decisions?

Yes. Division strategies are stored in `IEmpiricalMemory` like any other
knowledge. After division, `IDivisionOutcomeTracker` compares child metrics to
the parent baseline:

- Children perform better → strategy is **reinforced**
- Children perform worse → strategy is **contradicted**
- `IOfflineLearner` decays bad strategies over sleep cycles until they're pruned

On cold start (no experience), `ThresholdDivisionPolicy` uses simple heuristics:
tool count ≥ 6 AND tag clusters ≥ 2. On warm start, the policy recalls proven
strategies from memory and uses `IExplorationStrategy` (UCB) to balance
exploitation vs. exploration.

---

## Practical Concerns

### Is Ananke.Organics required? Does it change existing workflows?

No and no. `Ananke.Organics` is an **optional package**. Existing workflows,
tools, memory, and handoff channels are completely untouched. If you never
reference `Ananke.Organics`, nothing changes.

### What does the nervous system do?

Cells emit periodic **heartbeat signals** (`WorkflowSignal`) that carry their name,
domain, and capabilities. The `ICapabilityMap` aggregates these into a
live map. No signal for a configured duration = cell is dead.

This replaces the registry pattern (explicit Register/Unregister):
- A crashed cell stops signaling and fades from awareness — no stale entries
- A cell doesn't need cleanup code — it just stops being alive
- The model aligns with Docker health checks, K8s readiness probes

### How does this work in production? Is it just in-memory?

`IWorkflowHost` is hosting-agnostic. What "spawn" and "kill" mean depends on the
hosting model:

| Model | Spawn | Kill | Best for |
|---|---|---|---|
| `InProcessWorkflowHost` | `Task.Run` | Cancel token | Dev, demos, tests |
| Docker Compose | `docker run` | `docker stop` | Production |
| Kubernetes | Create CRD | Delete CRD | Production (large) |

The framework ships `InProcessWorkflowHost`. Production hosting adapters are external
implementations of `IWorkflowHost`.

### Can a colony grow indefinitely?

Division is self-regulating. The policy learns from negative outcomes — if a
division worsened metrics, that strategy is contradicted and eventually pruned.
Cold-start heuristics require **both** high tool count AND cluster separation
(surface tension, not just size), preventing premature splits.

If you deploy a single generalist agent with 3 tools, it will never divide.
There's nothing to specialize.

### What is the typical colony growth pattern?

```
Phase 1: Genesis        — single generalist cell, few tools, empty memory
Phase 2: Growth         — tools accumulate, tag clusters form, surface tension rises
Phase 3: Division       — policy triggers, parent dies, specialized peers emerge
Phase 4: Specialization — each cell excels at its domain, fewer errors, faster responses
Phase 5: Recursive      — if a specialist grows too complex, it divides again
```

Generation 0: `bookstore-general` (8 tools)
Generation 1: `bookstore-browse` (4) + `bookstore-orders` (4)
Generation 2: `bookstore-search` (2) + `bookstore-recommend` (2) + `bookstore-orders` (4)

No human designs this topology. It emerges from usage.

---

## Demos

### Where can I see this running?

Two runnable demos cover the organic lifecycle end-to-end:

**[OrganicKernelDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/04-organics-and-emergence/OrganicKernelDemo)**

The canonical full-lifecycle demo: a generalist bookstore agent accumulates tools, detects structural tension, proposes a division, passes through an approval gate, splits into two specialists, and records the outcome in empirical memory. No API keys required.

```bash
dotnet run                  # automatic approval
dotnet run -- --supervised  # pause for interactive approval
dotnet run -- --verbose     # show YAML snapshots
dotnet run -- --simulate    # dry-run: propose but don’t execute
```

**[LogEventsDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/04-organics-and-emergence/LogEventsDemo)**

Shows the `OfflineLearner` in a different domain: a simulated distributed system emits log events, `TagImportanceTracker` scores signal patterns, and the learner distils cascade-failure heuristics from the event stream. Useful for understanding how the learning layer works independently of the organic lifecycle. No API keys or Docker required.
