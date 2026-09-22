using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Conformance;

namespace Ananke.Orchestration.Conformance.NUnit;

/// <summary>
/// Conformance suite for <see cref="IStreamingAgentModel"/> implementations.
/// </summary>
/// <remarks>
/// Subclass in the provider's test project and override <see cref="CreateModel"/>; every scenario in
/// <see cref="StreamingAgentModelConformance.Scenarios"/> then runs against that implementation and
/// is reported as its own test case.
/// <code>
/// [TestFixture]
/// public sealed class OpenAIConformanceTests : StreamingAgentModelConformanceTests
/// {
///     protected override IStreamingAgentModel CreateModel() =>
///         new OpenAIChatAgentModel(StubbedClient(), "gpt-4o-mini");
/// }
/// </code>
/// Wire the adapter to a stub transport rather than a live endpoint — conformance is meant to run
/// offline and free.
/// </remarks>
[TestFixture]
public abstract class StreamingAgentModelConformanceTests
{
    /// <summary>Creates the subject under test. Called once per scenario.</summary>
    protected abstract IStreamingAgentModel CreateModel();

    /// <summary>The contract, enumerated for <c>[TestCaseSource]</c>.</summary>
    protected static IEnumerable<ConformanceScenario<IStreamingAgentModel>> Scenarios =>
        StreamingAgentModelConformance.Scenarios;

    /// <summary>Runs one scenario against <see cref="CreateModel"/>'s subject.</summary>
    /// <param name="scenario">Supplied by NUnit from <see cref="Scenarios"/>.</param>
    [TestCaseSource(nameof(Scenarios))]
    public async Task Conforms(ConformanceScenario<IStreamingAgentModel> scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var outcome = await scenario.RunAsync(CreateModel(), TestContext.CurrentContext.CancellationToken);
        ConformanceOutcomeAssert.Apply(outcome);
    }
}
