using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Ananke.Orchestration.Agents;

namespace Ananke.Orchestration.Anthropic;

public sealed class AnthropicAgentModel : IStreamingAgentModel
{
    private readonly AnthropicClient _client;
    private readonly string _model;
    private readonly int _maxTokens;

    public AnthropicAgentModel(AnthropicClient client, string model = "claude-sonnet-4-20250514", int maxTokens = 4096)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        _client = client;
        _model = model;
        _maxTokens = maxTokens;
    }

    /// <summary>
    /// Creates an <see cref="AnthropicAgentModel"/> from an API key and model name.
    /// Convenience factory for use with <c>ModelResolver</c> or standalone construction.
    /// </summary>
    /// <remarks>
    /// Sets the <c>ANTHROPIC_API_KEY</c> environment variable for the current process,
    /// which the Anthropic SDK reads during client construction.
    /// </remarks>
    public static AnthropicAgentModel Create(string apiKey, string model)
    {
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", apiKey);
        return new AnthropicAgentModel(new AnthropicClient(), model);
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

        await foreach (var evt in _client.Messages.CreateStreaming(parameters, ct))
        {
            if (evt.TryPickContentBlockStart(out var blockStart))
            {
                if (blockStart.ContentBlock.TryPickToolUse(out var toolUse))
                    toolCallBuilders[blockStart.Index] = (toolUse.ID, toolUse.Name, new StringBuilder());
            }
            else if (evt.TryPickContentBlockDelta(out var blockDelta))
            {
                if (blockDelta.Delta.TryPickText(out var textDelta))
                {
                    fullText.Append(textDelta.Text);
                    yield return new AgentStreamChunk { TextDelta = textDelta.Text };
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
                ToolCalls = toolCalls
            }
        };
    }

    private static AgentResponse MapMessage(Message message)
    {
        var toolCalls = new List<AgentToolCall>();
        string? text = null;

        foreach (var block in message.Content)
        {
            block.Match<object?>(
                t => { text = t.Text; return null; },
                _ => null,
                _ => null,
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

        return new AgentResponse
        {
            Text = text,
            ToolCalls = toolCalls.Count > 0 ? toolCalls : null
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
