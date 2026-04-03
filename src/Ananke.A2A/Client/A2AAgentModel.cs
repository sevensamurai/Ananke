using System.Runtime.CompilerServices;
using System.Text;
using A2A;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;

namespace Ananke.A2A.Client;

/// <summary>
/// An <see cref="IAgentModel"/> and <see cref="IStreamingAgentModel"/> implementation
/// that delegates to a remote A2A-compliant agent via the A2A protocol.
/// </summary>
/// <remarks>
/// Maps Ananke's <see cref="AgentRequest"/> to A2A <see cref="MessageSendParams"/>,
/// sends it to the remote agent, and converts the A2A response back to an
/// <see cref="AgentResponse"/>.
/// <para>
/// Create instances directly or via the <c>AddAnankeA2AClient</c> DI extension.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var model = new A2AAgentModel(new A2AAgentModelOptions
/// {
///     AgentUrl = new Uri("http://localhost:5100/echo")
/// });
///
/// var response = await model.GenerateAsync(new AgentRequest
/// {
///     Messages = [AgentMessage.User("Hello, remote agent!")]
/// });
/// </code>
/// </example>
public sealed class A2AAgentModel : IStreamingAgentModel
{
    private readonly A2AClient _client;
    private readonly A2AAgentModelOptions _options;

    public A2AAgentModel(A2AAgentModelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _client = options.HttpClient is not null
            ? new A2AClient(options.AgentUrl, options.HttpClient)
            : new A2AClient(options.AgentUrl);
    }

    /// <inheritdoc />
    public async Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sendParams = BuildSendParams(request);
        var response = await _client.SendMessageAsync(sendParams, ct).ConfigureAwait(false);

        return MapResponse(response);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sendParams = BuildSendParams(request);
        var accumulated = new StringBuilder();

        await foreach (var sseItem in _client.SendMessageStreamingAsync(sendParams, ct).ConfigureAwait(false))
        {
            if (sseItem.Data is null)
                continue;

            switch (sseItem.Data)
            {
                case TaskArtifactUpdateEvent artEvt:
                    var delta = ExtractText(artEvt.Artifact?.Parts);
                    if (!string.IsNullOrEmpty(delta))
                    {
                        accumulated.Append(delta);
                        yield return new AgentStreamChunk { TextDelta = delta };
                    }
                    break;

                case TaskStatusUpdateEvent statusEvt:
                    var statusText = statusEvt.Status.Message?.Parts is { } statusParts
                        ? ExtractText(statusParts)
                        : null;

                    if (statusEvt.Status.State is TaskState.Completed or TaskState.Failed)
                    {
                        if (!string.IsNullOrEmpty(statusText))
                            accumulated.Append(statusText);

                        yield return new AgentStreamChunk
                        {
                            TextDelta = statusText,
                            CompletedResponse = new AgentResponse { Text = accumulated.ToString() }
                        };
                        yield break;
                    }

                    if (!string.IsNullOrEmpty(statusText))
                    {
                        accumulated.Append(statusText);
                        yield return new AgentStreamChunk { TextDelta = statusText };
                    }
                    break;
            }
        }

        // Stream ended without an explicit terminal event — emit final response
        if (accumulated.Length > 0)
        {
            yield return new AgentStreamChunk
            {
                CompletedResponse = new AgentResponse { Text = accumulated.ToString() }
            };
        }
    }

    private MessageSendParams BuildSendParams(AgentRequest request)
    {
        var lastUserMessage = request.Messages
            .LastOrDefault(m => m.Role == AgentRole.User);

        var textContent = lastUserMessage?.Content ?? string.Empty;

        // Prepend system prompt as context when present
        if (!string.IsNullOrEmpty(request.SystemPrompt))
            textContent = $"[System: {request.SystemPrompt}]\n\n{textContent}";

        var a2aMessage = new global::A2A.AgentMessage
        {
            Role = MessageRole.User,
            MessageId = Guid.NewGuid().ToString(),
            Parts = [new global::A2A.TextPart { Text = textContent }]
        };

        return new MessageSendParams
        {
            Message = a2aMessage
        };
    }

    private static AgentResponse MapResponse(A2AResponse response)
    {
        return response switch
        {
            global::A2A.AgentTask task => MapTaskToResponse(task),
            global::A2A.AgentMessage message => new AgentResponse
            {
                Text = ExtractText(message.Parts)
            },
            _ => new AgentResponse { Text = string.Empty }
        };
    }

    private static AgentResponse MapTaskToResponse(global::A2A.AgentTask task)
    {
        // Extract text from artifacts first, fall back to status message
        if (task.Artifacts is { Count: > 0 })
        {
            var texts = task.Artifacts
                .Where(a => a.Parts is { Count: > 0 })
                .SelectMany(a => a.Parts!)
                .OfType<global::A2A.TextPart>()
                .Select(p => p.Text);

            var combined = string.Join("\n", texts);
            if (!string.IsNullOrEmpty(combined))
                return new AgentResponse { Text = combined };
        }

        // Fall back to status message text
        if (task.Status.Message?.Parts is { Count: > 0 } statusParts)
        {
            return new AgentResponse { Text = ExtractText(statusParts) };
        }

        return new AgentResponse { Text = string.Empty };
    }

    private static string? ExtractText(List<Part>? parts)
    {
        if (parts is null or { Count: 0 })
            return null;

        var texts = parts.OfType<global::A2A.TextPart>().Select(p => p.Text);
        return string.Join("", texts);
    }
}
