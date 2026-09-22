using System.Runtime.CompilerServices;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Conformance;
using Shouldly;

namespace Ananke.Orchestration.Conformance.Tests;

/// <summary>
/// Covers the two outcomes <see cref="FakeConformanceModel"/> never produces.
/// </summary>
/// <remarks>
/// The reference subject passes every scenario, so a green self-validation run says nothing about
/// <see cref="ConformanceStatus.Skipped"/> or <see cref="ConformanceStatus.Failed"/> — and
/// <c>Skipped</c> is the whole reason the core needs a result type rather than plain delegates
///. These drive both through real scenarios rather than asserting on the enum.
/// </remarks>
[TestFixture]
public sealed class ConformanceCoreTests
{
    /// <summary>
    /// The four scenarios that used to call <c>Assert.Pass</c>, each named with the condition that
    /// makes it inapplicable. <see cref="MinimalModel"/> satisfies all four conditions at once.
    /// </summary>
    private static readonly string[] ConditionalScenarios =
    [
        "GenerateAsync_ToolCallIds_AreNonEmpty",                        // model replied with text
        "GenerateAsync_WhenUsageReported_InputAndOutputArePositive",    // no usage reported
        "GenerateStreamAsync_WhenUsageReported_CompletedChunkCarriesUsage",
        "GenerateAsync_WhenPartsPopulated_HoldsSomethingBeyondPlainText" // text-only response
    ];

    [Test]
    public async Task Scenario_WhenNotApplicableToSubject_ReturnsSkippedWithReason()
    {
        foreach (var name in ConditionalScenarios)
        {
            var scenario = StreamingAgentModelConformance.Scenarios.Single(s => s.Name == name);

            var outcome = await scenario.RunAsync(new MinimalModel(), TestContext.CurrentContext.CancellationToken);

            outcome.Status.ShouldBe(ConformanceStatus.Skipped,
                $"'{name}' does not apply to a model that reports no usage, parts or tool calls");
            outcome.Reason.ShouldNotBeNullOrWhiteSpace("A skip must say why it does not apply");
        }
    }

    [Test]
    public async Task Scenario_WhenSubjectViolatesContract_ReturnsFailedCarryingTheAssertion()
    {
        var scenario = StreamingAgentModelConformance.Scenarios
            .Single(s => s.Name == "GenerateAsync_TextResponse_IsNotNullOrEmpty");

        var outcome = await scenario.RunAsync(new EmptyModel(), TestContext.CurrentContext.CancellationToken);

        outcome.Status.ShouldBe(ConformanceStatus.Failed);
        // The original assertion must survive, so a runner can rethrow it with its own message
        // and stack rather than a summary of it.
        outcome.Exception.ShouldNotBeNull();
        outcome.Reason.ShouldNotBeNull().ShouldContain("Response must carry text or tool calls");
    }

    [Test]
    public void Scenarios_HaveUniqueNames()
    {
        // Names are the identity a runner reports and a suppression list would target.
        string[] names =
        [
            .. StreamingAgentModelConformance.Scenarios.Select(s => s.Name),
            .. ToolSchemaTranslatorConformance.Scenarios.Select(s => s.Name),
            .. JsonSchemaTranslatorConformance.Scenarios.Select(s => s.Name)
        ];

        names.Distinct().Count().ShouldBe(names.Length);
    }

    // ── Subjects ─────────────────────────────────────────────────────────────

    /// <summary>Conformant, but declares nothing optional — every conditional scenario skips.</summary>
    private sealed class MinimalModel : IStreamingAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = "ok" });

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new AgentStreamChunk { TextDelta = "ok" };
            yield return new AgentStreamChunk { CompletedResponse = new AgentResponse { Text = "ok" } };
        }
    }

    /// <summary>Non-conformant: returns neither text nor tool calls.</summary>
    private sealed class EmptyModel : IStreamingAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse());

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new AgentStreamChunk { CompletedResponse = new AgentResponse() };
        }
    }
}
