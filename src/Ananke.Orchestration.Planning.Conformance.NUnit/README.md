# Ananke.Orchestration.Planning.Conformance.NUnit

NUnit fixtures over `Ananke.Orchestration.Planning.Conformance`. Inherit one, supply the seam, and
every scenario runs as its own test case.

```csharp
[TestFixture]
public sealed class MySeamTests : OperationSeamConformanceTests
{
    protected override PlanOperationSeam CreateSeam() => /* your catalog and applier */;
}
```

A library rather than a test project: it carries NUnit but neither Microsoft.NET.Test.Sdk nor
NUnit3TestAdapter, so referencing it never turns a consumer's project into something the SDK tries
to run.

On any other runner, use the contract package directly — the scenarios are named delegates and the
assertions are Shouldly, which needs no test framework.
