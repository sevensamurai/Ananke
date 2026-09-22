using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Providers;
using Ananke.Orchestration.Conformance.NUnit;
using Ananke.TestHelpers.ProviderStubs;
using Ananke.Orchestration.Google;
using Ananke.Orchestration.Google.Translators;
using Google.GenAI;
using Google.GenAI.Types;

namespace Ananke.Orchestration.Tests.Conformance;

/// <summary>
/// The shipped <see cref="GeminiAgentModel"/> under the conformance contract, driven through
/// <c>ClientOptions.HttpClientFactory</c>.
/// </summary>
/// <remarks>
/// The seam is on <c>ClientOptions</c>, not <c>HttpOptions</c> — the latter carries only
/// <c>BaseUrl</c>, <c>ApiVersion</c>, <c>Headers</c> and <c>Timeout</c>, and no transport at all
/// (enterprise-surfaces plan §10.9).
/// </remarks>
[TestFixture]
public sealed class GeminiAgentModelConformanceTests : StreamingAgentModelConformanceTests
{
    protected override IStreamingAgentModel CreateModel()
    {
        var options = new ClientOptions
        {
            HttpClientFactory = () => new HttpClient(new GeminiStubHandler())
        };

        return new GeminiAgentModel(
            new Client(apiKey: "stub-key", clientOptions: options), "gemini-2.5-flash");
    }
}

/// <summary>The shipped <see cref="GeminiToolSchemaTranslator"/> under the conformance contract.</summary>
[TestFixture]
public sealed class GeminiToolSchemaTranslatorConformanceTests : ToolSchemaTranslatorConformanceTests
{
    protected override IToolSchemaTranslator CreateTranslator() => new GeminiToolSchemaTranslator();
}

/// <summary>The shipped <see cref="GeminiJsonSchemaTranslator"/> under the conformance contract.</summary>
[TestFixture]
public sealed class GeminiJsonSchemaTranslatorConformanceTests : JsonSchemaTranslatorConformanceTests
{
    protected override IJsonSchemaTranslator CreateTranslator() => new GeminiJsonSchemaTranslator();
}
