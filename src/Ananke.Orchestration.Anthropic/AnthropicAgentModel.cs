using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Anthropic;

public sealed class AnthropicAgentModel : IStreamingAgentModel
{
    private readonly AnthropicClient _client;
    private readonly string _model;
    private readonly int _maxTokens;

    public AnthropicAgentModel(AnthropicClient client, string model = Models.Anthropic.Sonnet5, int maxTokens = 4096)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        _client = client;
        _model = model;
        _maxTokens = maxTokens;
    }

    /// <summary>
    /// Creates an <see cref="AnthropicAgentModel"/> from an API key, model name, and optional
    /// custom endpoint. Use <paramref name="endpoint"/> for Anthropic-compatible providers such as
    /// Moonshot/Kimi, DeepSeek, or Zhipu/GLM. Convenience factory for use with <c>ModelResolver</c>
    /// or standalone construction.
    /// </summary>
    /// <param name="apiKey">API key for the target endpoint.</param>
    /// <param name="model">Model name (e.g. <c>"claude-sonnet-4-5"</c>).</param>
    /// <param name="endpoint">Custom API base URL, or <see langword="null"/> for the default Anthropic endpoint.</param>
    public static AnthropicAgentModel Create(string apiKey, string model, Uri? endpoint = null)
    {
        var options = endpoint is not null
            ? new ClientOptions { ApiKey = apiKey, BaseUrl = endpoint.ToString() }
            : new ClientOptions { ApiKey = apiKey };

        var client = new AnthropicClient(options);
        return new AnthropicAgentModel(client, model);
    }

    public async Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
    {
        var parameters = BuildParameters(request);
        var message = await _client.Messages.Create(parameters, ct);

        return MapMessage(message);
    }

    public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var parameters = BuildParameters(request);

        var fullText = new StringBuilder();
        var toolCallBuilders = new Dictionary<long, (string id, string name, StringBuilder args)>();

        // Reasoning arrives split across events: the block is declared at ContentBlockStart, its
        // text accumulates over thinking deltas, and its signature lands as a *separate* delta
        // afterwards (Anthropic 12.39.0). So a ReasoningPart is only well-formed once the stream
        // has moved past the block — which is why parts are assembled at the end rather than
        // emitted incrementally. See ADR-arch-029 D2/D3.
        var streamBlocks = new SortedDictionary<long, StreamBlock>();

        int streamInputTokens = 0, streamOutputTokens = 0;

        await foreach (var evt in _client.Messages.CreateStreaming(parameters, ct))
        {
            // Start event carries input token count
            if (evt.TryPickStart(out var msgStart) && msgStart.Message.Usage is not null)
                streamInputTokens = (int)msgStart.Message.Usage.InputTokens;

            // Delta event carries output token count
            if (evt.TryPickDelta(out var msgDelta) && msgDelta.Usage is not null)
                streamOutputTokens = (int)msgDelta.Usage.OutputTokens;

            if (evt.TryPickContentBlockStart(out var blockStart))
            {
                if (blockStart.ContentBlock.TryPickToolUse(out var toolUse))
                {
                    toolCallBuilders[blockStart.Index] = (toolUse.ID, toolUse.Name, new StringBuilder());
                }
                else if (blockStart.ContentBlock.TryPickThinking(out var thinkingBlock))
                {
                    var block = GetOrAddBlock(streamBlocks, blockStart.Index, StreamBlockKind.Reasoning);
                    block.Text.Append(thinkingBlock.Thinking);
                    block.Signature ??= thinkingBlock.Signature;
                }
                else if (blockStart.ContentBlock.TryPickRedactedThinking(out var redactedBlock))
                {
                    var block = GetOrAddBlock(streamBlocks, blockStart.Index, StreamBlockKind.RedactedReasoning);
                    block.Text.Append(redactedBlock.Data);
                }
            }
            else if (evt.TryPickContentBlockDelta(out var blockDelta))
            {
                if (blockDelta.Delta.TryPickText(out var textDelta))
                {
                    fullText.Append(textDelta.Text);
                    GetOrAddBlock(streamBlocks, blockDelta.Index, StreamBlockKind.Text).Text.Append(textDelta.Text);
                    yield return new AgentStreamChunk { TextDelta = textDelta.Text };
                }
                else if (blockDelta.Delta.TryPickThinking(out var thinkingDelta))
                {
                    GetOrAddBlock(streamBlocks, blockDelta.Index, StreamBlockKind.Reasoning)
                        .Text.Append(thinkingDelta.Thinking);
                }
                else if (blockDelta.Delta.TryPickSignature(out var signatureDelta))
                {
                    // Arrives after the thinking text it belongs to, keyed by the same block index.
                    GetOrAddBlock(streamBlocks, blockDelta.Index, StreamBlockKind.Reasoning)
                        .Signature = signatureDelta.Signature;
                }
                else if (blockDelta.Delta.TryPickInputJson(out var jsonDelta))
                {
                    if (toolCallBuilders.TryGetValue(blockDelta.Index, out var builder))
                        builder.args.Append(jsonDelta.PartialJson);
                }
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
                Parts = BuildStreamParts(streamBlocks),
                ToolCalls = toolCalls,
                Usage = (streamInputTokens > 0 || streamOutputTokens > 0)
                    ? new TokenUsage { InputTokens = streamInputTokens, OutputTokens = streamOutputTokens }
                    : null
            }
        };
    }

    private enum StreamBlockKind { Text, Reasoning, RedactedReasoning }

    private sealed class StreamBlock(StreamBlockKind kind)
    {
        public StreamBlockKind Kind { get; } = kind;
        public StringBuilder Text { get; } = new();
        public string? Signature { get; set; }
    }

    private static StreamBlock GetOrAddBlock(
        SortedDictionary<long, StreamBlock> blocks, long index, StreamBlockKind kind)
    {
        if (blocks.TryGetValue(index, out var existing))
            return existing;

        var created = new StreamBlock(kind);
        blocks[index] = created;
        return created;
    }

    /// <summary>
    /// Assembles the streamed blocks into <see cref="AgentResponse.Parts"/>, in the block order the
    /// provider sent them.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="null"/> for a purely textual response. That is ADR-arch-029 D1: parts
    /// are required only when the response carries something that is not a <see cref="TextPart"/>,
    /// so a plain text reply keeps letting <see cref="AgentResponse.Text"/> carry it rather than
    /// paying for a wrapper that adds nothing.
    /// </remarks>
    private static IReadOnlyList<ContentPart>? BuildStreamParts(SortedDictionary<long, StreamBlock> blocks)
    {
        if (blocks.Count == 0 || blocks.Values.All(b => b.Kind == StreamBlockKind.Text))
            return null;

        var parts = new List<ContentPart>();
        foreach (var block in blocks.Values)
        {
            var text = block.Text.ToString();
            switch (block.Kind)
            {
                case StreamBlockKind.Text when text.Length > 0:
                    parts.Add(new TextPart(text));
                    break;
                case StreamBlockKind.Reasoning:
                    parts.Add(new ReasoningPart(text) { Signature = block.Signature });
                    break;
                case StreamBlockKind.RedactedReasoning:
                    parts.Add(new ReasoningPart(text) { IsRedacted = true });
                    break;
            }
        }

        return parts.Count > 0 ? parts : null;
    }

    private static AgentResponse MapMessage(Message message)
    {
        var toolCalls = new List<AgentToolCall>();
        var textBuilder = new StringBuilder();
        var responseParts = new List<ContentPart>();

        foreach (var block in message.Content)
        {
            block.Match<object?>(
                t =>
                {
                    if (t.Text is { Length: > 0 })
                    {
                        if (textBuilder.Length > 0) textBuilder.Append('\n');
                        textBuilder.Append(t.Text);
                        responseParts.Add(new TextPart(t.Text));
                    }
                    return null;
                },
                t =>
                {
                    if (t.Thinking is { Length: > 0 })
                        responseParts.Add(new ReasoningPart(t.Thinking)
                        {
                            Signature = t.Signature is { Length: > 0 } ? t.Signature : null
                        });
                    return null;
                },
                t =>
                {
                    if (t.Data is { Length: > 0 })
                        responseParts.Add(new ReasoningPart(t.Data) { IsRedacted = true });
                    return null;
                },
                t => { toolCalls.Add(new AgentToolCall(t.ID, t.Name, ToJsonString(t.Input))); return null; },
                _ => null,
                _ => null,
                _ => null,
                _ => null,
                _ => null,
                _ => null,
                _ => null,
                _ => null);
        }

        var text = textBuilder.Length > 0 ? textBuilder.ToString() : null;

        return new AgentResponse
        {
            Text = text,
            Parts = responseParts.Count > 0 ? responseParts : null,
            ToolCalls = toolCalls.Count > 0 ? toolCalls : null,
            Usage = message.Usage is not null
                ? new TokenUsage
                {
                    InputTokens = (int)message.Usage.InputTokens,
                    OutputTokens = (int)message.Usage.OutputTokens
                }
                : null
        };
    }

    private MessageCreateParams BuildParameters(AgentRequest request)
    {
        string? system = null;

        if (request.SystemPrompt is not null && request.ResponseFormat is not null)
            system = $"{request.SystemPrompt}\n\nYou must respond with valid JSON matching this schema:\n{request.ResponseFormat.JsonSchema}";
        else if (request.SystemPrompt is not null)
            system = request.SystemPrompt;
        else if (request.ResponseFormat is not null)
            system = $"You must respond with valid JSON matching this schema:\n{request.ResponseFormat.JsonSchema}\nRespond ONLY with the JSON object, no other text.";

        IReadOnlyList<ToolUnion>? tools = request.Tools is { Count: > 0 }
            ? request.Tools.Select<AgentTool, ToolUnion>(t => new Tool
            {
                Name = t.Name,
                Description = t.Description,
                InputSchema = JsonSerializer.Deserialize<InputSchema>(t.ParametersJsonSchema)!
            }).ToList()
            : null;

        var parameters = new MessageCreateParams
        {
            Model = _model,
            MaxTokens = _maxTokens,
            Messages = MapMessages(request),
            Tools = tools
        };

        if (system is not null)
            parameters = parameters with { System = system };

        return parameters;
    }

    private static List<MessageParam> MapMessages(AgentRequest request)
    {
        var messages = new List<MessageParam>();

        foreach (var msg in request.Messages)
        {
            switch (msg.Role)
            {
                case AgentRole.User:
                    if (msg.Parts is { Count: > 0 })
                    {
                        var contentBlocks = new List<ContentBlockParam>(msg.Parts.Count);
                        foreach (var part in msg.Parts)
                        {
                            switch (part)
                            {
                                case TextPart text:
                                    contentBlocks.Add(new TextBlockParam(text.Text));
                                    break;
                                case ImagePart image when image.Data is not null:
                                    contentBlocks.Add(new ImageBlockParam(
                                        new Base64ImageSource
                                        {
                                            Data = Convert.ToBase64String(image.Data),
                                            MediaType = image.MimeType
                                        }));
                                    break;
                                case ImagePart image when image.Uri is not null:
                                    contentBlocks.Add(new ImageBlockParam(
                                        new UrlImageSource { Url = image.Uri.ToString() }));
                                    break;
                                case DocumentPart doc when doc.Data is not null && doc.MimeType == "application/pdf":
                                    // Base64PdfSource's media type is a fixed discriminator the SDK
                                    // sets from this constructor — it is not independently assignable.
                                    contentBlocks.Add(new DocumentBlockParam(
                                        new Base64PdfSource(Convert.ToBase64String(doc.Data))));
                                    break;
                                case DocumentPart doc when doc.Data is not null && doc.MimeType == "text/plain":
                                    contentBlocks.Add(new DocumentBlockParam(
                                        new PlainTextSource(Encoding.UTF8.GetString(doc.Data))));
                                    break;
                                case DocumentPart doc when doc.Uri is not null && doc.MimeType == "application/pdf":
                                    contentBlocks.Add(new DocumentBlockParam(
                                        new UrlPdfSource(doc.Uri.ToString())));
                                    break;
                                case DocumentPart doc:
                                    throw new NotSupportedException(
                                        $"DocumentPart with MIME type '{doc.MimeType}' is not supported by the Anthropic " +
                                        "adapter — only 'application/pdf' and 'text/plain' are supported (a URI source is " +
                                        "supported for PDF only).");
                                case ReasoningPart reasoning when reasoning.IsRedacted:
                                    contentBlocks.Add(new RedactedThinkingBlockParam(reasoning.Text));
                                    break;
                                case ReasoningPart reasoning when reasoning.Signature is { Length: > 0 }:
                                    contentBlocks.Add(new ThinkingBlockParam
                                    {
                                        Thinking = reasoning.Text,
                                        Signature = reasoning.Signature
                                    });
                                    break;
                                case ReasoningPart:
                                    // Anthropic rejects a thinking block sent back without a valid signature —
                                    // fail loudly here with a clear local exception instead of an opaque 400.
                                    throw new NotSupportedException(
                                        "ReasoningPart without a Signature is not supported by the Anthropic adapter " +
                                        "— Anthropic requires a valid signature to echo a thinking block back on a " +
                                        "later turn. Only signed, non-redacted ReasoningPart instances (or redacted " +
                                        "ones, which carry no signature) can be sent.");
                                default:
                                    throw new NotSupportedException(
                                        $"{part.GetType().Name} is not supported by the Anthropic adapter's request content mapping.");
                            }
                        }
                        messages.Add(new MessageParam
                        {
                            Role = "user",
                            Content = contentBlocks
                        });
                    }
                    else
                    {
                        messages.Add(new MessageParam
                        {
                            Role = "user",
                            Content = msg.Content!
                        });
                    }
                    break;

                case AgentRole.Assistant when msg.ToolCalls is { Count: > 0 }:
                    var toolUseBlocks = msg.ToolCalls.Select<AgentToolCall, ContentBlockParam>(tc =>
                        new ToolUseBlockParam
                        {
                            ID = tc.Id,
                            Name = tc.FunctionName,
                            Input = ParseJsonElementDict(tc.Arguments)
                        }).ToList();
                    messages.Add(new MessageParam
                    {
                        Role = "assistant",
                        Content = toolUseBlocks
                    });
                    break;

                case AgentRole.Assistant:
                    messages.Add(new MessageParam
                    {
                        Role = "assistant",
                        Content = msg.Content ?? string.Empty
                    });
                    break;

                case AgentRole.Tool:
                    messages.Add(new MessageParam
                    {
                        Role = "user",
                        Content = new List<ContentBlockParam>
                        {
                            new ToolResultBlockParam
                            {
                                ToolUseID = msg.ToolCallId!,
                                Content = msg.Content!
                            }
                        }
                    });
                    break;
            }
        }

        return messages;
    }

    private static IReadOnlyDictionary<string, JsonElement> ParseJsonElementDict(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    private static string ToJsonString(object? input)
    {
        if (input is null) return "{}";
        if (input is JsonElement el) return el.GetRawText();
        return JsonSerializer.Serialize(input);
    }
}
