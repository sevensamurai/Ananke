using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Providers;
using Ananke.Orchestration.Anthropic;
using Ananke.Orchestration.Anthropic.Translators;
using Ananke.Orchestration.Conformance.NUnit;
using Ananke.TestHelpers.ProviderStubs;
using Anthropic;
using Anthropic.Core;

namespace Ananke.Orchestration.Tests.Conformance;

/// <summary>
/// The shipped <see cref="AnthropicAgentModel"/> under the conformance contract, driven through
/// <c>ClientOptions.HttpClient</c>.
/// </summary>
/// <remarks>
/// The seam is <c>HttpClient</c> and not <c>Handlers</c>: the latter rejects a handler whose
/// <c>InnerHandler</c> is already set, which makes a stub chain unsubstitutable. That was found by a
/// failing test on 2026-08-20, not read from the SDK's docs — see the enterprise-surfaces plan §10.7.
/// </remarks>
[TestFixture]
public sealed class AnthropicAgentModelConformanceTests : StreamingAgentModelConformanceTests
{
    protected override IStreamingAgentModel CreateModel()
    {
        var options = new ClientOptions
        {
            ApiKey = "stub-key",
            HttpClient = new HttpClient(new AnthropicStubHandler())
        };

        return new AnthropicAgentModel(new AnthropicClient(options), Models.Anthropic.Sonnet5);
    }
}

/// <summary>The shipped <see cref="AnthropicToolSchemaTranslator"/> under the conformance contract.</summary>
[TestFixture]
public sealed class AnthropicToolSchemaTranslatorConformanceTests : ToolSchemaTranslatorConformanceTests
{
    protected override IToolSchemaTranslator CreateTranslator() => new AnthropicToolSchemaTranslator();
}
