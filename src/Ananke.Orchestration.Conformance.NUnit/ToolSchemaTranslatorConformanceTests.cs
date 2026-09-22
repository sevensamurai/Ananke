using Ananke.Abstractions.Providers;
using Ananke.Orchestration.Conformance;

namespace Ananke.Orchestration.Conformance.NUnit;

/// <summary>
/// Conformance suite for <see cref="IToolSchemaTranslator"/> implementations. Subclass in the
/// provider's test project and override <see cref="CreateTranslator"/>.
/// </summary>
[TestFixture]
public abstract class ToolSchemaTranslatorConformanceTests
{
    /// <summary>Creates the subject under test. Called once per scenario.</summary>
    protected abstract IToolSchemaTranslator CreateTranslator();

    /// <summary>The contract, enumerated for <c>[TestCaseSource]</c>.</summary>
    protected static IEnumerable<ConformanceScenario<IToolSchemaTranslator>> Scenarios =>
        ToolSchemaTranslatorConformance.Scenarios;

    /// <summary>Runs one scenario against <see cref="CreateTranslator"/>'s subject.</summary>
    /// <param name="scenario">Supplied by NUnit from <see cref="Scenarios"/>.</param>
    [TestCaseSource(nameof(Scenarios))]
    public async Task Conforms(ConformanceScenario<IToolSchemaTranslator> scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var outcome = await scenario.RunAsync(CreateTranslator(), TestContext.CurrentContext.CancellationToken);
        ConformanceOutcomeAssert.Apply(outcome);
    }
}
