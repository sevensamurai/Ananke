<!-- topic: plans-and-contracts, tags: plan, contract, verification, supervision, long-running, decomposition, verification -->
# 18 — Plans and Contracts

Long-running work does not fit in one context window, and it does not fit in one job either.
`AgentContract` and `PlanTree` are how work that spans many steps keeps its goal, its constraints and
its evidence — without any of it being handed from step to step in a message.

**Demo:** [ItineraryDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/02-workflow-patterns/ItineraryDemo) — a week planned around one place that cannot be had as asked, run against a live model, where the criteria are invocations a program decides and the Planner rewrites the plan around what the world will actually allow.

---

## The problem this solves

A conversation has one window and one lifetime. A piece of work that takes a week has three, and they
expire at different rates:

| Lives for | What it is | Where it goes |
|---|---|---|
| One step | The messages of the current attempt | The agent's own context, compacted as usual |
| The whole work item | The goal, what counts as done, what must hold throughout | `AgentContract`, re-rendered into every call |
| Longer than any work item | What has been decided and verified across the plan | `PlanTree`, read from a store |

The failure mode without this split is familiar: the goal was stated in turn one, compaction evicted
it by turn forty, and the agent is now optimising something adjacent to the thing you asked for. The
second row is the fix for that. The third row is the fix for the same problem *between* steps — a
result passed from one step to the next is a handoff, and a handoff that is lost is not recoverable.

---

## The contract

`AgentContract` is what a work item was given. It is authored **above** the work — by whoever handed
it down — and rendered into every assembly the job makes, so no context strategy can evict it.

```csharp
var contract = new AgentContract
{
    Goal = "Ship offline export of reports",
    AcceptanceCriteria =
    [
        "the export runs without the reporting service",
        "the chosen approach handles the largest tenant's report"
    ],
    Constraints = ["no new external service dependencies"],
    QualityCriteria = ["the output is byte-identical to the online exporter"]
};
```

Four things about that shape are deliberate:

- **Acceptance criteria gate; quality criteria rank.** They are never averaged. A failed acceptance
  criterion cannot be bought back by a strong showing on quality, which is exactly what any weighted
  blend of the two would allow. A score is computed *only* once every gate has passed.
- **Criteria are a list, not a paragraph.** They are checked separately, so they are stated separately.
- **Constraints are stated once and never restated**, which is precisely why they have to survive
  compaction.
- **A contract says what, never how many goes.** There is no attempt budget on it, deliberately:
  re-running a step that produced nothing is **mechanical** — the loop re-issues the attempt, bounded
  by `PlanRetryPolicy`, and nothing about the count reaches the plan. It is not a judgement and so it
  is not a decision: a supervisor that could re-drive a step would be steering execution without the
  planner ever hearing about it. A number declared when the plan was written could only ever be a
  guess about an outage that had not happened yet.

A contract is not a system prompt. A system prompt says who the agent is and is written by whoever
built the agent; a contract says what this particular work item is for. Keeping them apart is what
lets you hand down a goal without rewriting a persona.

You can pin one onto any agent job, with or without a plan:

```csharp
var job = AgentJobFactory.Create<MyState, MyResult>("deliver", model)
    .WithSystemPrompt("You are a senior engineer.")
    .WithContract(contract)
    .Build();
```

The engine composes the contract into the assembly once, at construction — the job does not have to
remember to restate it, and no compaction pass gets a say.

Contracts are opt-in. A job without one behaves exactly as it does today.

---

## The plan tree

`PlanTree` is a decomposition plus every version it has been through. `PlanNode` is one work item in
it: its contract, its children, and the verdicts recorded against its criteria.

```csharp
var tree = PlanTree.Create(
    planId: "offline-export",
    rootContract: contract,
    children:
    [
        new PlanTree.PlanNodeSpec("discovery", discoveryContract,
        [
            new PlanTree.PlanNodeSpec("survey-pipeline", surveyContract),
            new PlanTree.PlanNodeSpec("choose-format", formatContract)
        ]),
        new PlanTree.PlanNodeSpec("delivery", deliveryContract, [/* ... */])
    ]);
```

### A node has no status field

This is the single most load-bearing decision in the design, so it is worth stating plainly: marking
something "complete" or "cancelled" records the outcome and **destroys the reason**. What a node did
is derived instead — and on **two axes**, because *what happened when we tried this* and *does the
contract hold* are different questions:

```csharp
tree.LifecycleOf("delivery"); // Planned | Running | Completed | Faulted | Abandoned — the attempt
tree.OutcomeOf("delivery");   // Unmet | Met | Disputed — what the verdicts say about the contract
tree.Settled("delivery");     // met, and its last attempt did not die

tree.StaleNodes();           // ran against a plan version that is no longer in force
tree.IsStale("build-endpoint");
```

They come apart in both directions, which is why they are two: a node can be `Completed` and `Unmet`
— it ran and satisfied nothing — or `Faulted` with criteria that already held.

Every one of those is computed from the structure on demand, with a single exception:
`PlanNode.AttemptStartedAt` is stored, because *in flight since T* cannot be reconstructed from
verdicts a running node has not produced yet. It is not a status flag — it records no outcome and
destroys no reason — and it is cleared when a pass begins, so a run that died mid-step reads
`Planned` again rather than `Running` forever. A stored answer to a question the structure *can*
answer is a second source of truth, and it is wrong from the moment anything re-rules.

### Changing the plan mints a version

When the plan itself turns out to be wrong, that is not a flag flipping — it is a new `PlanVersion`
with a stated reason:

```csharp
var v2 = tree.Rerule(
    nodeId: "delivery",
    contract: deliveryContract with { Goal = "Stream the export rather than buffering it" },
    reason: "the largest tenant's report is 4 GB against 2 GB of host memory",
    children: [("build-renderer-reuse", rendererContract), ("build-endpoint", endpointContract)]);
```

Supplying `children` replaces the decomposition. Nodes not listed are **dropped**, not cancelled: they
are gone from version 2, recorded in `PlanVersion.DroppedNodeIds`, and still fully readable in version
1 — contract, goal and all. That is what makes *"how much finished work did this discovery
invalidate"* answerable at all; it is not recoverable from status flags after the fact.

Unchanged subtrees are shared by reference rather than copied, and verdicts are only cleared under a
contract that actually changed — a node whose own contract is identical was not re-issued anything
different, and its verdict was a fact about criteria that still stand.

---

## Running a plan

A tree lives in an `IPlanTreeStore`. `PlanExecutor` walks it.

```csharp
IPlanTreeStore store = new FilePlanTreeStore("./plans");   // or InMemoryPlanTreeStore
await store.SaveAsync(tree);

var executor = new PlanExecutor(store, projectionTokenBudget: 400, verifier: verifier);

var result = await executor.ExecuteAsync("offline-export", runner);
```

`ExecuteAsync` runs **one pass**: children in execution order, then the node that decomposed them.
Post-order is the point — no leaf-level check proves that a commitment made higher up still holds, so
the evidence has to climb back to the level that authored the criterion.

Three more properties worth knowing before you use it:

- **Sequential, one node at a time.** What is contended between two nodes working the same area is
  the artifact, not the context window, so no scheme for sharing capacity helps.
- **The tree is reloaded before every node and saved after every one.** That is what makes the read a
  real read, and what makes a half-finished run resumable — a rerun skips whatever the tree already
  records as satisfied.
- **A reported contradiction stops the pass and is returned, not resolved.** `PlanRunResult.HaltedAt`
  says where.

`PlanRunResult` carries only what the tree cannot answer — `Executed`, `Skipped`, `Rulings`, and
`HaltedAt`. What the contracts say comes from `RootOutcome`, and how the run ended from `Outcome`;
both ask the tree.

### Repeating passes

To run a plan out, use `ExecuteToCompletionAsync`:

```csharp
var result = await executor.ExecuteToCompletionAsync(planId, runner);
```

Or let a workflow do it — see [Running a plan as a job](#running-a-plan-as-a-job) below, which is
the shape most consumers want.

It repeats passes while the tree is still changing and stops when a pass decides nothing new. **That
is not a retry ceiling** — there is no count to tune. A pass that leaves every status and every
standing verdict where it found them has made no progress, and a further pass has nothing new to work
from; that *is* non-convergence. Returning to any state the run has already been in ends it too, which
is what catches work that oscillates — a fix that breaks what the last fix repaired — and would
otherwise never leave two consecutive passes alike.

A dispute halts the pass and is returned unresolved rather than retried, and so does a step that
threw. A step that merely completed without satisfying its contract does neither: the pass carries
on, the root reads unmet, and what decides whether to go round again is the coordinator.

Read the outcome the same way you read a single pass — `Outcome` says how it ended in one word
(`Completed`, `Blocked`, `Abandoned`, `Faulted`) and `HaltedAt` names the node that stopped it.
**There is no halt *kind*.** There was one, and it was retired: the tree already holds the facts a
kind was summarising — a standing `PlanViolation`, a `NodeFailure`, a failing verdict with its own
`Basis` — and at most one of them is ever present for a node that just halted, so
`PlanCoordination.HaltReason` reads them in that order and needs no enum to choose between them. The
word is deliberately generic for the same reason: what makes one `Blocked` run different from another
is in the halt and the tree. **If telling two endings apart needs another enum value, it needs a
better record instead.**

`ExecuteAsync` stays public and stays a single pass. A caller that wants to do its own work between
passes — re-rule a node, ask a person, run a build — needs them separated, and that is a real case
rather than a lower-level detail.

---

## What runs a node

`PlanNodeRunner` is a delegate. Anything can implement it — an agent job, a shell check, a person:

```csharp
public delegate Task<NodeOutcome> PlanNodeRunner(PlanNodeContext context, CancellationToken ct);
```

`PlanNodeContext` is everything the node is given, and all of it came out of the tree: the `PlanNode`,
its pinned `AgentContract`, and `TreeView` — the tree rendered to fit the node's allocation. There is
deliberately no property holding a sibling's result or a message history. If one were added, the
design would quietly become a handoff again.

To run a node as an agent, use `PlanNodeAgentRunner`:

```csharp
var runner = new PlanNodeAgentRunner(new PlanNodeAgentOptions
{
    Model = model,
    Persona = "You are a senior engineer delivering one work item.",
    Oracle = "self-reported",
    OnReport = (context, report) => Console.WriteLine($"{context.Node.Id}: {report.Summary}")
});

var result = await executor.ExecuteAsync(planId, runner.AsRunner());
```

The node answers in a `PlanNodeReport`: a `Summary`, and an `Operation` — the change it proposes
making to the world.

**It reports no verdict, no dispute and no question**, because a step is the one seat with no standing
to classify its own outcome. Whether a criterion holds is decided by something that did not do the
work. Measured, that mattered: asked to say which kind of halt it was having, a model was right
**14/24** on *somebody must choose* and **0/24** on *somebody outside must act*.

**And it does not perform the operation either.** `Operation` is a candidate: the loop shape-checks it
— form only, never whether it will work — hands it to whatever the supervision configured as its
`Applier`, and only then runs the checks. So the world's answer arrives at the loop as a value rather
than as prose for a model to read and classify.

Note also what the runner does *not* let you do: `Configure` runs first and the contract and prompt
are applied last, so a caller cannot unpin the contract by supplying their own prompt.

To run a plan without a provider, hand it `SimulatedAgentModel` with a script keyed on each node's
goal — matched against the system prompt, which is where the contract is pinned. See
[14 — Testing](14-testing.md); the demo below runs its whole scenario that way.

---

## Declaring a plan as data

A plan can be written down instead of built in code. `PlanManifest` is the plan tier's equivalent of
`WorkflowManifest`, in the same format family:

```yaml
plan: offline-export

root:
  goal: Ship offline export of reports.
  criteria:
    - An exported file is byte-identical to the online report.
    - Export completes for the largest tenant.
  quality:
    - The export path reuses the existing report renderer.
  constraints:
    - No new external service dependencies.
  children:
    - id: discovery
      goal: Understand what already exists before designing anything.
      criteria:
        - Every existing render path is accounted for.
      children:
        - id: survey-pipeline
          goal: Map the current report pipeline end to end.

revisions:
  - node: delivery
    reason: >
      The largest tenant's report is 4 GB. Assembling it in one buffer cannot meet the root's
      criterion at any buffer size, so buffering is not the work any more.
    goal: Build offline export by streaming.
    criteria:
      - A report larger than available memory exports successfully.
    children:
      - id: build-streaming-writer
        goal: Stream rendered pages straight to storage.
```

```csharp
var manifest = PlanManifest.Load("plan.yml");

var asWritten = manifest.ToTree();          // version one
var asItStands = manifest.ToCurrentTree();  // every revision applied, lineage and all
```

**`revisions:` is the half that matters.** A format that could state only the original decomposition
would describe a plan's first version — and the interesting thing about a plan is what changed and
why. Each revision names the node, the contract that replaced its own, the reason, and the children
that replace its decomposition. Children not listed are **dropped**, and stay readable in every
earlier version.

Applying one at the moment a run justifies it is `ApplyTo`, or `PlanExecutor.ReruleAsync` if the tree
is already in a store:

```csharp
var reruled = manifest.Revisions[0].ApplyTo(tree);
```

Four things the format deliberately cannot do: **no conditions, no expressions, no variables, no
includes.** A plan format that grows control flow has stopped describing work and become a program
that computes a description — at which point the document nobody could read is back, in a worse
notation. In the same spirit, **an unknown key is an error**: a misspelled `critera:` that parsed to
nothing would produce a node with no acceptance criteria, which reads exactly like a node that
legitimately has none, and would be discovered when the plan passed without checking anything.

---

## Running a plan as a job

A plan is a unit of work inside a workflow, not a second way to run one. `Supervise` registers it the
same way `SubFlow` registers a nested workflow — a job, a couple of mapping functions, the ordinary
`Job(name, job)` path underneath:

```csharp
var supervision = new SupervisionOptions
{
    Runner = new PlanNodeAgentRunner(new PlanNodeAgentOptions { Model = model }).AsRunner(),
    Verifier = verifier,
    Store = new FilePlanTreeStore("./plans"),
    ProjectionTokenBudget = 400
};

var workflow = new Workflow<DeliveryState>("feature-delivery")
    .Supervise("deliver", PlanManifest.Load("plan.yml").ToTree(), supervision,
               (s, r) => s with { Outcome = r })
    .Then("deliver", Workflow.End);
```

**The tree is the supervised job's, not the state's.** It seeds the store once and every pass after
that reads the store, so a copy kept in `TState` is only ever the staler of the two. The other
overload — `Supervise(name, s => s.Plan, …)` — exists for the one case where the state genuinely
carries it: a plan an *earlier job produced*.

```csharp
var workflow = new Workflow<DeliveryState>("feature-delivery")
    .Job("decompose", planner)                       // writes s.Plan
    .Supervise("deliver", s => s.Plan!, supervision, (s, r) => s with { Outcome = r })
    .Chain("decompose", "deliver")
    .Then("deliver", Workflow.End);
```

That is the whole of it: no control loop, no store to wire by hand, no executor to configure at the
call site. The job runs the plan to completion — the stop condition from
[Repeating passes](#repeating-passes) — and hands `PlanRunResult` to `mapResult`.

Four things it will not do, each of them deliberate:

- **It does not resolve a dispute.** A node that reports its contract is wrong stops the run, and
  `HaltedAt` says where. Whether the plan should change belongs to whoever authored the criterion,
  which is a decision *above* this job — so it is a second job, and
  [`AgenticPattern.SupervisedPlan`](#closing-the-loop-a-plan-that-can-change) composes the two.

- **It does not widen how a plan executes.** Nodes still run one at a time. A workflow that forks two
  supervised jobs is doing what workflows do; each plan is still sequential within itself.
- **It does not overwrite a store that already holds the plan.** The tree in the store is further
  along than whatever it was handed — a run that died mid-plan, a checkpoint restored from before the
  last pass — so the plan given to `Supervise` *seeds* an empty store and is ignored by a full one.
  That is what makes a half-finished run resume rather than start again, and it is why two supervised
  jobs sharing a store and a plan id supervise one plan rather than two.
- **It is not a handoff.** When a plan *does* reach this job through workflow state — the planner
  case above — that is not the thing the tree replaces. What nodes may not pass between *themselves*
  is one rule; what a workflow passes between its own jobs is ordinary workflow state, and predates
  all of this.

---


## Closing the loop: a plan that can change

A plan that halts and is never answered is only half the tier. The other half is one builder:

```csharp
var workflow = AgenticPattern.SupervisedPlan<DeliveryState>("feature-delivery")
    .WithPlan(PlanManifest.Load("plan.yml").ToTree())
    .Supervised(supervision)
    .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
    .WithCoordinator(new AgentPlanSupervisor(supervision).AsCoordinator())
    .Build();
```

Two jobs and a loop: the plan runs, the coordinator decides what a halt means, and the work resumes
under the new version. That is the smallest shape, and the rest of this section is what you add to it
when a halt is somebody else's to answer — an advisor that *offers* rather than decides, a Planner
that re-authors when nothing could be offered, and a review of the finished work against the goal.

It adds **no execution concept** — the builder composes `Supervise`, `Job`, `Then` and `Loop`, and
returns an ordinary `Workflow<TState>` — which is what buys you the things a decision made privately
inside one job could never have:

| | |
|---|---|
| `InterruptBefore("coordinate")` | stops the run between assessment and resumption, for a person to look |
| `.EscalateToAPerson(when)` | stops **only** on the halts a person must answer — see [Guide 07](07-human-in-the-loop.md#escalating-a-supervised-plan) |
| `History` / `JobsExecuted` | a change of plan is a job that ran, not an invisible round trip |
| `ToDsl()` / topology export | the loop is drawn, because the edges are real |
| `WithCoordinator(IJob<TState>)` | a queue, a person or a sub-workflow in the same slot as a model |

### When the halt is not the plan's to answer

Some halts are nobody in the process's to decide. `EscalateToAPerson(when)` wires a conditional pause
before the coordinator job, so the run steers itself through the halts it can and stops on the ones
it cannot:

```csharp
    .MaxChangesOfPlan(3)
    .EscalateToAPerson(coordination => coordination.Node?.Failure is not null)
```

It is an ordinary workflow pause — checkpoint, `Interrupted`, `ResumeAsync(id, transform)` — so
everything [Guide 07](07-human-in-the-loop.md) says about human-in-the-loop applies unchanged, and a
host that can render one paused workflow can render this one. **Give it a durable checkpoint store if
the wait may outlast the process.** What somebody is being asked lives in workflow state, not in the
plan tree, so a run paused with `InMemoryCheckpointStore` behind it needs its process to stay alive
while they think; `UseCheckpointing(new FileCheckpointStore("./runs"))` is enough to make the pause
survive a restart. **An escalated decision is reported and never counted**: `MaxChangesOfPlan`, if you set one, bounds
the re-planning this loop does by itself, and a round it paused for is bounded by somebody being
there to answer. `PlanDecisionTaken` carries `Escalated` so a reader can tell the two apart without
inferring it.

### Bounding a coordinator that will not stop

`MaxChangesOfPlan(n)` puts a ceiling on how many times one run may change its plan. It is
**optional and unset by default**, because two bounds that are not arbitrary already apply:

| What runs away | What stops it |
|---|---|
| Spend | `BudgetConfig.MaxCost` — the currency that actually runs out, per run or per period |
| One step that produced nothing | `PlanRetryPolicy` — the loop re-issues the attempt, bounded, and spends no change of plan doing it |
| A coordinator that keeps re-ruling | your coordinator's own judgement — or this ceiling, if you do not trust it |

Set it when the third row worries you: a planner re-authoring the same contradiction pass after pass
is real observed behaviour. Leave it unset and the run ends when the plan settles, the coordinator
stops, or a round changes nothing — never on a number the framework picked for you.

### What a coordinator decides

It is handed a `PlanCoordination` — the last pass's result, the halted node, its dispute, its
verdicts, and how much of the change-of-plan budget is left — and returns **one of two things**:

| Decision | Means | Mints a version? | Spends a change of plan? |
|---|---|---|---|
| `PlanDecision.Replan(contract, rationale, children, nodeId)` | The plan was wrong. Here is the replacement | Yes | Yes |
| `PlanDecision.Ask(options)` | A person must act — whatever kind of choice put this beyond the seat | No | No |

**It used to be five, and the other three did not survive contact with what they were for.**
*Retrying* is not a judgement, so it belongs to the loop and not to a seat that thinks; *stopping* is
a person's cancel or the planner reporting that nothing can be planned; *referring a constraint* and
*giving a step up* are both **a person must act**, told apart by the question text rather than by the
vocabulary. Collapsing them closed the case a node used to get wrong near-unanimously, without
inventing anything: `Ask` carries the options and the question says which kind of asking it is.

Asking spends nothing because nothing was re-planned — no version is minted and no contract
re-authored — so there is nothing for a change-of-plan budget to count. `Replan` is the one that buys
another pass, and it is the one that pays for it.

`Replan` names the node it re-rules, which is **not necessarily the one that halted**: a
contradiction surfaces in a child while the contract that has to change is often the parent's,
because a node's input is the previous node's output. Re-ruling the halted node re-plans its subtree;
re-ruling its parent re-plans the remainder.

Which one a coordinator picks says what kind of coordinator it is. `DeterministicPlanSupervisor`
re-rules the node its table names — a rule written in advance knows which node the change belongs
to, because whoever wrote it has seen the whole plan. `AgentPlanSupervisor`, the seat ItineraryDemo
fills with a model, re-rules the node that halted, because a model reading one halt at a time has
only that node in front of it. Both are shipped, deliberately: steering a plan is not inherently a
judgement call, and a coordinator seat only a model could fill would exclude a consumer whose failure
modes are already known.

**There is no reason to supply, and that is the point.** Why the plan stopped being right is taken
from the halt — `PlanCoordination.HaltReason` — because the coordinator did not witness it:

| What the tree holds | The reason, and where it comes from |
|---|---|
| A failing verdict | its own `Basis` — what the check that ruled against it actually saw |
| A standing `PlanViolation` | the disputing node's own words, verbatim. It is the only thing that saw the contradiction |
| A `NodeFailure` | the recorded failure, in the provider's or the tool's own words |

Read in that order, and no halt *kind* is needed to choose between them: at most one is ever present
for a node that just halted.

**A node that throws stops that node, not the run.** The failure is recorded on the node as a
`NodeFailure` — never as a failing verdict, because a verdict claims something was checked and a node
that died checked nothing — and the coordinator is asked what to do. Before it gets that far the loop
re-issues the attempt, bounded by `PlanRetryPolicy`: a dispute is a claim about the contract and
stands until the contract changes, while a failure is a claim about one attempt, so an attempt that
finishes clears it. **One kind of failure is never retried** — `NodeFailure.Terminal` marks the errors
waiting cannot fix, an account out of allowance rather than a provider having a bad minute, and a halt
carrying one is not escalated into seats that would fail the same way.

What the coordinator *is* asked for is a `PlanRationale` — its argument for the replacement it just
wrote — and that is recorded **beside** the reason, attributed to whoever concluded it:

```
version 2 — minted …, re-ruling delivery
  reason: The largest tenant's report is 4 GB; no buffer size is large enough.   ← observed
  planner says: Streaming keeps peak memory to one page.                          ← inferred
```

A single field for both is how a lineage comes to read like a record while being partly a guess. It
is also how a model asked *"why did this go wrong?"* — about work it never saw — comes to answer
confidently and wrongly, into the one part of a lineage no diff can recover.

**The state slot is one property.** `Tracking(read, write)` says where the pass outcome lives, the
same way `SubFlow` maps in and out. It is state rather than something the pattern keeps privately so
that a checkpointed run finds its plan's progress where every other job's progress is.

**A change of plan is not a new plan.** The tree stays in the supervision's store and carries its
versions, so looping back into the same supervised job picks up the next version with its lineage
intact. Spawning a fresh plan per change would discard exactly what versions exist to preserve.

### A halt a step is not allowed to answer

Not every stop is a failure. A step can reach a choice it is **not entitled to make** — two windows
that both fit, two formats that both satisfy the contract — and the honest thing is to stop and say
so rather than pick one:

```csharp
await executor.AskAsync(planId, nodeId,
    asks: "Which three-night window for Hakone?",
    options: ["the 2nd to the 5th", "the 6th to the 9th"]);
```

**A step's report sets its state.** A step reports whether it did its task (`Done`) and every option
it found (`Options`). The executor marks the node's `State` from that: done with one option, the node
is `Done` with that option as its `Result`; done with several, or not done, the node is `Blocked`, the
pass halts there, and the options are recorded as the node's question. The advisor offers every one
of them and recommends one; it drops none. Attended, a person picks; unattended, the recommendation is
taken. Picking an option of a step that did its task marks the step `Done` with it. Picking one from
a step that could not goes to the Planner as a change of plan.

**Nothing is wrong when this happens**, which is what makes it different from every other halt: the
contract is right, the work is right, and a decision inside it simply belongs to somebody else. A run
that recorded it as a dispute sends a coordinator off to re-author a contract that never needed
changing.

Answering it mints no version and re-words no criterion — the plan did not change, a choice inside
the work was made by somebody entitled to make it, and the step reads it off the node on its next
attempt:

```csharp
await supervision.AnswerAsync(planId, nodeId, "the 2nd to the 5th", by: "the traveller", ct);
```

### Offering rather than deciding

A coordinator **decides**. An advisor **offers** — two or three alternatives with one recommended,
for a person, or for an unattended run, to pick between:

```csharp
public delegate Task<PlanProposal> PlanAdvisor(PlanCoordination coordination, CancellationToken ct);
```

It exists because a run that escalates needs it and every consumer was writing it. Producing the
alternatives is a model's work; putting them where a host can render them, appending the ways out,
and answering on behalf of a run nobody is watching are not judgements at all — they are the same
four lines every time, and the fourth is the one that gets forgotten.

Two jobs, split at the pause:

| Job | Does |
|---|---|
| `PlanAskingJob` | asks the advisor and records the question, with its options, for somebody to answer |
| `PlanChoiceJob` | applies whatever was chosen, and mints the version if a change of plan was chosen |

**An option gives the step up, asks for a replan, or answers the step's own question — never two of
the three.** Giving the step up and asking for a replan are opposite claims about the same halt: one
says the plan is better without this step, the other says the plan is wrong and needs changing.
Answering says the plan is right and a choice inside the work has been made. Asking for a replan
carries no contract of its own — the Planner writes it, from the option's rationale and the rest of
what halted.

**At most one replan, and it names the problem, not the fix.** Several replans could only differ in
the change they describe, and which change to make is the Planner's. A replan's rationale quotes
the finding that makes the halt the plan's.

The advisor is shown `PlanTreeProjection.Lineage`: each earlier version's steps and criteria, and
why it was replaced.

What a seat is told about a halt is `PlanHaltRecord`, section by section, so a second seat reads the
same halt without owning the first:

| | |
|---|---|
| `Said` | what somebody answered off the list, verbatim and marked as theirs |
| `Remembered` | what earlier runs settled, marked as remembered rather than stated — it binds nothing |
| `Tried` | the lineage, and a line asking for the rationale to say so if the plan keeps stopping on one finding |
| `Refused` | what was answered already and could not be used, with the shape that would have been accepted |
| `ForPlanner` | the whole plan at a halt — the record above plus every step with where it stands and what it settled on, the terms that bind, and why it stopped |

Each is empty when there is nothing to say, so a record composes by addition and a halt nobody
answered reads without a heading for it. A supervisor is asked what to do about the step that
stopped, so it is shown that step; a Planner is asked which steps there should be, so `ForPlanner`
shows it the plan.

**Not answering is not an answer.** An outstanding question returns the state untouched and the
topology routes it back to where it was asked, so a run cannot advance by being ignored.

**Three things can be answered, and they are told apart by shape rather than by parsing**: `Picked`
takes one of the options, `Said` is free text nobody listed, and `Refused` is `PlanRefusal.Cancel`.
The last two are appended by whatever puts the question and are never in the list, so no supervisor
can offer a menu with no way out of it — that is what makes narrowing to three options safe. An
off-list answer is not applied and not treated as a refusal: it goes **back to the supervisor**, which
reads it and offers again with it settled.

**Giving one step up is an option now, not a refusal** — `PlanOption.Abandon` — so it competes with
the alternatives, can be recommended, and can be taken by an unattended run. As a refusal it was the
one course of action nobody had to weigh.

**One topology serves attended and unattended runs.** `WithAdvisor(advisor, unattended: true)` answers
the recommendation in the job rather than pausing; the pause is still declared, it simply finds
nothing outstanding when it arrives. Two lanes would mean the unattended path is a second
implementation of the loop, free to drift from the first — and the run that minted twenty-one plan
versions without stopping happened on the one with nobody in it.

### Giving a step up

Sometimes the answer is to go without something. That is **not** a re-ruling: the plan was right, and
somebody has decided not to have this part of it.

```csharp
// Offered as an option the supervisor authored, and applied when somebody takes it:
new PlanOption { Summary = "Go without Hakone", Abandon = "the traveller would rather go without" };
await supervision.AbandonAsync(planId, nodeId, reason, by, ct);   // what taking it does
```

The step stays in the plan, `NodeLifecycle.Abandoned` is what it now reads, nothing will attempt it
again, and the run carries on with the rest. Its parent stops waiting on it; a dispute it had raised
stops travelling upward, because there is no longer a contract to be wrong about. The work it already
did stays on the record — this is a decision, not a retraction.

**It is a state on the step rather than an edit to a child list, and that is the whole point.** Saying
*drop this one thing* by re-authoring the parent's children drops by omission: asked about one
over-full day, a live supervisor chose *drop a highlight* and destroyed six completed steps it had
simply not re-listed — and every one of them was reported as done.

`PlanStepAbandoned` is reported when it happens, which is the seam a consumer releases whatever the
step was holding from. Nothing else says so: no version is minted, so without the event the only
trace would be a field on a node nobody thought to read.

### When nobody can propose anything: the Planner

A supervisor may replace a contract. Only the **Planner** may decide which steps there are.

```csharp
public delegate Task<AuthoredPlan?> PlanAuthor(
    PlanCoordination coordination, IReadOnlyList<string> refused, CancellationToken ct);
```

It runs on **two signals: a proposal that kept nothing, and a chosen replan.** An advisor that looked
and found nothing is not having a bad day — it is making a statement about the *shape* of the plan,
from the one role that has seen the halt, the tools and the tree. Not on a proposal that merely ran
out of tool rounds, which is a budget rather than a plan, and not on a halt somebody has been asked
about and answered with something that settles it here, because a person choosing is the answer.

The second signal arrives the same way whether an unattended run took the recommendation or a person
picked it: `PlanChoiceJob` reads the chosen option's `Replan` and turns it into
`PlanDecision.Replan(rationale)` — a `ReplanPlan` with no contract, which is the Planner's signal to
write one.

It is told what was refused — every alternative that was tried and every reason it could not be used —
because that is the most specific evidence anybody has about why the present shape does not work.
Returning `null` is a real answer: the plan is what it should be, and the halt stands.

**What it writes is admitted before it runs.** A plan whose criteria name checks that do not exist, or
that could never hold, is refused rather than executed to find out. And it may not un-abandon: work
somebody gave up stays given up, which holds by construction — a re-listed node keeps the decision
recorded on it, and one left out is dropped rather than revived.

Admission answers whether a plan **is** one. Whether it would **hold** is a different question, and a
Planner can ask it about its own draft before returning it:

```csharp
var draft = tree.Rerule(tree.Root.Id, tree.Root.Contract, "draft", steps);
var unmet = await PlanFindings.UnmetAsync(draft, checks, ct);
```

Every criterion a check can decide is run — each step's in the plan's order, then the plan's own, last
because a check over the whole plan has nothing to read until the steps are known — and what does not
hold comes back in the checks' own words. A draft that fails can go back to the model with the
findings as data rather than as advice.

This is worth doing where a tool already offers the model the same verdict. A tool the model may skip
is advice: one demo's Planner had a `check_plan` tool and wrote a plan that tool had rejected twice,
and every plan that did not hold then cost a pass and a change of plan.

`PlanReauthored` and `PlanNotReauthored` say which happened. The second matters as much as the first:
until it existed, *the shape of the plan is what is wrong* and *the supervisor had a bad day* looked
identical from outside.

### Reviewing the finished work against the goal

Every criterion can hold and the work can still not be what was asked for. A reviewer reads the
finished plan against the one thing a supervisor may not rewrite — the **goal**:

```csharp
public delegate Task<PlanReview> PlanReviewer(PlanCoordination coordination, CancellationToken ct);
```

This is written against a real run. A supervisor re-ruled the hardest step of a trip into *"go to the
island and stay overnight — return planned in follow-up"*, its own rationale admitting the dependency
it left. Every criterion held, the plan reported satisfied, and the family had no way home. No fixed
set of criteria could have caught it, because the criteria that mattered were written by the
supervisor as part of the change.

A rejection is recorded as a dispute against the root and travels the way every other halt does — the
coordinator is asked, a person is shown the options where one is wired, the round is charged like any
other. Nothing new is added to the loop.

**Silence is acceptance.** A reviewer that answered nothing has not found a fault, and stopping
finished work on no evidence is worse than missing one.

**And there is a third thing it can say.** Work that achieves its goal while leaving a third of its
budget unused is not *wrong*, it is *improvable* — so the verdict stays binary and the finding travels
beside it:

```csharp
PlanReview.Accept(new PlanUnspent
{
    What = "3 of 12 nights are unused",
    CouldBuy = "a night in Nikko"
});
```

That produces a halt that asks whether the remainder is worth spending — a decision that is available,
not a fault — because a run that finishes under budget and says nothing looks exactly like one that
needed everything it had.

### The whole loop, wired

Each of those is optional and each is one builder call:

```csharp
var workflow = AgenticPattern.SupervisedPlan<TripState>("tokyo-trip")
    .WithPlan(plan)
    .Supervised(supervision)
    .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
    .WithAdvisor(advisor, unattended: false)   // offers; implies the chooser
    .WithPlanner(planner)                      // re-authors, when nothing could be offered
    .WithReviewer(reviewer)                    // reads the finished work against the goal
    .MaxChangesOfPlan(4)
    .Build()
    .UseCheckpointing(new FileCheckpointStore("./runs"));
```

```
plan      → halted ? propose : review          review only if wired, else __end__
propose   → author                             author only if wired, else coordinate
author    → coordinate
review    → halted ? propose : __end__         a rejection is a halt, so it re-enters at propose
coordinate → __end__   stopped, referred, or out of changes of plan
           → propose   a question is still outstanding — nobody may advance by ignoring it
           → plan      otherwise: something was decided, so run the plan again
```

The job names are the handles — `SupervisedPlanBuilder<T>.AdvisorJob`, `.PlannerJob`,
`.ReviewerJob`, `.CoordinatorJob` — and `CoordinatorJob` is still `"coordinate"`, so anything already
pausing on it keeps working whether or not you wire an advisor in front of it.

**`WithAdvisor` implies the chooser.** With it and no explicit `WithCoordinator`, the `coordinate`
seat becomes the `PlanChoiceJob` that applies whatever was picked. Supplying both is legal and is the shape
for a coordinator that has to pause *between* proposing and deciding.

**Wire none of them and the topology is exactly what it always was** — `plan → coordinate → loop` —
so nothing about an existing supervised plan changes. `WithAdvisor`, `WithPlanner` and `WithReviewer`
also read `SupervisionOptions.Advisor` and `.Author` when you set those instead; the explicit call
wins.

### A coordinator that is not a model

`WithCoordinator` takes either form. A **delegate** is a function of the halt; a **job** is for a
decider that is not — a person to be asked, a queue to post to, a rules table, a sub-workflow:

```csharp
.WithCoordinator(new DeterministicPlanSupervisor(rules).AsCoordinator())   // a delegate
.WithCoordinator(new AskTheOnCallEngineer(supervision))                     // an IJob<TState>
```

`DeterministicPlanSupervisor` ships as the second kind of answer: a table of `HaltRule`s written
before the run. The first unspent rule matching the halt re-rules the plan; otherwise a **failure**
is retried up to its allowance; otherwise the halt stands. A rule fires **at most once** — handing
back the same replacement at every halt is a change of plan nobody authored.

**It never retries anything, and it no longer could.** Retrying left the vocabulary entirely: it is
mechanical, it belongs to the loop, and it happens before a coordinator is ever consulted. What
survives is the distinction that made it worth arguing about, and it is still worth carrying into
your own coordinator:

| What the tree holds | Would running it again help? |
|---|---|
| A `NodeFailure` | The loop has **already tried**, bounded by `PlanRetryPolicy`, before you were asked. If it is `Terminal` — an account out of allowance — it was not retried at all, and nothing you decide will change that |
| A standing `PlanViolation` | No. A dispute is a claim about the *contract* and stands until the contract changes; the node does the work again and comes back contradicted |
| A failing verdict with its `Basis` | Depends entirely on what the check saw, which is why the `Basis` is put in front of you rather than a halt kind |

**Which is the whole reason a halt has no *kind*.** A single word — *failed*, *disputed*, *blocked* —
is exactly the summary that made re-running look reasonable on a halt where the node would run nothing
at all. The evidence answers the question the word was standing in for.

*"None of these — propose something else"* is **not** a fourth decision, and `PlanDecision` should not
grow one. It is a judgement about the **proposal**, not about the plan, so it belongs inside your
coordinator's own loop — which is exactly the shape [Guide 07](07-human-in-the-loop.md#escalating-a-supervised-plan)
describes, where the pause is what bounds the asking.

**What a job coordinator owes you, and what it does not.** It decides, and it applies its own
re-ruling — `supervision.ReruleAsync(...)` — because it knows what it decided before anything else
can. Everything else is the framework's: write the decision where `Tracking` reads it, and the
change-of-plan budget is spent for you and the decision reported for you. A job that writes no
decision is not made to look decisive — nothing is reported and nothing is spent.

> **One difference to know about.** For a delegate, the decision is reported *before* it is applied,
> so the account reads in the order things happened. A job applies its own decision, so its
> `PlanDecisionTaken` necessarily follows the `PlanVersionMinted` it explains. The event is accurate
> and late; reporting it earlier would mean announcing a decision nobody had taken yet.

### Doing it by hand

The pattern is a composition, so nothing stops you writing it out — worth doing when the loop needs
a shape the builder does not have:

```csharp
.Supervise("attempt", tree, supervision, (s, r) => s with { Outcome = r })
.Job("redesign", RedesignJob)      // re-rules in the store, and hands nothing back
.Then("attempt", "redesign")
.Loop("redesign", loopTarget: "attempt", exitTarget: Workflow.End,
    until: s => s.Outcome!.HaltedAt is null, maxIterations: 3)
```

Either way the job re-rules through the supervision it already has — no executor to assemble, and no
risk of re-ruling a different store from the one being supervised:

```csharp
await supervision.ReruleAsync(planId, nodeId, newContract, reason, children, rationale, ct);
await revision.ApplyToAsync(supervision, planId, ct);    // or, for a revision declared in the manifest
```

At this level `reason` is a parameter, because a hand-written job may be re-ruling for something that
never halted at all. Pass `coordination.HaltReason` when it did — inventing one there is the same
mistake with a longer fuse.

One supervised job, re-entered — not two registrations of the same plan. The cap counts **changes of
plan you are willing to accept**, which only a consumer can decide; how many passes a plan needs to
settle is derived from the tree, and nothing counts those.

---

## Wiring one for your own case

Four things are chosen together, and the demo picks one combination of them. This is how to pick
another.

### Supervision or coordination?

The two words sit next to each other in the builder and the first question is which one you want.
**One rules on the work; the other rules on the plan.**

| | **Supervision** (`SupervisionOptions`) | **Coordination** (`PlanSupervisor`) |
|---|---|---|
| Answers | *Did this node meet its contract?* | *Was the contract right?* |
| Runs | inside every pass, once per node | between passes, once per halt |
| Made of | `Runner`, `Verifier`, `Store` | one decision: re-rule · retry · stop |
| May change the plan | **never** — it records satisfied, disputed or failed | **only it can** — a re-ruling mints a version |
| Sees | one node, its contract, its evidence | the halt, the tree around it, and the budget |

**Why they are separate** is the tier's whole argument: the authority to change a criterion belongs to
the level that wrote it. A node reports; it never asks. If the supervision could re-rule, a node that
found its contract inconvenient could rewrite it.

You always configure a supervision. You configure a **coordinator** only when you want the plan to be
able to change — without one, `Supervise` on its own is the honest shape, and the builder says so if
you leave the slot empty.

### The four choices

| | Answers | What ships |
|---|---|---|
| `Runner` | who does a node's work | `PlanNodeAgentRunner` for an agent; any delegate for a shell command, a script, a person |
| `Verifier` | who rules on whether it met its contract | `DeterministicVerifier` over checks; `AgentVerifier` for what checks cannot see |
| `Store` | where the tree and its lineage live | `InMemoryPlanTreeStore`, `FilePlanTreeStore` |
| checkpointing | where a *paused* run lives — set on the workflow, not the supervision | `InMemoryCheckpointStore`, `FileCheckpointStore` |
| coordinator | what a halt means | `AgentPlanSupervisor` (a model), `DeterministicPlanSupervisor` (a table), or your own delegate or job |

Supplying `Executor` instead of `Runner` builds the default `PlanNodeAgentRunner` for you; `Runner`
wins when both are given, which is what lets a persona, tools or a trajectory observer through.

### Three shapes

**All model.** The cheapest to write and the easiest to fool: nothing outside the model rules on
whether the work was done.

```csharp
var supervision = new SupervisionOptions
{
    Executor = worker,                       // runs the nodes
    Supervisor = stronger,                      // authors a change of plan
    Store = new FilePlanTreeStore("./plans")
};
```

Expect abstentions to be invisible here — with no checks, a criterion nothing can decide is decided by
the model that wrote it. Give it an verifier before trusting it, even one that can only abstain.

**Model plus checks — the shape to default to.** The work is a model's; the ruling is not.

```csharp
var supervision = new SupervisionOptions
{
    Executor = worker,
    Supervisor = stronger,
    Verifier = new DeterministicVerifier(
        [new ProcessCheck("dotnet", "test", ["the build is green"])]),
    Store = new FilePlanTreeStore("./plans")
};
```

When a re-ruling authors criteria your checks cannot decide — which happens the moment a model writes
new acceptance criteria — add the [reviewer](#when-nothing-can-check-it-the-reviewer-role), which
rules on the record and only on what the checks abstained from:

```csharp
    Verifier = new AgentVerifier(reviewerModel, new DeterministicVerifier(checks))
```

**No model at all.** Every node is a command, and the coordinator is a table. This is the shape that
makes a plan run reproducible, and it is what the demo's scripted mode uses:

```csharp
var supervision = new SupervisionOptions
{
    Runner = (context, ct) => RunTheStep(context, ct),   // your own: a command, a script, a person
    Verifier = new DeterministicVerifier(checks),
    Store = new InMemoryPlanTreeStore()
};

var workflow = AgenticPattern.SupervisedPlan<MyState>("build")
    .WithPlan(tree).Supervised(supervision)
    .Tracking(s => s.Coordination, (s, c) => s with { Coordination = c })
    .WithCoordinator(new DeterministicPlanSupervisor(rules).AsCoordinator())
    .Build();
```

A `HaltRule` names what it matches (`NodeId`) and what it replaces (`Contract`, `Children`,
`ReruleNodeId`), and fires at most once. It matches on **which node stopped** and nothing else: it
used to match on the halt kind as well, and that went with the kind.

### And when a person has to answer one

That is a conditional pause, not a plan-tier concept: see
[Guide 07 — Human-in-the-Loop](07-human-in-the-loop.md#escalating-a-supervised-plan). The whole of it
is `.EscalateToAPerson(when)` and a `ResumeAsync` carrying the decision.

---

## Watching a plan run

A plan reports what it is doing as ordinary workflow events, read through the stream you already
read. There is no observer interface to implement:

```csharp
await foreach (var evt in workflow.StreamAsync(state))
{
    switch (evt)
    {
        case PlanNodeStarted started:
            Console.WriteLine($"{started.NodeId} — read {started.ProjectedTokens} tokens of the plan");
            break;
        case PlanNodeReported reported:
            Console.WriteLine($"  {reported.Summary}");
            break;
        case PlanNodeDisputed disputed:
            Console.WriteLine($"  ⚠ {disputed.Criterion}: {disputed.Reason}");
            break;
        case PlanNodeVerified { Abstained.Count: > 0 } ruled:
            Console.WriteLine($"  nothing could decide: {string.Join(", ", ruled.Abstained)}");
            break;
        case PlanVersionMinted minted:
            Console.WriteLine($"  v{minted.PlanVersion}: {minted.Reason}");
            break;
    }
}
```

| Event | Reports |
|---|---|
| `PlanNodeStarted` | a node is about to run, and what the projection showed it |
| `PlanNodeReported` | what the node said it did |
| `PlanNodeDisputed` | a node returned rather than routing around a contract it believes is wrong |
| `PlanNodeFailed` | an attempt did not finish — retries exhausted, a tool fault, a reply that would not parse |
| `PlanNodeVerified` | the ruling, what was abstained on, the score, whether a bound was passed |
| `PlanPassCompleted` | what one pass ran, what it skipped, and where it stopped |
| `PlanDecisionTaken` | the supervisor was asked what a halt means, and what it decided |
| `PlanVersionMinted` | the plan changed: which node, why, what was dropped, and which of the new criteria nothing can decide |

Three of these carry things you cannot get back afterwards, which is why they are events rather than
something to read off the tree at the end:

- **What a node was shown.** The tree keeps one reading per node, so once a node runs again, what the
  earlier attempt was shown is gone. `PlanNodeStarted.SawEverything` is answerable only while it runs.
- **What nothing could decide.** The tree records verdicts, and an abstention is the *absence* of one.
  `PlanNodeVerified.Abstained` names the gap between what was verified and what merely looks fine.
- **Every account a node ever gave.** The tree keeps the *last* summary on the node's reading, so
  what an earlier attempt said about itself is gone once it runs again. `PlanNodeReported.Summary`
  is the only place the whole sequence exists.
- **What a change of plan left ungated.** `PlanVersionMinted.CriteriaNothingCanDecide` is a fact
  about what was configured at the moment the version was minted, not about the plan, so the tree
  cannot hold it — the same criteria under a differently configured run are a different answer.
- **Every decision the supervisor took.** A re-ruling leaves a version behind, but a `Retry` changes
  nothing and a `Stop` ends the run, so two of the three would otherwise have to be inferred from
  what stopped happening. `PlanDecisionTaken` carries what halted, the cause, the decision itself,
  and how much of the change-of-plan budget was left — and it is reported *before* the decision is
  acted on, so the account reads in the order it happened.

Outside a workflow they still work — reporting with nobody listening is a no-op, and the events carry
`PlanId` with an empty `WorkflowName` rather than inventing one.

### Watching a whole session, not just its decisions

`PlanNarrator` answers *what did the plan decide*. It cannot answer *what did the session do* —
which job is running, what it asked the model, which tools it called and what came back — because
none of that is a plan event: it arrives as spans on `IWorkflowTracer`.

`SessionTrace` (in `Ananke.Design`) is both, joined into one account in the order things happened:

```csharp
var trace = new SessionTrace(Console.WriteLine);

workflow.UseTracing(trace);                       // the work below the plan
await foreach (var evt in workflow.StreamAsync(state))
    trace.Note(evt);                              // the plan's own decisions

await File.WriteAllTextAsync("trace.md", trace.ToTranscript());
```

```
     ▸ plan — started
         ↳ list-changes: called read_changelog → 329 chars back
         ↳ list-changes: called save → 28 chars back
       · list-changes: asked the model — 2 tool round(s)  (9.8s)
   1. list-changes             Read the changelog and saved changes.csv.
     ── pass 1: 4 node(s) ran
     ▪ plan — done  (44.3s)
     ▸ coordinate — started
     ⟐ the supervisor was asked about 'write-index' (says it cannot do what was asked) — 0 of 3 changes of plan used
       it decided: re-rule 'write-index' — the plan was wrong, and this replaces it
```

Three things about it are deliberate:

- **It explains rather than dumps.** A span carrying `tool.hallucination=true` is not printed as an
  attribute; it is printed as *no such tool; it was told so and can correct itself*. Prompts and
  replies are deliberately absent — this is a record of the session's *conduct*, and the model's own
  words already have a home in the node's report.
- **A tool call is attributed from the span's name**, not from the event stream. A plan node's agent
  is named `plan-node:<id>`, so the work says which step wanted it; reading that from events instead
  would attribute it to whatever the consumer had reached, which can be a step behind.
- **It is local, and it is the copy that is guaranteed.** No collector, no exporter, no account —
  the transcript is a string you can write next to whatever the run produced. A provider's dashboard
  is a convenience; this is the record.

### Reading them without writing the switch

The events are typed because each carries something the others do not, and the cost of that is a
switch in every consumer that wants to watch a run. `PlanNarrator` (in `Ananke.Design`) is that
switch, shipped:

```csharp
await foreach (var line in workflow.StreamAsync(state).Narrate())
    Console.WriteLine(line);
```

It is stateful on purpose. A node's start and what it reported are two events and read as one line,
so a started node is held until something else arrives — which also means **a node that never
reported still gets its line**. Steps and passes are numbered there, because those are facts about
the narration rather than about the plan, and the tree deliberately holds neither.

It also narrates what the supervisor offered (`PlanProposalOffered`) and what the Planner wrote
(`PlanReauthored.Authored`), and each line about a halt names the plan version. An agent's tool
calls reach the same stream as `AgentToolCalled`: the job, the tool, its arguments, and its result
cut to `AgentToolCalled.ResultCap` characters.

For a run that needs its own words in the middle of the shipped ones, `Describe` takes one event at
a time and returns `null` when it has nothing to add — anything that is not a plan event, and a
ruling that simply passed:

```csharp
var narrator = new PlanNarrator();

await foreach (var evt in workflow.StreamAsync(state))
{
    if (narrator.Describe(evt) is { } line)
        Console.WriteLine(line);

    if (evt is PlanVersionMinted)
        Console.WriteLine(WhyThisMatters);
}
```

---

## Nobody rules on their own work

A node reports what it observed. Whether that meets the contract is ruled **outside** it, by an
`IVerifier`:

```csharp
var verifier = new DeterministicVerifier(
[
    new ProcessCheck("dotnet", "test --no-build", ["Every existing test still passes."]),
    new PredicateCheck("the exported file", ["An exported file is byte-identical to the online report."],
        _ => File.ReadAllBytes(exported).SequenceEqual(File.ReadAllBytes(online)))
]);
```

`IDeterministicCheck` is narrow on purpose — an `Oracle` name, `CanRule(criterion)`, and
`RunAsync(criterion, ct)`. Anything that needs to *read and weigh* in order to answer is not one of
these and must not be dressed up as one: a check that quietly guesses is worse than no check, because
its verdict carries the authority of something that ran.

Two implementations ship, and they cover most gates:

| Check | Decides by | Oracle |
|---|---|---|
| `ProcessCheck` | running a command and reading its exit code — a build, a test suite, a linter | the command line, so the record says how to run it again by hand |
| `PredicateCheck` | evaluating a predicate in process — a file that must exist, a count that must be zero, a byte comparison | whatever you name it |

**Both are told which criteria they cover, and refuse everything else.** A check that answered for
every criterion it was handed would make abstention impossible, and abstention is the part that keeps
the unverified visible.

`ProcessCheck` makes one more distinction the interface cannot express: **"the check could not run" is
not "the criterion is not met".** A missing executable or a command that outruns its `Timeout` throws,
rather than returning a verdict — recording a fabricated `false` would put a criterion in the tree as
checked and failed when nothing checked it at all. A non-zero exit code, on the other hand, is a real
verdict; `SuccessExitCodes` says which codes mean the criterion holds, for the runners that do not use
zero.

`DeterministicVerifier` returns an `Verification`:

| `VerificationOutcome` | Means |
|---|---|
| `Passed` | Every acceptance criterion was checked and held |
| `GateFailed` | At least one was checked and failed |
| `Abstained` | Some criterion had no check that could decide it, so nothing was ruled either way |
| `ViolationStands` | The node disputed its contract and nothing could refute it — it travels upward |
| `ViolationRefuted` | The node disputed a criterion and a check decided it holds after all |

**Abstention is the feature.** Where no check exists, the verifier says "nothing was decided"
rather than guessing, and `Verification.Abstained` names exactly which criteria those were. That gap —
between what can be verified and what merely looks fine — stays visible instead of being papered over.
Filling it with a model is a separate, riskier decision: an verifier sits in the *control* path,
not only the measurement path, so a judge that is wrong here does not merely mismeasure the work, it
misdirects it.

Note what `DeterministicVerifier` will not do. It can rule that a dispute was *mistaken*, when a
check decides the disputed criterion holds. It never *upholds* a dispute on its own authority —
whether a contract is wrong belongs to whoever wrote the criterion, and the dispute travels there.

### A re-ruling is checked against the checks that will have to rule on it

Verdicts are matched by **exact text**, so a plan re-authored in new words can come out of a change of
plan gated by nothing at all: every new criterion is abstained rather than failed, and every node
still reads as fine. That failure is silent by construction, and it is the one shape here that makes
an unverified run look finished.

So a re-ruling asks the configured verifier which of the criteria it just authored nothing can
decide, and the answer rides on `PlanVersionMinted`:

```csharp
case PlanVersionMinted { CriteriaNothingCanDecide: { Count: > 0 } ungated } minted:
    Console.WriteLine($"  v{minted.PlanVersion} — criteria nothing can decide: {ungated.Count}");
    break;
```

Three things about it are deliberate:

- **It does not block.** Abstention stays legitimate — a criterion worth stating before anyone has
  written a check for it is not a mistake. It stops being *silent*, which is the whole complaint.
- **Nothing runs to answer it.** `IVerifier.CannotDecide(criteria)` is synchronous, and that is the
  ruling rather than a detail: an answer costing what a ruling costs would not be affordable at the
  moment it matters. An verifier that would have to do the work to find out returns `null` —
  *nobody could say* — which is a different fact from an empty list and must not render as one.
- **Gates only.** A quality criterion nothing can decide narrows the score, and that is visible in the
  score; counting both would dilute the number that matters.

`DeterministicVerifier` answers from `CanRule` — the same test it applies before running anything —
so it cannot report a criterion decidable and then abstain on it.

### When nothing can check it: the `reviewer` role

Some criteria have no check and could not have one — *"every step of this plan has a recorded
verdict"* is about the plan's own state, not about a file. `AgentVerifier` puts a model on the
`reviewer` role and asks it about exactly what the checks left undecided:

```csharp
Verifier = new AgentVerifier(reviewerModel, new DeterministicVerifier(checks))
```

**The reviewer judges the record; the planner judges the contract.** That is the line the interface
already draws — a ruling may say a criterion was or was not met, never that it is *wrong* — and it
is why these are two roles rather than two rungs of a price ladder.

Four rules, and each has a failure behind it:

| Rule | Because |
|---|---|
| **Checks first, and they win** | They read the artifact and can be re-run. The reviewer only ever sees what they abstained on, and never overturns them |
| **Anything but a clear ruling is an abstention** | An unverified criterion recorded as met is the one outcome that makes an unfinished run look finished |
| **It may not rule a criterion wrong** | Whether a contract is right belongs to whoever wrote it; a node's dispute travels there regardless. The answer shape cannot express it |
| **It may not refute a dispute** | A check that refutes one ran a program over the artifact; a model that refutes one has re-read the record the node read and disagreed with the witness |

**It is shown the record and not the node's account of it** — and not the node's *projection* of the
record either, which is budgeted for the work rather than for the ruling. What it cannot see, it
cannot rule on: a criterion about a file's contents gets an abstention, and that is correct.

**A model verdict carries its grounds.** `CriterionVerdict.Basis` is what the oracle saw — empty for
a check, whose exit code is its own explanation, and required in practice for a judgement, which
without it is a claim nobody can weigh. It is also how a wrong ruling is caught: a live reviewer once
answered *met* to *"every step has been ruled on"* while its own basis named only the steps that had
verdicts. The criterion was reworded to say what it counts; the basis is what made that visible.

Verification is opt-in. A plan with no verifier records the node's own verdicts, which is the
behaviour every caller had before it existed — and reports `null` for decidability rather than a
clean bill of health it is in no position to give. **A reviewer reports `null` there too**: whether
it can decide a criterion is only knowable by reading the record and ruling on it, which is the work
itself.

---

## What the node actually saw

`PlanTreeProjection.Project` renders the tree for one node under a token budget, and reports what it
had to leave out. Every run writes a `NodeReading` back onto the node — even when the node produced
nothing:

```csharp
var reading = tree.Node("build-endpoint").LastRead;
reading?.PlanVersion;         // the plan version in force when it ran
reading?.ProjectedTokens;     // how big the view it was given was
reading?.SawEverything;       // false when the budget forced omissions
reading?.Summary;             // what the node said it did, in its own words
```

**`Summary` is narration, and never evidence.** Nothing derives from it, no status depends on it, and
it never becomes a verdict — it is what the node *says*, not what was checked. It is kept because it
is the one thing about a run that no verdict and no dispute can reconstruct: verdicts say which
criteria held and a dispute says what contradicted the contract, and neither says what was actually
attempted. Whoever has to decide what a halted plan should become is otherwise reasoning about work
nobody described, which is how a model comes to invent a reason. A runner that offers no prose — a
shell check, a person — leaves it `null`, and a reader is told the gap is a gap.

The omission counts are load-bearing. The argument for reading a tree rather than passing records is
that a loss becomes recoverable — but that only holds if the node *could* have read what it needed.
When a node goes wrong, this separates content that was withheld from it from content it was shown
and did not use. The second is much worse news, and without this it is indistinguishable from the
first.

---

## Reading a plan afterwards

`PlanTreeProjection` serves a model under a budget. A person needs the opposite, so
`PlanReportExporter` (in `Ananke.Design`) renders the same tree with nothing elided:

```csharp
Console.WriteLine(tree.ToOutline());   // the version in force
Console.WriteLine(tree.ToLineage());   // what each version changed, added and dropped
Console.WriteLine(tree.ToReport());    // both
```

The outline is the decomposition with each node's derived status, the goal it was given, and every
criterion with the verdict standing against it — including the ones nothing decided, which are
rendered `unchecked` rather than left out. A node marks itself when it has never run, when it last
ran against an earlier plan version, and when it has used up the attempts its contract allowed:

```
build-endpoint — Satisfied
  Expose the export endpoint.
  · met      The endpoint returns a downloadable file.  (build + tests) — after 1 attempt(s) that did not
write-docs — Pending
  Document the export endpoint.
  · unchecked  The endpoint appears in the API reference.
```

The lineage is where re-ruling pays for itself over a cancelled flag:

```
version 2 — minted 2026-08-28 07:55:33Z, re-ruling delivery
  reason: The largest tenant's report is 4 GB…
  contract changed: delivery
    goal was: Build offline export on top of what discovery found.
    goal now: Build offline export by streaming, on top of what discovery found.
    criterion dropped: A report exports end to end from the API.
    criterion added:   A report exports end to end from the API, at any tenant size.
  added:   build-streaming-writer — Stream rendered pages straight to storage.
  dropped: build-buffer — Assemble the whole report in memory before writing it.
           not cancelled: still readable in version 1
  carried over unchanged: 8 node(s)
```

**All of that is derived by comparing the two versions**, not read from an annotation — the same
discipline that keeps status out of a node. The two stored fields it uses are the reason and, when
somebody offered one, the rationale: why a plan stopped being right is the only part no diff can
recover, and who concluded what to do about it is the only part that was never observed at all.

---

## Rules of thumb

- **Don't store status.** If the structure can answer it, ask the structure.
- **Don't pass results between nodes.** What a node knows, it reads from the tree.
- **Don't let a node grade itself.** Give the executor an verifier, and let abstention show you
  where you have no oracle.
- **Don't treat a dispute as an error.** A leaf contradicting a decision made at the root is the
  cheapest place that discovery will ever happen. The failure mode is not the contradiction — it is
  the contradiction having nowhere to go.
- **Don't cancel — re-rule.** `Rerule` records what changed and why; a cancelled flag records only
  that something stopped.

---

## Type reference

| Type | Purpose |
|---|---|
| `AgentContract` | Goal, acceptance and quality criteria, constraints |
| `PlanTree` | A plan and every version it has been through; all derived queries live here |
| `PlanNode` | One work item: contract, children, verdicts, dispute, last reading |
| `PlanVersion` | One state of a plan, with what was re-ruled, what was dropped, and why |
| `NodeLifecycle` | What happened to the attempt: `Planned`, `Running`, `Completed`, `Faulted`, `Abandoned` |
| `ContractOutcome` | What the verdicts say: `Unmet`, `Met`, `Disputed` |
| `PlanRunOutcome` | How a run ended: `Completed`, `Blocked`, `Abandoned`, `Faulted` |
| `CriterionVerdict` | One criterion, whether it held, which oracle decided, and when |
| `PlanViolation` | A node's report that its contract is wrong. A report, never a verdict |
| `NodeFailure` | An attempt that did not finish, in whatever threw's own words. `Terminal` marks the ones waiting cannot fix |
| `PlanRationale` | Somebody's argument for a change of plan, attributed. Never merged into the version's reason |
| `NodeReading` | That a node ran against a given plan version, how much of it it saw, and what it said it did |
| `IPlanTreeStore` | `InMemoryPlanTreeStore` for tests, `FilePlanTreeStore` for runs that outlive a process |
| `ICheckpointStore` | Where a paused run lives: `InMemoryCheckpointStore`, or `FileCheckpointStore` when the wait outlasts the process |
| `PlanExecutor` | Walks the tree post-order — `ExecuteAsync` for one pass, `ExecuteToCompletionAsync` until it stops changing |
| `SupervisedJob<TState>` | A plan as one job in a workflow, registered with `Supervise` |
| `SupervisionOptions` | Who runs a node, who rules on it, where the tree lives, how much of it a node sees |
| `PlanManifest` | A plan as data — decomposition, contracts and revisions; `Ananke.Design` |
| `PlanNodeManifest` / `PlanRevisionManifest` | One declared work item, and one declared re-ruling |
| `NodeQuestion` | A choice somebody must make: what needs deciding, and what would answer it. `Done` says whether picking one of its options settles the step. Recorded by `AskAsync`, or by the executor where a step's report halted the pass |
| `StepState` | Where a step, or the whole plan, stands: `Pending`, `Done`, `Blocked`, `Skipped`. Marked by the executor from what a step reported, never by the step itself |
| `PlanAnswer` | What a node was told when it asked. A decision, never a verdict |
| `PlanAbandonment` | That somebody decided not to have a step, why, and who decided |
| `PlanAdvisor` | Delegate that **offers** the changes a halt admits, for somebody else to choose between |
| `PlanProposal` / `PlanOption` | The alternatives, and one of them — `Abandon`, `Replan`, or an `Answer`, never two |
| `PlanQuestion` | What is outstanding, and the three shapes of answer: `Picked`, `Said` (free text), `Refused` |
| `PlanRefusal` | The one refusal nobody authors: `Cancel` ends the run. Giving a step up is an option now, not a refusal |
| `PlanAskingJob<TState>` | Asks the advisor and records the question; answers it in-job when unattended |
| `PlanChoiceJob<TState>` | Applies whatever was chosen, and mints the version if a change of plan was |
| `AccountedSupervisorJob<TState>` | Wraps a coordinator job so the budget is spent and the decision reported by the framework |
| `PlanAuthor` / `AuthoredPlan` / `AuthoredStep` | The Planner: re-authors the whole plan when a proposal kept nothing |
| `PlanAuthorJob<TState>` | Hands that halt to the Planner, admits what it writes, and applies it |
| `PlanReviewer` / `PlanReview` / `PlanUnspent` | Reads finished work against the goal; accepts, rejects, or says what was left unspent |
| `PlanReviewJob<TState>` | Runs the review when nothing is left to run, and turns a rejection into a halt |
| `PlanNodeRunner` | Delegate that runs one node |
| `PlanNodeContext` | What a node is given — all of it out of the tree |
| `NodeOutcome` | What a node produced: a `Candidate` to apply, and narration. `NodeOutcome.Nothing` for neither |
| `PlanRunResult` | Facts about one pass that the tree does not record |
| `PlanNodeAgentRunner` | Runs a node as an agent job with its contract pinned |
| `PlanNodeReport` | What the agent answers: a `Summary`, and an `Operation` it proposes. No verdict, no dispute, no question |
| `IVerifier` | Rules on whether a node met its contract, from outside the node; `CannotDecide` answers what it could not rule on, before anything runs |
| `AgentVerifier` | A model on the `reviewer` role, ruling from the record on what the checks abstained from |
| `DeterministicVerifier` | Rules from `IDeterministicCheck` gates only, and abstains elsewhere |
| `IDeterministicCheck` | A gate that decides without judgement, returning a `Finding` — whether it holds, and the world's own words when it does not |
| `Verification` | Verdicts, outcome, what was abstained on, optional score |
| `PlanTreeProjection` | Renders the tree for one node under a token budget |
| `Finding` | What a check found: `Holds`, and `Detail` — the failure's own words, never composed |
| `PlanCandidateApplier` / `PlanApplication` | Applies a shape-valid candidate to the world, and says whether it was even understood |
| `NodeRejection` | What a rejected attempt proposed and why, carried verbatim into the retry |
| `PlanRetryPolicy` | How many times the loop re-issues an attempt that produced nothing. Nothing it counts reaches the plan |
| `PlanTerm` | Something an answer settled that outlives the halt. Bound into every contract authored afterwards |
| `PlanReportExporter` | Renders the tree and its lineage for a person — `ToOutline`, `ToLineage`, `ToReport`; `Ananke.Design` |
| `PlanNarrator` | Turns plan events into lines as they arrive — `Describe(evt)`, or `.Narrate()` over the stream; `Ananke.Design` |
| `PlanEvent` | Base for plan progress on the workflow's event stream — `PlanNodeStarted`, `PlanNodeReported`, `PlanNodeDisputed`, `PlanNodeFailed`, `PlanNodeBlocked`, `PlanNodeAnswered`, `PlanNodeVerified`, `PlanPassCompleted`, `PlanDecisionTaken`, `PlanVersionMinted`, `PlanStepAbandoned`, `PlanReauthored`, `PlanNotReauthored` |
| `SessionTrace` | Joins those events with the spans below them into one readable account, locally — see the section below |

---

## Next

- [ItineraryDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/02-workflow-patterns/ItineraryDemo) — the whole of the above running end to end, live: `InvocationCheck` criteria ruled against a service, `PlanFindings` refusing a draft before it is written, `PlanHaltRecord.ForPlanner` as the record the Planner reads, and `--hitl` for a person at the pause
- [02 — Workflows](02-workflows.md) — the `Workflow<TState>` builder, and where a `PlanNodeRunner` gets its agent job from
- [07 — Human in the Loop](07-human-in-the-loop.md) — interrupts and resume, for the case where the level that authored a criterion is a person
