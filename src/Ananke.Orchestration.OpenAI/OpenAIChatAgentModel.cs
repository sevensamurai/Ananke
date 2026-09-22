using System.ClientModel;
using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using System.Text;
using Ananke.Abstractions.Agents;
using OpenAI;
using OpenAI.Chat;

namespace Ananke.Orchestration.OpenAI;

public sealed class OpenAIChatAgentModel(ChatClient client) : IStreamingAgentModel
{
    private readonly ChatClient _client = client;

    /// <summary>
    /// Creates an <see cref="OpenAIChatAgentModel"/> from an API key, model name, and optional
    /// custom endpoint. Use this for OpenAI-compatible providers such as
    /// Ollama (<c>http://localhost:11434/v1</c>), LM Studio, vLLM, or Azure OpenAI.
    /// When <paramref name="endpoint"/> is <see langword="null"/>, the default OpenAI endpoint is used.
    /// </summary>
    /// <param name="apiKey">API key. For local servers that don't require auth, use any non-empty string (e.g. <c>"ollama"</c>).</param>
    /// <param name="model">Model name (e.g. <c>"llama3.2"</c>, <c>"gpt-4.1-mini"</c>).</param>
    /// <param name="endpoint">Custom API base URL, or <see langword="null"/> for the default OpenAI endpoint.</param>
    public static OpenAIChatAgentModel Create(string apiKey, string model, Uri? endpoint = null)
    {
        var credential = new ApiKeyCredential(apiKey);

        if (endpoint is not null)
        {
            var options = new OpenAIClientOptions { Endpoint = endpoint };
            return new OpenAIChatAgentModel(new ChatClient(model, credential, options));
        }

        return new OpenAIChatAgentModel(new ChatClient(model, credential));
    }

    /// <summary>
    /// Creates an <see cref="OpenAIChatAgentModel"/> that authenticates with a caller-supplied
    /// <see cref="AuthenticationPolicy"/> instead of a static API key. Use this for endpoints whose
    /// credential rotates or is issued by an identity provider — Microsoft Entra ID in front of
    /// Microsoft Foundry, AWS SigV4 in front of Amazon Bedrock, or a corporate SSO / token broker.
    /// </summary>
    /// <remarks>
    /// Ananke deliberately takes the policy rather than any vendor's credential type: nothing here
    /// references <c>Azure.Identity</c>, <c>AWSSDK</c>, or any other SDK, so an organisation can
    /// plug in an identity source Ananke has never heard of. SeeD2.
    /// </remarks>
    /// <param name="authenticationPolicy">
    /// Policy that authenticates each request. <see cref="BearerTokenPolicy"/> covers the common
    /// case of a rotating bearer token; any <see cref="AuthenticationPolicy"/> subclass works.
    /// </param>
    /// <param name="model">Model name, or the deployment name for endpoints that alias models.</param>
    /// <param name="endpoint">Custom API base URL, or <see langword="null"/> for the default OpenAI endpoint.</param>
    public static OpenAIChatAgentModel Create(
        AuthenticationPolicy authenticationPolicy, string model, Uri? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(authenticationPolicy);

#pragma warning disable OPENAI001 // ChatClient's AuthenticationPolicy constructors are experimental
        if (endpoint is not null)
        {
            var options = new OpenAIClientOptions { Endpoint = endpoint };
            return new OpenAIChatAgentModel(new ChatClient(model, authenticationPolicy, options));
        }

        return new OpenAIChatAgentModel(new ChatClient(model, authenticationPolicy));
#pragma warning restore OPENAI001
    }

    /// <summary>
    /// Creates an <see cref="OpenAIChatAgentModel"/> that authenticates with bearer tokens obtained
    /// from <paramref name="tokenProvider"/>. A convenience over the
    /// <see cref="Create(AuthenticationPolicy, string, Uri?)"/> overload for the common case; it
    /// wraps the provider in a <see cref="BearerTokenPolicy"/>.
    /// </summary>
    /// <param name="tokenProvider">
    /// Source of bearer tokens. Implement <see cref="AuthenticationTokenProvider"/> to bridge a
    /// corporate identity provider; vendor SDKs also supply implementations.
    /// </param>
    /// <param name="scope">Token scope requested from <paramref name="tokenProvider"/>.</param>
    /// <param name="model">Model name, or the deployment name for endpoints that alias models.</param>
    /// <param name="endpoint">Custom API base URL, or <see langword="null"/> for the default OpenAI endpoint.</param>
    public static OpenAIChatAgentModel Create(
        AuthenticationTokenProvider tokenProvider, string scope, string model, Uri? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        return Create(new BearerTokenPolicy(tokenProvider, scope), model, endpoint);
    }

    public async Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
    {
        var messages = MapMessages(request);
        var options = BuildOptions(request);

        ClientResult<ChatCompletion> result;

        try
        {
            result = await _client.CompleteChatAsync(messages, options, ct);
        }
        // The provider asked for something specific; give it that and try once more. See
        // SilenceReasoning for why this is a retry rather than a rule applied up front.
        catch (ClientResultException ex) when (AsksForSilentReasoning(ex))
        {
            SilenceReasoning(options);
            result = await _client.CompleteChatAsync(messages, options, ct);
        }

        var completion = result.Value;

        if (completion.FinishReason == ChatFinishReason.ToolCalls)
        {
            var toolCalls = completion.ToolCalls
                .Select(tc => new AgentToolCall(
                    tc.Id, tc.FunctionName, tc.FunctionArguments.ToString()))
                .ToList();

            return new AgentResponse
            {
                Text = completion.Content.FirstOrDefault()?.Text,
                ToolCalls = toolCalls,
                Usage = MapUsage(completion.Usage)
            };
        }

        return new AgentResponse
        {
            Text = completion.Content.FirstOrDefault()?.Text,
            Usage = MapUsage(completion.Usage)
        };
    }

    public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = MapMessages(request);
        var options = BuildOptions(request);

        var fullText = new StringBuilder();
        var toolCallBuilders = new Dictionary<int, (string id, string name, StringBuilder args)>();
        ChatTokenUsage? streamUsage = null;

        // Established separately, because the same rejection arrives here — on the first chunk — and
        // an iterator cannot yield from inside a try/catch. Same fix, same one retry.
        var updates = await EstablishStreamAsync(messages, options, ct).ConfigureAwait(false);

        await foreach (var update in updates)
        {
            // Usage is reported on the final streaming chunk
            if (update.Usage is not null)
                streamUsage = update.Usage;
            foreach (var part in update.ContentUpdate)
            {
                if (part.Text is { Length: > 0 })
                {
                    fullText.Append(part.Text);
                    yield return new AgentStreamChunk { TextDelta = part.Text };
                }
            }

#pragma warning disable OPENAI001 // OutputAudioUpdate is experimental
            if (update.OutputAudioUpdate is { } audioUpdate)
            {
                if (audioUpdate.AudioBytesUpdate is { Length: > 0 } audioBytes)
                {
                    yield return new AgentStreamChunk
                    {
                        AudioDelta = audioBytes.ToArray(),
                        AudioMimeType = "audio/pcm",
                        TranscriptDelta = audioUpdate.TranscriptUpdate
                    };
                }
                else if (audioUpdate.TranscriptUpdate is { Length: > 0 })
                {
                    fullText.Append(audioUpdate.TranscriptUpdate);
                    yield return new AgentStreamChunk
                    {
                        TextDelta = audioUpdate.TranscriptUpdate,
                        TranscriptDelta = audioUpdate.TranscriptUpdate
                    };
                }
            }
#pragma warning restore OPENAI001

            foreach (var tc in update.ToolCallUpdates)
            {
                if (tc.ToolCallId is not null)
                    toolCallBuilders[tc.Index] = (tc.ToolCallId, tc.FunctionName, new StringBuilder());

                if (tc.FunctionArgumentsUpdate is not null
                    && toolCallBuilders.TryGetValue(tc.Index, out var builder))
                    builder.args.Append(tc.FunctionArgumentsUpdate.ToString());
            }
        }

        var toolCalls = toolCallBuilders.Count > 0
            ? toolCallBuilders.Values
                .Select(tc => new AgentToolCall(tc.id, tc.name, tc.args.ToString()))
                .ToList()
            : null;

        yield return new AgentStreamChunk
        {
            CompletedResponse = new AgentResponse
            {
                Text = fullText.Length > 0 ? fullText.ToString() : null,
                ToolCalls = toolCalls,
                Usage = MapUsage(streamUsage)
            }
        };
    }

    private static TokenUsage? MapUsage(ChatTokenUsage? usage) =>
        usage is null ? null : new TokenUsage
        {
            InputTokens = usage.InputTokenCount,
            OutputTokens = usage.OutputTokenCount
        };

    private static List<ChatMessage> MapMessages(AgentRequest request)
    {
        var messages = new List<ChatMessage>();

        if (request.SystemPrompt is not null)
            messages.Add(ChatMessage.CreateSystemMessage(request.SystemPrompt));

        foreach (var msg in request.Messages)
        {
            switch (msg.Role)
            {
                case AgentRole.User:
                    if (msg.Parts is { Count: > 0 })
                    {
                        var contentParts = new List<ChatMessageContentPart>(msg.Parts.Count);
                        foreach (var part in msg.Parts)
                        {
                            switch (part)
                            {
                                case TextPart text:
                                    contentParts.Add(ChatMessageContentPart.CreateTextPart(text.Text));
                                    break;
                                case ImagePart image when image.Data is not null:
                                    contentParts.Add(ChatMessageContentPart.CreateImagePart(
                                        BinaryData.FromBytes(image.Data), image.MimeType));
                                    break;
                                case ImagePart image when image.Uri is not null:
                                    contentParts.Add(ChatMessageContentPart.CreateImagePart(image.Uri));
                                    break;
#pragma warning disable OPENAI001 // ChatInputAudioFormat is experimental
                                case AudioPart audio:
                                    contentParts.Add(ChatMessageContentPart.CreateInputAudioPart(
                                        BinaryData.FromBytes(audio.Data), MapAudioFormat(audio.MimeType)));
                                    break;
#pragma warning restore OPENAI001
#pragma warning disable OPENAI001 // ChatMessageContentPart.CreateFilePart is experimental
                                case DocumentPart doc when doc.Data is not null:
                                    contentParts.Add(ChatMessageContentPart.CreateFilePart(
                                        BinaryData.FromBytes(doc.Data), doc.MimeType, doc.Name ?? "document"));
                                    break;
#pragma warning restore OPENAI001
                                case DocumentPart:
                                    throw new NotSupportedException(
                                        "DocumentPart with only a Uri is not supported by the OpenAI adapter — " +
                                        "OpenAI's file content part requires either bytes or a pre-uploaded file ID, " +
                                        "and this adapter does not upload files on the caller's behalf. Supply Data instead.");
                                default:
                                    throw new NotSupportedException(
                                        $"{part.GetType().Name} is not supported by the OpenAI adapter's request content mapping.");
                            }
                        }
                        messages.Add(ChatMessage.CreateUserMessage(contentParts));
                    }
                    else
                    {
                        messages.Add(ChatMessage.CreateUserMessage(msg.Content!));
                    }
                    break;

                case AgentRole.Assistant when msg.ToolCalls is { Count: > 0 }:
                    messages.Add(new AssistantChatMessage(
                        msg.ToolCalls.Select(tc =>
                            ChatToolCall.CreateFunctionToolCall(
                                tc.Id, tc.FunctionName,
                                BinaryData.FromString(tc.Arguments)))));
                    break;

                case AgentRole.Assistant:
                    messages.Add(ChatMessage.CreateAssistantMessage(msg.Content ?? string.Empty));
                    break;

                case AgentRole.Tool:
                    messages.Add(ChatMessage.CreateToolMessage(msg.ToolCallId!, msg.Content!));
                    break;

                case AgentRole.System:
                    messages.Add(ChatMessage.CreateSystemMessage(msg.Content!));
                    break;
            }
        }

        return messages;
    }

    /// <summary>
    /// Starts a streaming call, retrying once if the provider rejects it for the one reason it tells
    /// us how to fix.
    /// </summary>
    /// <remarks>
    /// The rejection arrives while the first chunk is being fetched, which is inside the enumerator —
    /// so the call is made here, where a <c>try</c> is allowed, and the sequence is handed back for
    /// the iterator to walk.
    /// </remarks>
    private async Task<IAsyncEnumerable<StreamingChatCompletionUpdate>> EstablishStreamAsync(
        List<ChatMessage> messages, ChatCompletionOptions options, CancellationToken ct)
    {
        var updates = _client.CompleteChatStreamingAsync(messages, options, ct);
        var enumerator = updates.GetAsyncEnumerator(ct);

        try
        {
            if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                return Empty();

            return Prepend(enumerator.Current, enumerator);
        }
        catch (ClientResultException ex) when (AsksForSilentReasoning(ex))
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
            SilenceReasoning(options);
            return _client.CompleteChatStreamingAsync(messages, options, ct);
        }
    }

    private static async IAsyncEnumerable<StreamingChatCompletionUpdate> Empty()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    /// <summary>The chunk already pulled, followed by the rest of the stream.</summary>
    private static async IAsyncEnumerable<StreamingChatCompletionUpdate> Prepend(
        StreamingChatCompletionUpdate first, IAsyncEnumerator<StreamingChatCompletionUpdate> rest)
    {
        try
        {
            yield return first;

            while (await rest.MoveNextAsync().ConfigureAwait(false))
                yield return rest.Current;
        }
        finally
        {
            await rest.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Whether the service rejected the call because its default reasoning effort cannot be combined
    /// with function tools on this route, and named the fix.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Found live, and it applies to every current OpenAI model.</b> The whole 5.6 line answers
    /// HTTP 400 to a chat completion carrying function tools —
    /// <c>"Function tools with reasoning_effort are not supported for … To use function tools, use
    /// /v1/responses or set reasoning_effort to 'none'"</c> — while this adapter never sets the
    /// parameter at all: it is the service's own default that conflicts. Left alone, no current
    /// model can call a tool through this adapter.
    /// </para>
    /// <para>
    /// <b>Matched on the instruction, not on a model name.</b> A table of which models need this
    /// would be a second source of truth that goes stale, and this adapter also serves Ollama, vLLM
    /// and Azure deployments whose names it cannot interpret.
    /// </para>
    /// </remarks>
    private static bool AsksForSilentReasoning(ClientResultException ex) =>
        ex.Status == 400
        && ex.Message.Contains("reasoning_effort", StringComparison.OrdinalIgnoreCase)
        && ex.Message.Contains("'none'", StringComparison.Ordinal);

    /// <summary>
    /// Sets the effort the service just asked for, on the retry only.
    /// </summary>
    /// <remarks>
    /// <b>Not applied up front.</b> Sending it unconditionally is worse than sending nothing: models
    /// that are not reasoning models reject the parameter outright (<c>"Unrecognized request
    /// argument supplied: reasoning_effort"</c>), and older reasoning models reject this particular
    /// value (<c>"does not support 'none' with this model"</c>). Both were confirmed against the
    /// live API. So the default stays "say nothing", and only a model that asks gets an answer.
    /// </remarks>
    private static void SilenceReasoning(ChatCompletionOptions options)
    {
#pragma warning disable OPENAI001 // ReasoningEffortLevel is experimental in the SDK; the wire field is not
        options.ReasoningEffortLevel = new ChatReasoningEffortLevel("none");
#pragma warning restore OPENAI001
    }

    private static ChatCompletionOptions BuildOptions(AgentRequest request)
    {
        var options = new ChatCompletionOptions { StoredOutputEnabled = request.StoreCompletions };

        // Only when set: null must reach the provider as "absent", not as 0.
        if (request.Temperature is { } temperature)
            options.Temperature = (float)temperature;

        if (request.Tools is { Count: > 0 })
        {
            foreach (var tool in request.Tools)
                options.Tools.Add(ChatTool.CreateFunctionTool(
                    tool.Name, tool.Description,
                    BinaryData.FromString(tool.ParametersJsonSchema)));
        }

        if (request.ResponseFormat is not null)
        {
            options.ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                request.ResponseFormat.SchemaName,
                BinaryData.FromString(request.ResponseFormat.JsonSchema),
                jsonSchemaIsStrict: request.ResponseFormat.Strict);
        }

        // Only alongside a stored completion. The API rejects metadata outright unless `store` is
        // enabled — "The 'metadata' parameter is only allowed when 'store' is enabled" — and since
        // metadata exists to label a stored completion, there is nothing for it to label when
        // nothing is stored. Sending it regardless made every agent job's first call an HTTP 400,
        // because the engine attaches workflow attribution to every request and StoreCompletions
        // defaults to false on purpose.
        if (request.StoreCompletions && request.Metadata is not null)
        {
            foreach (var (key, value) in request.Metadata)
                options.Metadata[key] = value;
        }

        return options;
    }

#pragma warning disable OPENAI001 // ChatInputAudioFormat is experimental
    private static ChatInputAudioFormat MapAudioFormat(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "audio/wav" or "audio/x-wav" or "audio/wave" => ChatInputAudioFormat.Wav,
        "audio/mp3" or "audio/mpeg" => ChatInputAudioFormat.Mp3,
        _ => new ChatInputAudioFormat(mimeType.Replace("audio/", string.Empty))
    };
#pragma warning restore OPENAI001
}
