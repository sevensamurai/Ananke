# Ananke.Orchestration.Conformance.NUnit

NUnit fixtures over the [conformance contract](../Ananke.Orchestration.Conformance/README.md).
Inherit one, supply the adapter, and every scenario runs as its own test case.

```csharp
[TestFixture]
public sealed class OpenAIConformanceTests : StreamingAgentModelConformanceTests
{
    protected override IStreamingAgentModel CreateModel() =>
        new OpenAIChatAgentModel(StubbedClient(), "gpt-4o-mini");
}
```

There are three: `StreamingAgentModelConformanceTests`, `ToolSchemaTranslatorConformanceTests`
and `JsonSchemaTranslatorConformanceTests`.

## Why this is a separate package

The contract itself must not be tied to a runner. `Ananke.Orchestration.Conformance`
holds the scenarios as named delegates and asserts with Shouldly, which throws its own exception type
and needs no framework; everything NUnit-shaped lives here. A consumer on xUnit, MSTest or TUnit takes
the core, writes about ten lines of glue, and never acquires an NUnit dependency they did not choose.

That glue is all this package is:

| | |
|---|---|
| `[TestCaseSource]` over `Scenarios` | so each rule reports as its own test case rather than one aggregate |
| `ConformanceOutcomeAssert` | maps `Passed` / `Skipped` / `Failed` onto NUnit's vocabulary |

`ConformanceOutcomeAssert` is public so you can use the mapping from a hand-written fixture without
inheriting one of the three.

## Two details that are deliberate

**`Skipped` becomes `Assert.Ignore`, so a skip reports as a skip.** Several rules are conditional —
*"**when** usage is reported…"* — and a provider that legitimately does not report usage has nothing
to prove. The fixtures this grew from wrote that as `Assert.Pass`, which reports as a *pass*: a
subclass that skipped every conditional scenario would read as fully green, which is the exact
failure this package exists to prevent one level up.D2 says *"other runners map it to
their own skip"* — NUnit's own skip is `Assert.Ignore`, so this follows D2's principle over its
letter. The reason string is carried through, so the runner shows *why*.

**A failure without a captured assertion throws `AssertionException` rather than calling
`Assert.Fail`.** `Assert.Fail` records into NUnit's ambient result as well as throwing, so a caught
`Assert.Fail` still fails the surrounding test. Throwing the same exception type keeps the mapping
testable and self-contained.

## This is a library, not a test project

It references NUnit but neither Microsoft.NET.Test.Sdk nor NUnit3TestAdapter, so referencing it does
not turn a consumer's project into something the SDK tries to run. Discovery happens in *your*
test project, against *your* concrete subclasses.
