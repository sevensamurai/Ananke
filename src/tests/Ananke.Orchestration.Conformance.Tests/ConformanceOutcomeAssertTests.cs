using Ananke.Orchestration.Conformance;
using Ananke.Orchestration.Conformance.NUnit;
using Shouldly;

namespace Ananke.Orchestration.Conformance.Tests;

/// <summary>
/// Covers the NUnit shim's half of the D2 split: <c>Skipped</c> must be reported as NUnit's own
/// skip — not as a pass, or a fixture that skipped everything would read as fully green — and
/// <c>Failed</c> must surface the original assertion rather than a summary of it.
/// </summary>
[TestFixture]
public sealed class ConformanceOutcomeAssertTests
{
    [Test]
    public void Apply_Passed_DoesNothing()
    {
        Should.NotThrow(() => ConformanceOutcomeAssert.Apply(ConformanceOutcome.Passed));
    }

    [Test]
    public void Apply_Skipped_ReportsAsSkippedCarryingTheReason()
    {
        var ex = Should.Throw<IgnoreException>(
            () => ConformanceOutcomeAssert.Apply(ConformanceOutcome.Skip("provider reports no usage")));

        ex.Message.ShouldContain("provider reports no usage");
    }

    [Test]
    public void Apply_Failed_RethrowsTheOriginalAssertion()
    {
        var original = new ShouldAssertException("the adapter returned neither text nor tool calls");

        var thrown = Should.Throw<ShouldAssertException>(
            () => ConformanceOutcomeAssert.Apply(ConformanceOutcome.Fail(original.Message, original)));

        thrown.ShouldBeSameAs(original);
    }

    [Test]
    public void Apply_FailedWithoutAnException_StillFails()
    {
        var ex = Should.Throw<AssertionException>(
            () => ConformanceOutcomeAssert.Apply(ConformanceOutcome.Fail("no exception was captured")));

        ex.Message.ShouldContain("no exception was captured");
    }
}
