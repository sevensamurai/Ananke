using System.Text.Json;
using A2A;
using Ananke.Abstractions.Channels;

namespace Ananke.A2A.Channels;

/// <summary>
/// An <see cref="IHandoffChannel"/> implementation that delegates to a remote A2A agent
/// using the A2A protocol. Publishes messages as A2A <see cref="MessageSendParams"/> and
/// maps the returned task artifacts back to the expected response type.
/// </summary>
/// <remarks>
/// Enables existing <c>HandoffJob</c>-based workflows to transparently delegate
/// to remote A2A agents by swapping the channel implementation — no workflow changes required.
/// The <c>topic</c> parameter in <see cref="SendAsync{TMessage, TResponse}"/>
/// is unused (the A2A endpoint is fixed at construction), but the correlation ID is mapped to
/// the A2A message's <c>ContextId</c> for traceability.
/// </remarks>
/// <example>
/// <code>
/// var channel = new A2AHandoffChannel(new Uri("http://remote-agent:5100/a2a"));
///
/// var workflow = new Workflow&lt;MyState&gt;("pipeline")
///     .Then("delegate", Handoff.To&lt;MyState, Request, Response&gt;("topic", channel)
///         .CreateMessage(s =&gt; new Request { Query = s.Input })
///         .MapResult((s, r) =&gt; s with { Output = r.Answer })
///         .Build());
/// </code>
/// </example>
public sealed class A2AHandoffChannel : IHandoffChannel
{
    private readonly A2AClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Creates a handoff channel targeting the specified A2A agent endpoint.
    /// </summary>
    /// <param name="agentUrl">The remote A2A agent's endpoint URL.</param>
    /// <param name="httpClient">Optional shared <see cref="HttpClient"/>.</param>
    public A2AHandoffChannel(Uri agentUrl, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(agentUrl);

        _client = httpClient is not null
            ? new A2AClient(agentUrl, httpClient)
            : new A2AClient(agentUrl);
    }

    /// <summary>
    /// Sends a message to the remote A2A agent and returns the deserialized response.
    /// </summary>
    /// <remarks>
    /// The <paramref name="message"/> is serialized to JSON and sent as a <see cref="TextPart"/>.
    /// The <paramref name="topic"/> parameter is not used for routing (the endpoint is fixed),
    /// but is included in message metadata for diagnostic purposes.
    /// </remarks>
    public async Task<TResponse> SendAsync<TMessage, TResponse>(
        string topic,
        string correlationId,
        TMessage message,
        TimeSpan timeout,
        CancellationToken ct = default)
        where TMessage : class
        where TResponse : class
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var messageJson = JsonSerializer.Serialize(message, JsonOptions);

        var a2aMessage = new global::A2A.AgentMessage
        {
            Role = MessageRole.User,
            MessageId = Guid.NewGuid().ToString(),
            ContextId = correlationId,
            Parts = [new TextPart { Text = messageJson }]
        };

        var sendParams = new MessageSendParams
        {
            Message = a2aMessage
        };

        var response = await _client.SendMessageAsync(sendParams, cts.Token).ConfigureAwait(false);

        var responseText = ExtractResponseText(response);
        return JsonSerializer.Deserialize<TResponse>(responseText, JsonOptions)
            ?? throw new InvalidOperationException(
                $"A2A agent response could not be deserialized to {typeof(TResponse).Name}. Response: {responseText}");
    }

    /// <summary>
    /// Not supported for <see cref="A2AHandoffChannel"/>. The remote A2A agent responds
    /// synchronously via <see cref="SendAsync{TMessage, TResponse}"/>; there is no separate
    /// completion step.
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public Task CompleteAsync<TResponse>(
        string topic,
        string correlationId,
        TResponse response,
        CancellationToken ct = default)
        where TResponse : class
    {
        throw new NotSupportedException(
            $"{nameof(A2AHandoffChannel)} does not support CompleteAsync. " +
            "The remote A2A agent provides responses via SendMessageAsync.");
    }

    private static string ExtractResponseText(A2AResponse response)
    {
        return response switch
        {
            global::A2A.AgentTask task => ExtractFromTask(task),
            global::A2A.AgentMessage message => ExtractFromParts(message.Parts),
            _ => string.Empty
        };
    }

    private static string ExtractFromTask(global::A2A.AgentTask task)
    {
        if (task.Artifacts is { Count: > 0 })
        {
            var texts = task.Artifacts
                .Where(a => a.Parts is { Count: > 0 })
                .SelectMany(a => a.Parts!)
                .OfType<TextPart>()
                .Select(p => p.Text)
                .Where(t => !string.IsNullOrEmpty(t));

            var combined = string.Join("\n", texts);
            if (!string.IsNullOrEmpty(combined))
                return combined;
        }

        if (task.Status.Message?.Parts is { Count: > 0 } statusParts)
            return ExtractFromParts(statusParts);

        return string.Empty;
    }

    private static string ExtractFromParts(List<Part>? parts)
    {
        if (parts is null or { Count: 0 })
            return string.Empty;

        return string.Join("", parts.OfType<TextPart>().Select(p => p.Text));
    }

    /// <summary>
    /// Not supported for <see cref="A2AHandoffChannel"/>. A2A is a request-response
    /// protocol; use <see cref="SendAsync{TMessage, TResponse}"/> instead.
    /// </summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public Task SubscribeAsync<TMessage, TResponse>(
        string topic,
        Func<TMessage, CancellationToken, Task<TResponse>> handler,
        CancellationToken ct = default)
        where TMessage : class
        where TResponse : class
    {
        throw new NotSupportedException(
            $"{nameof(A2AHandoffChannel)} does not support SubscribeAsync. " +
            "A2A agents respond synchronously via SendMessageAsync.");
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => default;
}
