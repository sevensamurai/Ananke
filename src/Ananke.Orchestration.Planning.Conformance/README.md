# Ananke.Orchestration.Planning.Conformance

The contract for the plan tier's **operation seam** — an `OperationCatalog` and a
`PlanCandidateApplier` — as framework-neutral scenarios.

## Why this is its own package

`Ananke.Orchestration.Conformance` holds the provider contract and depends on
`Ananke.Abstractions` alone. The operation seam lives in `Ananke.Orchestration`, and a provider
adapter proving only the streaming contract should not acquire that assembly to do it. The
dependency is real, so it is kept on this side of the line.

## What it proves

Half of the list is your implementation and half is the tier's behaviour around it:

| | |
|---|---|
| the catalog | admits what you say it admits, refuses what you say it refuses, refuses an absent or unnamed operation, and names every operation in its legend |
| the applier | is reached exactly once by an admitted proposal, and never by one the catalog refused |
| the tier | never hands the applier a candidate from a step that reported not-done, nor a step that proposed nothing |
| a rejection | travels into the next attempt with its finding verbatim and its candidate intact |

The not-done scenario is the one worth knowing about. A step that proposes cannot have met its
contract yet — nothing has applied or checked its change — so a proposing step reports `Done: true`,
and a step reporting `false` has its candidate dropped before the gate and before the applier, with
no event and no retry to show for it. A seam whose steps report `false` applies nothing and looks
exactly like a model that proposed nothing.

## Using it

```csharp
[TestFixture]
public sealed class MySeamTests : OperationSeamConformanceTests   // ...Conformance.NUnit
{
    protected override PlanOperationSeam CreateSeam() => new()
    {
        Operations = MyDomain.Catalog,
        Applier    = new MyApplier(workspace).AsApplier(),
        Admissible = new Operation { Name = "apply_diff", Arguments = ["sw.py", OneHunk] },
        Refused    = new Operation { Name = "apply_diff", Arguments = ["sw.py"] }
    };
}
```

`Admissible` and `Refused` are yours because the contract cannot guess them: every domain names its
own operations. `Refused` must be refused **on shape** — an unknown name, or a known one at the wrong
arity. An operation naming something that does not exist is admissible; the candidate reaches the
world regardless, and what the world says is ordinary evidence for the acceptance gate.

On another runner, drive `OperationSeamConformance.Scenarios` directly.
`OperationSeamConformance.ReferenceSeam()` is a subject the suite passes against, for checking your
wiring before pointing it at a real one.
