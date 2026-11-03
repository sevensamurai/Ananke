# Ananke.Orchestration.Conformance

Ananke's **conformance suite**: the scenarios every `IStreamingAgentModel`, `IToolSchemaTranslator`
and `IJsonSchemaTranslator` adapter must satisfy, and the scenarios for the plan tier's operation
seam. It is test code. It is not packable, and it is not a test project either: it is a library the
test projects reference, and the tests run where its fixtures are subclassed.

## Structure

The scenarios are **framework-neutral**: a scenario is a named delegate over the subject under test,
and every assertion is Shouldly, which throws its own exception type and needs no runner. The NUnit
fixtures over them are a thin layer in their own folders.

| Folder | What it holds |
|---|---|
| *(root)* | The provider contract. `ConformanceScenario`, `ConformanceOutcome`, the three scenario lists (`StreamingAgentModelConformance`, 16 scenarios; `ToolSchemaTranslatorConformance`, 6; `JsonSchemaTranslatorConformance`, 6), and the reference subjects `FakeConformanceModel`, `PassThroughToolSchemaTranslator` and `PassThroughJsonSchemaTranslator` |
| `NUnit/` | `StreamingAgentModelConformanceTests`, `ToolSchemaTranslatorConformanceTests`, `JsonSchemaTranslatorConformanceTests`, `ConformanceOutcomeAssert`, and the coverage gate `ConformanceCoverage` |
| `Planning/` | The operation seam contract: `OperationSeamConformance` and `PlanOperationSeam` |
| `Planning/NUnit/` | `OperationSeamConformanceTests` |

The reference subjects make the suite **self-validating**: `Ananke.Orchestration.Conformance.Tests`
runs them through every scenario, so a failure in an adapter's run points at the adapter rather than
at the fixture.

## Covering a provider adapter

Inherit the fixture in the adapter's test project and supply the adapter:

```csharp
[TestFixture]
public sealed class OpenAIConformanceTests : StreamingAgentModelConformanceTests
{
    protected override IStreamingAgentModel CreateModel() =>
        new OpenAIChatAgentModel(StubbedClient(), "gpt-4o-mini");
}
```

Wire the adapter to a **stub transport** rather than a live endpoint. Every SDK exposes a seam, and
each one is a different mechanism with a different name:

| SDK | Seam |
|---|---|
| OpenAI (`System.ClientModel`) | `OpenAIClientOptions.Transport` |
| Anthropic | `ClientOptions.HttpClient` — **not** `Handlers`, which rejects a pre-attached handler |
| Google | `ClientOptions.HttpClientFactory` — **not** `HttpOptions`, which carries no transport |

The stubs for all three are in `src/tests/Ananke.TestHelpers/ProviderStubs/`.

**The coverage gate.** `ConformanceCoverage.AssertEveryAdapterHasAFixture` finds every adapter
assembly beside the test assembly and fails when an adapter type has no fixture. Coverage is proved
by construction: each fixture is instantiated and its factory invoked, and the runtime type of what
comes back is what counts as covered. `Ananke.Orchestration.Tests` runs it as
`EveryShippedAdapter_IsSubjectToTheConformanceSuite`.

### Why `Skipped` is a returned value, not a thrown one

Several rules are conditional — *"**when** usage is reported, input and output must be positive"* —
and a provider that legitimately does not report usage has nothing to prove. Returning the
distinction keeps the scenarios independent of any runner.

**A skip reports as a skip, not as a pass.** A subject that skipped every conditional scenario would
otherwise read as fully green. `ConformanceOutcomeAssert` maps `Skipped` to `Assert.Ignore` for that
reason, with the reason string carried through. A failure without a captured assertion throws
`AssertionException` rather than calling `Assert.Fail`, because `Assert.Fail` also records into
NUnit's ambient result, so a caught one still fails the surrounding test.

## The operation seam

The plan tier's operation seam is an `OperationCatalog` and a `PlanCandidateApplier`. Half of what
the scenarios prove is the implementation and half is the tier's behaviour around it:

| | |
|---|---|
| the catalog | admits what you say it admits, refuses what you say it refuses, refuses an absent or unnamed operation, and names every operation in its legend |
| the applier | is reached exactly once by an admitted proposal, and never by one the catalog refused |
| the tier | never hands the applier a candidate from a step that reported not-done, nor a step that proposed nothing |
| a rejection | travels into the next attempt with its finding verbatim and its candidate intact |

The not-done scenario is the one worth knowing about. A step that proposes cannot have met its
contract yet — nothing has applied or checked its change — so a proposing step reports `Done: true`,
and a step reporting `false` has its candidate dropped before the gate and before the applier, with
no event and no retry to show for it.

```csharp
[TestFixture]
public sealed class MySeamTests : OperationSeamConformanceTests
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

`Admissible` and `Refused` are supplied because every domain names its own operations. `Refused`
must be refused **on shape** — an unknown name, or a known one at the wrong arity.
`OperationSeamConformance.ReferenceSeam()` is a subject the suite passes against.

## What conformance does and does not prove

> Conformance proves an adapter is **self-consistent with the contract**. It does not prove the
> adapter is **right about the service**.

A token under-count cannot surface from a stub, because the fixture author writes the same wrong
number the code already believes. Offline conformance is necessary and cheap; it is not sufficient.
