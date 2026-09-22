using System.ClientModel;
using System.ClientModel.Primitives;
using Amazon.Runtime;
using Amazon.Runtime.Credentials;
using Ananke.Orchestration.Anthropic;
using Ananke.Orchestration.OpenAI;
using Anthropic;
using Anthropic.Core;
using OpenAI;
using OpenAI.Chat;

namespace Ananke.Orchestration.Bedrock;

/// <summary>
/// Builds agent models that talk to Amazon Bedrock through its OpenAI-compatible and
/// Anthropic-native endpoints.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type contains no wire-format code, and that is deliberate.</b> Bedrock serves an OpenAI
/// Chat Completions API and an Anthropic Messages API, both of which Ananke already implements — so
/// what is left is authentication and endpoint construction. The factories here return
/// <see cref="OpenAIChatAgentModel"/> and <see cref="AnthropicAgentModel"/> instances, not a
/// Bedrock-specific model type.
/// </para>
/// <para>
/// <b>You supply the model id.</b> There is no Bedrock model catalogue here, by decision
///: Bedrock's ids carry region prefixes and version suffixes that churn, and a
/// catalogue would be a maintenance tax for no gain. The consequence is that Bedrock ids are outside
/// <c>ModelCatalog.Validate</c> and outside the <c>ANNKE001/2/3</c> analyzers — nothing will warn you
/// that a model was deprecated. See the package README.
/// </para>
/// <para>
/// <b>Not every Bedrock model is reachable this way.</b> Amazon's own Nova and Titan families speak
/// only the native Converse and Invoke APIs, which this package does not implement. See the README.
/// </para>
/// </remarks>
public static class BedrockAgentModel
{
    /// <summary>
    /// Creates a model that calls Bedrock's OpenAI-compatible Chat Completions API, authenticating
    /// with AWS SigV4.
    /// </summary>
    /// <param name="endpoint">Which Bedrock endpoint to call, and the region to sign for.</param>
    /// <param name="modelId">Native Bedrock model id, e.g. <c>"openai.gpt-oss-20b-1:0"</c>.</param>
    /// <param name="credentials">
    /// Credential source. Defaults to the standard AWS chain (environment, profile, SSO, container,
    /// instance metadata), so the usual AWS configuration applies with no extra wiring.
    /// </param>
    /// <param name="timeProvider">Clock used for the request timestamp.</param>
    public static OpenAIChatAgentModel CreateChatCompletions(
        BedrockEndpoint endpoint,
        string modelId,
        AWSCredentials? credentials = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        return ChatCompletionsWith(endpoint, modelId, SigV4(endpoint, credentials, timeProvider));
    }

    /// <summary>
    /// Creates a model that calls Bedrock's OpenAI-compatible Chat Completions API, authenticating
    /// with a Bedrock API key.
    /// </summary>
    /// <param name="endpoint">Which Bedrock endpoint to call.</param>
    /// <param name="modelId">Native Bedrock model id.</param>
    /// <param name="apiKey">Bedrock API key, as in <c>AWS_BEARER_TOKEN_BEDROCK</c>.</param>
    public static OpenAIChatAgentModel CreateChatCompletions(
        BedrockEndpoint endpoint, string modelId, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        return ChatCompletionsWith(endpoint, modelId, new BedrockApiKeyHandler(apiKey));
    }

    /// <summary>
    /// Creates a model that calls Bedrock's Anthropic-shaped Messages API, authenticating with
    /// AWS SigV4. Use this for Claude models, which do not serve the Chat Completions API on Bedrock.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Claude on Bedrock needs account provisioning that no code can do for you.</b> Anthropic
    /// requires first-time customers to submit use-case details once per AWS account — or once at
    /// the organisation's management account — before any Claude model can be invoked, and those
    /// details are shared with Anthropic. Until that is done, requests fail with a not-found or
    /// access-denied error that names the model, which reads like a wrong model id rather than a
    /// missing entitlement.
    /// </para>
    /// <para>
    /// <b>Model ids here are not the ones the Anthropic API uses.</b> Bedrock's newest Claude
    /// entries carry no version suffix (<c>anthropic.claude-sonnet-5</c>), while dated ones do
    /// (<c>anthropic.claude-haiku-4-5-20251001-v1:0</c>) — and dated ones generally require a
    /// cross-Region inference profile prefix such as <c>us.</c> for on-demand throughput, without
    /// which Bedrock refuses the call outright. Check
    /// <c>aws bedrock list-foundation-models</c> for what your account can actually reach.
    /// </para>
    /// <para>
    /// <b>This path is verified offline only.</b> The repository's live tests exercise
    /// non-Anthropic models, because the provisioning step above is not something a test suite can
    /// perform. See <c>BedrockLiveTests</c>.
    /// </para>
    /// </remarks>
    /// <param name="endpoint">Which Bedrock endpoint to call, and the region to sign for.</param>
    /// <param name="modelId">Native Bedrock model id for a Claude model.</param>
    /// <param name="credentials">Credential source. Defaults to the standard AWS chain.</param>
    /// <param name="timeProvider">Clock used for the request timestamp.</param>
    public static AnthropicAgentModel CreateMessages(
        BedrockEndpoint endpoint,
        string modelId,
        AWSCredentials? credentials = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        return MessagesWith(endpoint, modelId, SigV4(endpoint, credentials, timeProvider));
    }

    /// <summary>
    /// Creates a model that calls Bedrock's Anthropic-native Messages API, authenticating with a
    /// Bedrock API key.
    /// </summary>
    /// <param name="endpoint">Which Bedrock endpoint to call.</param>
    /// <param name="modelId">Native Bedrock model id for a Claude model.</param>
    /// <param name="apiKey">Bedrock API key.</param>
    public static AnthropicAgentModel CreateMessages(
        BedrockEndpoint endpoint, string modelId, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        return MessagesWith(endpoint, modelId, new BedrockApiKeyHandler(apiKey));
    }

    // ── composition ──────────────────────────────────────────────────

    internal static OpenAIChatAgentModel ChatCompletionsWith(
        BedrockEndpoint endpoint, string modelId, DelegatingHandler authHandler)
    {
        authHandler.InnerHandler ??= new HttpClientHandler();

        var options = new OpenAIClientOptions
        {
            Endpoint = endpoint.OpenAiCompatibleBaseUrl,
            Transport = new HttpClientPipelineTransport(new HttpClient(authHandler))
        };

        // The credential is never used — the handler owns authentication — but the OpenAI client
        // requires one, and refuses an empty string.
        return new OpenAIChatAgentModel(
            new ChatClient(modelId, new ApiKeyCredential("unused-handled-by-delegating-handler"), options));
    }

    internal static AnthropicAgentModel MessagesWith(
        BedrockEndpoint endpoint, string modelId, DelegatingHandler authHandler)
    {
        authHandler.InnerHandler ??= new HttpClientHandler();

        // Composed through HttpClient rather than ClientOptions.Handlers, to match the OpenAI path
        // above. The Anthropic client's Handlers setter rejects a handler whose InnerHandler is
        // already set, which would make the chain unsubstitutable — and the transport is the only
        // seam a test has, since neither endpoint can be reached without AWS credentials. Retry and
        // timeout are unaffected: the client applies both in its send loop, not in a handler.
        var options = new ClientOptions
        {
            BaseUrl = endpoint.AnthropicMessagesBaseUrl.ToString(),
            ApiKey = "unused-handled-by-delegating-handler",
            HttpClient = new HttpClient(authHandler)
        };

        return new AnthropicAgentModel(new AnthropicClient(options), modelId);
    }

    private static AwsSigV4Handler SigV4(
        BedrockEndpoint endpoint, AWSCredentials? credentials, TimeProvider? timeProvider) =>
        new(credentials ?? DefaultAWSCredentialsIdentityResolver.GetCredentials(),
            endpoint.Region,
            endpoint.SigningService,
            timeProvider);
}
