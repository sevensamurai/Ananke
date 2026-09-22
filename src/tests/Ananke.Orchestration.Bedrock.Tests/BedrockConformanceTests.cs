using Amazon.Runtime;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Conformance.NUnit;
using Ananke.TestHelpers.ProviderStubs;
using Microsoft.Extensions.Time.Testing;

namespace Ananke.Orchestration.Bedrock.Tests;

/// <summary>
/// The Bedrock composition under the conformance contract, on both routes it serves.
/// </summary>
/// <remarks>
/// <para>
/// Bedrock ships no wire code of its own) — it is auth and endpoint
/// construction, and the requests are served by the shipped OpenAI and Anthropic adapters against
/// Bedrock's compatible endpoints. So what these fixtures prove is that the <b>composition</b>
/// conforms: the signing handler and base-URL construction must not disturb the contract the two
/// adapters satisfy on their own.
/// </para>
/// <para>
/// Both routes are covered because they are different code paths, not two names for one:
/// <c>ChatCompletionsWith</c> reaches <c>/openai/v1/chat/completions</c> through
/// <c>OpenAIClientOptions.Transport</c>, and <c>MessagesWith</c> reaches the Anthropic Messages
/// route through <c>ClientOptions.HttpClient</c>.
/// </para>
/// </remarks>
[TestFixture]
public sealed class BedrockChatCompletionsConformanceTests : StreamingAgentModelConformanceTests
{
    protected override IStreamingAgentModel CreateModel()
    {
        // SigV4 rather than the API-key handler: it is the path with something to disturb, since it
        // rewrites headers and hashes the body after the SDK has finished with the request.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero));
        var auth = new AwsSigV4Handler(
            new BasicAWSCredentials("AKID", "SECRET"), "us-east-1", "bedrock", clock)
        {
            InnerHandler = new OpenAIStubHandler()
        };

        return BedrockAgentModel.ChatCompletionsWith(
            BedrockEndpoint.Runtime("us-east-1"), "openai.gpt-oss-20b-1:0", auth);
    }
}

/// <summary>The Anthropic Messages route of the same composition.</summary>
[TestFixture]
public sealed class BedrockMessagesConformanceTests : StreamingAgentModelConformanceTests
{
    protected override IStreamingAgentModel CreateModel()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero));
        var auth = new AwsSigV4Handler(
            new BasicAWSCredentials("AKID", "SECRET"), "us-east-1", "bedrock", clock)
        {
            InnerHandler = new AnthropicStubHandler()
        };

        return BedrockAgentModel.MessagesWith(
            BedrockEndpoint.Runtime("us-east-1"), "anthropic.claude-sonnet-5-v1:0", auth);
    }
}
