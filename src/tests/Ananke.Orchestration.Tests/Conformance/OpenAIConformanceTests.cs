using System.ClientModel;
using System.ClientModel.Primitives;
using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Providers;
using Ananke.Orchestration.Conformance.NUnit;
using Ananke.TestHelpers.ProviderStubs;
using Ananke.Orchestration.OpenAI;
using Ananke.Orchestration.OpenAI.Translators;
using OpenAI;
using OpenAI.Chat;

namespace Ananke.Orchestration.Tests.Conformance;

/// <summary>
/// The shipped <see cref="OpenAIChatAgentModel"/> under the conformance contract, driven through
/// <c>OpenAIClientOptions.Transport</c> — no network, no credentials, no cost.
/// </summary>
[TestFixture]
public sealed class OpenAIChatAgentModelConformanceTests : StreamingAgentModelConformanceTests
{
    protected override IStreamingAgentModel CreateModel()
    {
        var options = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(new HttpClient(new OpenAIStubHandler()))
        };

        return new OpenAIChatAgentModel(
            new ChatClient("gpt-4.1-mini", new ApiKeyCredential("stub-key"), options));
    }
}

/// <summary>The shipped <see cref="OpenAIToolSchemaTranslator"/> under the conformance contract.</summary>
[TestFixture]
public sealed class OpenAIToolSchemaTranslatorConformanceTests : ToolSchemaTranslatorConformanceTests
{
    protected override IToolSchemaTranslator CreateTranslator() => new OpenAIToolSchemaTranslator();
}
