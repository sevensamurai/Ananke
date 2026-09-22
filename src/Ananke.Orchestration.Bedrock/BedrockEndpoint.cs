namespace Ananke.Orchestration.Bedrock;

/// <summary>
/// A Bedrock inference endpoint: which host to call, and in which region requests are signed.
/// </summary>
/// <remarks>
/// <para>
/// Bedrock exposes two endpoints, and they are <b>not</b> interchangeable —
/// <see cref="Runtime"/> carries Guardrails, intelligent prompt routing, cross-Region inference and
/// IAM-principal usage attribution; <see cref="Mantle"/> carries server-side tool use, asynchronous
/// inference, Projects/Workspaces, and hosts some models the runtime endpoint does not. AWS's own
/// guidance is to use both and choose per use case, so this type makes the choice explicit rather
/// than burying it in a base URL.
/// </para>
/// <para>
/// <see cref="Custom"/> exists because teams calling Bedrock from a VPC reach it through a
/// PrivateLink interface endpoint whose hostname does not match either template. An adapter that
/// could only build the public hostnames would fail exactly the enterprise case it is for.
/// </para>
/// </remarks>
public sealed record BedrockEndpoint
{
    private BedrockEndpoint(Uri baseUri, string region)
    {
        BaseUri = baseUri;
        Region = region;
    }

    /// <summary>Base URI of the endpoint, without an API path.</summary>
    public Uri BaseUri { get; }

    /// <summary>AWS region used to build the SigV4 credential scope.</summary>
    public string Region { get; }

    /// <summary>Service name used in the SigV4 credential scope.</summary>
    public string SigningService => "bedrock";

    /// <summary>
    /// The <c>bedrock-runtime</c> endpoint — <c>https://bedrock-runtime.{region}.amazonaws.com</c>.
    /// AWS recommends this one for new applications.
    /// </summary>
    /// <param name="region">AWS region, e.g. <c>"us-west-2"</c>.</param>
    public static BedrockEndpoint Runtime(string region)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        return new(new Uri($"https://bedrock-runtime.{region}.amazonaws.com"), region);
    }

    /// <summary>
    /// The <c>bedrock-mantle</c> endpoint — <c>https://bedrock-mantle.{region}.api.aws</c>.
    /// Choose it for server-side tool use, asynchronous inference, or a model only it hosts.
    /// </summary>
    /// <param name="region">AWS region, e.g. <c>"us-west-2"</c>.</param>
    public static BedrockEndpoint Mantle(string region)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        return new(new Uri($"https://bedrock-mantle.{region}.api.aws"), region);
    }

    /// <summary>
    /// An explicit endpoint URI — for a PrivateLink interface endpoint, a proxy, or a test double.
    /// The region is still required because it forms part of the SigV4 credential scope and cannot
    /// be inferred from a private hostname.
    /// </summary>
    /// <param name="baseUri">Absolute base URI, without an API path.</param>
    /// <param name="region">AWS region used for signing.</param>
    public static BedrockEndpoint Custom(Uri baseUri, string region)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(region);

        if (!baseUri.IsAbsoluteUri)
            throw new ArgumentException("Base URI must be absolute.", nameof(baseUri));

        return new(baseUri, region);
    }

    /// <summary>
    /// Base URL for Bedrock's OpenAI-compatible Chat Completions API — the <c>/openai/v1/</c> path
    /// on this endpoint.
    /// </summary>
    public Uri OpenAiCompatibleBaseUrl => Combine("openai/v1/");

    /// <summary>
    /// Base URL for Bedrock's Anthropic-shaped Messages API. The Anthropic client appends
    /// <c>v1/messages</c>, giving <c>/anthropic/v1/messages</c> — mirroring the OpenAI-compatible
    /// route rather than sitting at the endpoint root.
    /// </summary>
    /// <remarks>
    /// <b>The endpoint root does not serve this API, and fails silently if you assume it does.</b>
    /// A POST to <c>/v1/messages</c> returns <b>HTTP 200</b> carrying
    /// <c>{"Output":{"__type":"com.amazon.coral.service#UnknownOperationException"}}</c> — Bedrock's
    /// front door answering for an operation it does not recognise. Nothing about the status code
    /// says "wrong route"; the Anthropic client parses the body as a message and throws
    /// <c>'content' cannot be absent</c>, which reads like a response-mapping bug. The two routes
    /// are told apart by the shape of their errors: <c>/anthropic/v1/</c> returns Anthropic's own
    /// <c>{"type":"error",...}</c> envelope.
    /// </remarks>
    public Uri AnthropicMessagesBaseUrl => Combine("anthropic/v1/");

    private Uri Combine(string relativePath)
    {
        var root = BaseUri.AbsoluteUri.TrimEnd('/');
        return relativePath.Length == 0 ? new Uri(root + "/") : new Uri($"{root}/{relativePath}");
    }
}
