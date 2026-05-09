using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Ananke.Abstractions.Agents;
using Google.GenAI;
using Google.GenAI.Types;

namespace Ananke.Orchestration.Google;

/// <summary>
/// Google Gemini implementation of <see cref="IStreamingAgentModel"/>.
/// Wraps the official <c>Google.GenAI</c> SDK and supports both the
/// Gemini Developer API (API key) and Gemini Enterprise Agent Platform (project + location + ADC).
/// </summary>
public sealed class GeminiAgentModel : IStreamingAgentModel
{
    private readonly Client _client;
    private readonly string _model;

    /// <summary>
    /// Creates a <see cref="GeminiAgentModel"/> from an existing <see cref="Client"/>.
    /// </summary>
    /// <param name="client">A configured Google GenAI client.</param>
    /// <param name="model">Model name (e.g. <c>"gemini-2.5-flash"</c>).</param>
    public GeminiAgentModel(Client client, string model)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        _client = client;
        _model = model;
    }

    /// <summary>
    /// Creates a <see cref="GeminiAgentModel"/> for the Gemini Developer API.
    /// </summary>
    /// <param name="apiKey">Google AI API key.</param>
    /// <param name="model">Model name (e.g. <c>"gemini-2.5-flash"</c>).</param>
    public static GeminiAgentModel Create(string apiKey, string model) =>
        new(new Client(apiKey: apiKey), model);

    /// <summary>
    /// Creates a <see cref="GeminiAgentModel"/> for Gemini Enterprise Agent Platform using
    /// Application Default Credentials.
    /// </summary>
    /// <param name="project">Google Cloud project ID.</param>
    /// <param name="location">Google Cloud region (e.g. <c>"us-central1"</c>).</param>
    /// <param name="model">Model name (e.g. <c>"gemini-2.5-flash"</c>).</param>
    public static GeminiAgentModel CreateVertexAI(string project, string location, string model) =>
        new(new Client(project: project, location: location, vertexAI: true), model);

    /// <inheritdoc />
    public async Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
    {
        var config = BuildConfig(request);
        var contents = MapContents(request.Messages);

        var response = await _client.Models.GenerateContentAsync(
            model: _model,
            contents: contents,
            config: config,
            cancellationToken: ct);

        return MapResponse(response);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var config = BuildConfig(request);
        var contents = MapContents(request.Messages);

        var fullText = new StringBuilder();
        var toolCalls = new List<AgentToolCall>();
        var responseParts = new List<ContentPart>();
        TokenUsage? streamUsage = null;

        await foreach (var chunk in _client.Models.GenerateContentStreamAsync(
            model: _model,
            contents: contents,
            config: config,
            cancellationToken: ct))
        {
            // UsageMetadata is reported on the final streaming chunk
            if (chunk.UsageMetadata is not null)
                streamUsage = new TokenUsage
                {
                    InputTokens = chunk.UsageMetadata.PromptTokenCount ?? 0,
                    OutputTokens = chunk.UsageMetadata.CandidatesTokenCount ?? 0
                };

            if (chunk.Candidates is not { Count: > 0 })
                continue;

            var parts = chunk.Candidates[0].Content?.Parts;
            if (parts is null)
                continue;

            foreach (var part in parts)
            {
                if (part.Text is { Length: > 0 })
                {
                    fullText.Append(part.Text);
                    yield return new AgentStreamChunk { TextDelta = part.Text };
                }

                if (part.InlineData is { Data: not null, MimeType: not null } blob &&
                    blob.MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                {
                    responseParts.Add(new AudioPart(blob.Data, blob.MimeType));
                    yield return new AgentStreamChunk
                    {
                        AudioDelta = blob.Data,
                        AudioMimeType = blob.MimeType
                    };
                }

                if (part.FunctionCall is not null)
                {
                    toolCalls.Add(new AgentToolCall(
                        Guid.NewGuid().ToString("N"),
                        part.FunctionCall.Name!,
                        SerializeArgs(part.FunctionCall.Args)));
                }
            }
        }

        if (fullText.Length > 0)
            responseParts.Insert(0, new TextPart(fullText.ToString()));

        yield return new AgentStreamChunk
        {
            CompletedResponse = new AgentResponse
            {
                Text = fullText.Length > 0 ? fullText.ToString() : null,
                Parts = responseParts.Count > 0 ? responseParts : null,
                ToolCalls = toolCalls.Count > 0 ? toolCalls : null,
                Usage = streamUsage
            }
        };
    }

    private GenerateContentConfig BuildConfig(AgentRequest request)
    {
        var config = new GenerateContentConfig();

        if (request.SystemPrompt is not null)
        {
            config.SystemInstruction = new Content
            {
                Parts = [new Part { Text = request.SystemPrompt }]
            };
        }

        if (request.Tools is { Count: > 0 })
        {
            var declarations = request.Tools.Select(t => new FunctionDeclaration
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = JsonSchemaConverter.Convert(t.ParametersJsonSchema)
            }).ToList();

            config.Tools = [new Tool { FunctionDeclarations = declarations }];
        }

        if (request.ResponseFormat is not null)
        {
            config.ResponseMimeType = "application/json";
            config.ResponseSchema = JsonSchemaConverter.Convert(request.ResponseFormat.JsonSchema);
        }

        if (request.Metadata is not null &&
            request.Metadata.TryGetValue("response_modalities", out var modalities))
        {
            config.ResponseModalities = modalities
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(m => m.ToUpperInvariant())
                .ToList();
        }

        if (request.Metadata is not null &&
            request.Metadata.TryGetValue("speech_voice", out var voice))
        {
            config.SpeechConfig = new SpeechConfig
            {
                VoiceConfig = new VoiceConfig { PrebuiltVoiceConfig = new PrebuiltVoiceConfig { VoiceName = voice } }
            };
        }

        return config;
    }

    private static List<Content> MapContents(IReadOnlyList<AgentMessage> messages)
    {
        var contents = new List<Content>();

        foreach (var msg in messages)
        {
            switch (msg.Role)
            {
                case AgentRole.User:
                    contents.Add(new Content
                    {
                        Role = "user",
                        Parts = MapParts(msg)
                    });
                    break;

                case AgentRole.Assistant when msg.ToolCalls is { Count: > 0 }:
                    var functionCallParts = msg.ToolCalls.Select(tc => new Part
                    {
                        FunctionCall = new FunctionCall
                        {
                            Name = tc.FunctionName,
                            Args = DeserializeArgs(tc.Arguments)
                        }
                    }).ToList();

                    // Include text part if present alongside tool calls
                    if (msg.Content is { Length: > 0 })
                        functionCallParts.Insert(0, new Part { Text = msg.Content });

                    contents.Add(new Content
                    {
                        Role = "model",
                        Parts = functionCallParts
                    });
                    break;

                case AgentRole.Assistant:
                    contents.Add(new Content
                    {
                        Role = "model",
                        Parts = [new Part { Text = msg.Content ?? string.Empty }]
                    });
                    break;

                case AgentRole.Tool:
                    // Gemini expects FunctionResponse in a "user" role content.
                    // Find the function name from the tool call ID by scanning prior messages.
                    var functionName = FindFunctionName(messages, msg.ToolCallId!);
                    contents.Add(new Content
                    {
                        Role = "user",
                        Parts =
                        [
                            new Part
                            {
                                FunctionResponse = new FunctionResponse
                                {
                                    Name = functionName,
                                    Response = DeserializeToDict(msg.Content!)
                                }
                            }
                        ]
                    });
                    break;

                case AgentRole.System:
                    // System messages are handled via config.SystemInstruction.
                    // If one appears in the message list, treat it as a user message.
                    contents.Add(new Content
                    {
                        Role = "user",
                        Parts = MapParts(msg)
                    });
                    break;
            }
        }

        return contents;
    }

    private static List<Part> MapParts(AgentMessage msg)
    {
        if (msg.Parts is not { Count: > 0 })
            return [new Part { Text = msg.Content ?? string.Empty }];

        var parts = new List<Part>(msg.Parts.Count);
        foreach (var contentPart in msg.Parts)
        {
            switch (contentPart)
            {
                case TextPart text:
                    parts.Add(new Part { Text = text.Text });
                    break;
                case AudioPart audio:
                    parts.Add(new Part { InlineData = new Blob { MimeType = audio.MimeType, Data = audio.Data } });
                    break;
                case ImagePart image when image.Data is not null:
                    parts.Add(new Part { InlineData = new Blob { MimeType = image.MimeType, Data = image.Data } });
                    break;
                case ImagePart image when image.Uri is not null:
                    parts.Add(new Part { FileData = new FileData { MimeType = image.MimeType, FileUri = image.Uri.ToString() } });
                    break;
            }
        }

        return parts;
    }

    private static AgentResponse MapResponse(GenerateContentResponse response)
    {
        string? text = null;
        var toolCalls = new List<AgentToolCall>();
        var responseParts = new List<ContentPart>();

        if (response.Candidates is { Count: > 0 })
        {
            var parts = response.Candidates[0].Content?.Parts;
            if (parts is not null)
            {
                foreach (var part in parts)
                {
                    if (part.Text is { Length: > 0 })
                    {
                        text = part.Text;
                        responseParts.Add(new TextPart(part.Text));
                    }

                    if (part.InlineData is { Data: not null, MimeType: not null } blob &&
                        blob.MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                    {
                        responseParts.Add(new AudioPart(blob.Data, blob.MimeType));
                    }

                    if (part.FunctionCall is not null)
                    {
                        toolCalls.Add(new AgentToolCall(
                            Guid.NewGuid().ToString("N"),
                            part.FunctionCall.Name!,
                            SerializeArgs(part.FunctionCall.Args)));
                    }
                }
            }
        }

        return new AgentResponse
        {
            Text = text,
            Parts = responseParts.Count > 0 ? responseParts : null,
            ToolCalls = toolCalls.Count > 0 ? toolCalls : null,
            Usage = response.UsageMetadata is not null
                ? new TokenUsage
                {
                    InputTokens = response.UsageMetadata.PromptTokenCount ?? 0,
                    OutputTokens = response.UsageMetadata.CandidatesTokenCount ?? 0
                }
                : null
        };
    }

    /// <summary>
    /// Resolves a function name from a synthetic tool-call ID by scanning the message history.
    /// Gemini correlates tool results by function name, not by ID.
    /// </summary>
    private static string FindFunctionName(IReadOnlyList<AgentMessage> messages, string toolCallId)
    {
        foreach (var msg in messages)
        {
            if (msg.ToolCalls is null) continue;
            foreach (var tc in msg.ToolCalls)
            {
                if (tc.Id == toolCallId)
                    return tc.FunctionName;
            }
        }

        // Fallback: use the ID itself (which may already be the function name in some flows)
        return toolCallId;
    }

    private static string SerializeArgs(Dictionary<string, object>? args)
    {
        if (args is null || args.Count == 0)
            return "{}";
        return JsonSerializer.Serialize(args);
    }

    private static Dictionary<string, object>? DeserializeArgs(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return null;

        return JsonSerializer.Deserialize<Dictionary<string, object>>(json);
    }

    private static Dictionary<string, object>? DeserializeToDict(string content)
    {
        // Try to parse as JSON first; if it fails, wrap the content as a "result" key
        try
        {
            if (content.TrimStart().StartsWith('{'))
                return JsonSerializer.Deserialize<Dictionary<string, object>>(content);
        }
        catch (JsonException) { }

        return new Dictionary<string, object> { ["result"] = content };
    }
}
