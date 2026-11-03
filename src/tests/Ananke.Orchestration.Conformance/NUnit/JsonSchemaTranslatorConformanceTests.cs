using Ananke.Abstractions.Providers;
using Ananke.Orchestration.Conformance;

namespace Ananke.Orchestration.Conformance.NUnit;

/// <summary>
/// Conformance suite for <see cref="IJsonSchemaTranslator"/> implementations. Subclass in the
/// provider's test project and override <see cref="CreateTranslator"/>.
/// </summary>
[TestFixture]
public abstract class JsonSchemaTranslatorConformanceTests
{
    /// <summary>Creates the subject under test. Called once per scenario.</summary>
    protected abstract IJsonSchemaTranslator CreateTranslator();

    /// <summary>The contract, enumerated for <c>[TestCaseSource]</c>.</summary>
    protected static IEnumerable<ConformanceScenario<IJsonSchemaTranslator>> Scenarios =>
        JsonSchemaTranslatorConformance.Scenarios;

    /// <summary>Runs one scenario against <see cref="CreateTranslator"/>'s subject.</summary>
    /// <param name="scenario">Supplied by NUnit from <see cref="Scenarios"/>.</param>
    [TestCaseSource(nameof(Scenarios))]
    public async Task Conforms(ConformanceScenario<IJsonSchemaTranslator> scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var outcome = await scenario.RunAsync(CreateTranslator(), TestContext.CurrentContext.CancellationToken);
        ConformanceOutcomeAssert.Apply(outcome);
    }
}
