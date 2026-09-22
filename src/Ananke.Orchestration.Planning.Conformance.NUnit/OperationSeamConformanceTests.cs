using Ananke.Orchestration.Conformance;
using Ananke.Orchestration.Conformance.NUnit;
using Ananke.Orchestration.Planning.Conformance;

namespace Ananke.Orchestration.Planning.Conformance.NUnit;

/// <summary>
/// Conformance suite for an operation seam. Subclass in the consumer's test project and override
/// <see cref="CreateSeam"/>.
/// </summary>
/// <remarks>
/// The seam is rebuilt per scenario rather than shared, because several scenarios apply an operation
/// and one asserts on a freshly built world.
/// </remarks>
[TestFixture]
public abstract class OperationSeamConformanceTests
{
    /// <summary>Creates the subject under test. Called once per scenario.</summary>
    protected abstract PlanOperationSeam CreateSeam();

    /// <summary>The contract, enumerated for <c>[TestCaseSource]</c>.</summary>
    protected static IEnumerable<ConformanceScenario<PlanOperationSeam>> Scenarios =>
        OperationSeamConformance.Scenarios;

    /// <summary>Runs one scenario against <see cref="CreateSeam"/>'s subject.</summary>
    /// <param name="scenario">Supplied by NUnit from <see cref="Scenarios"/>.</param>
    [TestCaseSource(nameof(Scenarios))]
    public async Task Conforms(ConformanceScenario<PlanOperationSeam> scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var outcome = await scenario.RunAsync(CreateSeam(), TestContext.CurrentContext.CancellationToken);
        ConformanceOutcomeAssert.Apply(outcome);
    }
}
