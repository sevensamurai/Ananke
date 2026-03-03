using System.ClientModel;
using System.Runtime.CompilerServices;
using System.Text;
using Ananke.Orchestration.Agents;
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

    public async Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
    {
        var messages = MapMessages(request);
        var options = BuildOptions(request);

        var result = await _client.CompleteChatAsync(messages, options, ct);
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
                ToolCalls = toolCalls
            };
        }

        return new AgentResponse
        {
            Text = completion.Content.FirstOrDefault()?.Text
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

        await foreach (var update in _client.CompleteChatStreamingAsync(messages, options, ct))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (part.Text is { Length: > 0 })
                {
                    fullText.Append(part.Text);
                    yield return new AgentStreamChunk { TextDelta = part.Text };
                }
            }

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
                ToolCalls = toolCalls
            }
        };
    }

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
                    messages.Add(ChatMessage.CreateUserMessage(msg.Content!));
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

    private static ChatCompletionOptions BuildOptions(AgentRequest request)
    {
        var options = new ChatCompletionOptions { StoredOutputEnabled = request.StoreCompletions };

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

        if (request.Metadata is not null)
        {
            foreach (var (key, value) in request.Metadata)
                options.Metadata[key] = value;
        }

        return options;
    }
}
