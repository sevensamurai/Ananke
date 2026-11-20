using System.Text;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Routing;

namespace Ananke.Orchestration.Agents;

/// <summary>
/// A model asked for its answer as a stream and handed back whole.
/// </summary>
/// <remarks>
/// <para>
/// <b>The client's timeout applies to each read.</b> A provider client gives up on a call whose whole
/// answer has not arrived within its network timeout, and asks again from the start. Read as a stream,
/// the timeout applies to each read instead, so a model that reasons for minutes before answering is
/// not cut off while it is still sending. The time a whole call may take is
/// <see cref="TimeLimitedAgentModel"/>'s.
/// </para>
/// <para>
/// <b>A reply that runs away repeating itself is stopped as it arrives.</b> Each piece is fed to
/// <see cref="RepeatedOutput"/>, which throws <see cref="RepeatedOutputException"/> once the reply ends
/// repeating one passage over <see cref="RepeatedOutput.MinStreamedRepeatedLength"/> characters. Read
/// whole, such a reply runs to the provider's output limit before anything can look at it. Until the
/// stream ends, why it will stop is not known, so a shorter repeat goes on: rows asked for may repeat.
/// </para>
/// <para>
/// <b>Once it ends, the reply is checked as an unstreamed one is.</b> It is returned whole, and an
/// agent job checks it as it checks any reply: one cut at its output limit, or that does not say why
/// it stopped, throws <see cref="RepeatedOutputException"/> when it ends repeating one passage over
/// <see cref="RepeatedOutput.MinRepeatedLength"/> characters; one the model ended itself does not.
/// </para>
/// <para>
/// A model that cannot stream is asked as it is.
/// </para>
/// </remarks>
public sealed class StreamedAgentModel : IAgentModel, IModelContextResolver
{
    private readonly IAgentModel _inner;

    /// <summary>Asks <paramref name="inner"/> for its answers as a stream, when it can stream.</summary>
    public StreamedAgentModel(IAgentModel inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The stream's completed response when it sends one, and otherwise a response holding the text the
    /// stream carried — unless the request offered tools: a call arrives only on the completed response,
    /// so without one the reply cannot be told from a final answer.
    /// </remarks>
    /// <exception cref="RepeatedOutputException">The reply ran away repeating one passage.</exception>
    /// <exception cref="InvalidOperationException">
    /// The request offered tools and the stream ended without a completed response.
    /// </exception>
    public async Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
    {
        if (_inner is not IStreamingAgentModel streaming)
            return await _inner.GenerateAsync(request, ct).ConfigureAwait(false);

        var text = new StringBuilder();
        var repeated = new RepeatedOutput(RepeatedOutput.MinStreamedRepeatedLength);

        await foreach (var chunk in streaming.GenerateStreamAsync(request, ct).ConfigureAwait(false))
        {
            if (chunk.CompletedResponse is { } completed)
                return completed;

            text.Append(chunk.TextDelta);
            repeated.Add(chunk.TextDelta);
        }

        if (request.Tools is { Count: > 0 })
        {
            throw new InvalidOperationException(
                "The stream ended without a completed response, and the request offered tools: whether the "
                + "model called one cannot be read from its text.");
        }

        // A stream with no completed response does not say why it stopped: its StopReason is Unknown.
        return new AgentResponse { Text = text.Length > 0 ? text.ToString() : null };
    }

    /// <inheritdoc />
    public ModelContextWindow ResolveContextWindow(AgentRequest request) =>
        _inner is IModelContextResolver resolver ? resolver.ResolveContextWindow(request) : ModelContextWindow.Unknown;
}

/// <summary>Asking a model for its answers as a stream.</summary>
public static class AgentModelStreamedExtensions
{
    /// <summary>
    /// Asks <paramref name="model"/> for each answer as a stream, and hands it back whole: the client's
    /// timeout applies to each read, and a reply that runs away repeating one passage throws
    /// <see cref="RepeatedOutputException"/> as it arrives, which ends the run. A reply the model ended
    /// itself is returned whole however it repeats; an agent job checks one cut at its output limit, or
    /// that does not say why it stopped, as it checks an unstreamed reply.
    /// </summary>
    /// <example>
    /// <code>
    /// var reasoning = OpenAIChatAgentModel.Create(key, model, endpoint)
    ///     .Streamed()
    ///     .WithCallTimeout(TimeSpan.FromMinutes(5));
    /// </code>
    /// </example>
    public static StreamedAgentModel Streamed(this IAgentModel model) => new(model);
}
