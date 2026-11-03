using System.Runtime.CompilerServices;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Routing;

namespace Ananke.Orchestration.Agents;

/// <summary>
/// A model whose every call has a time limit: an attempt that runs past it is asked again, and a call
/// whose last attempt runs past it ends with a <see cref="CallTimeoutException"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The limit belongs to the model, set where the model is chosen.</b> A workflow knows which of its
/// steps are small and which reason at length, and gives each kind of step a model; the time a call
/// may take goes with that model, so a small step's model is held to a short limit and a model hired
/// to reason is given a long one.
/// </para>
/// <para>
/// <b>Asked again once, by default, then not at all.</b> A provider that stops answering is usually
/// answering again on the next attempt. A call that runs past its limit on every attempt is a model
/// going round or a provider that is down, and <see cref="TerminalProviderError.Is"/> reads
/// <see cref="CallTimeoutException"/> as terminal: the engine does not retry it, a plan does not
/// re-attempt the step, and the run ends.
/// </para>
/// <para>
/// The caller's own cancellation is never a timeout and is never retried.
/// </para>
/// </remarks>
public sealed class TimeLimitedAgentModel : IStreamingAgentModel, IModelContextResolver
{
    private readonly IAgentModel _inner;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Holds every call to <paramref name="inner"/> to <paramref name="limit"/>, over at most
    /// <paramref name="attempts"/> attempts, timed by <paramref name="timeProvider"/>
    /// (<see cref="TimeProvider.System"/> when <see langword="null"/>).
    /// </summary>
    public TimeLimitedAgentModel(IAgentModel inner, TimeSpan limit, int attempts = 2, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);

        _inner = inner;
        _timeProvider = timeProvider ?? TimeProvider.System;
        Limit = limit;
        Attempts = attempts;
    }

    /// <summary>How long one attempt may take.</summary>
    public TimeSpan Limit { get; }

    /// <summary>How many attempts a call gets, the first included.</summary>
    public int Attempts { get; }

    /// <inheritdoc />
    public async Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var timer = new CancellationTokenSource(Limit, _timeProvider);
            using var call = CancellationTokenSource.CreateLinkedTokenSource(ct, timer.Token);

            try
            {
                return await _inner.GenerateAsync(request, call.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timer.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                if (attempt == Attempts)
                    throw TimedOut(attempt);
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The limit covers the whole stream. An attempt that has sent nothing when it runs out is asked
    /// again; one that has already sent chunks cannot be, since they have been handed on, and ends the
    /// call.
    /// </remarks>
    public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
        AgentRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_inner is not IStreamingAgentModel streaming)
        {
            var response = await GenerateAsync(request, ct).ConfigureAwait(false);

            if (response.Text is not null)
                yield return new AgentStreamChunk { TextDelta = response.Text };

            yield return new AgentStreamChunk { CompletedResponse = response };
            yield break;
        }

        for (var attempt = 1; ; attempt++)
        {
            using var timer = new CancellationTokenSource(Limit, _timeProvider);
            using var call = CancellationTokenSource.CreateLinkedTokenSource(ct, timer.Token);

            var chunks = streaming.GenerateStreamAsync(request, call.Token).GetAsyncEnumerator(call.Token);
            var sent = false;

            try
            {
                while (true)
                {
                    bool more;

                    try
                    {
                        more = await chunks.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (timer.IsCancellationRequested && !ct.IsCancellationRequested)
                    {
                        if (sent || attempt == Attempts)
                            throw TimedOut(attempt);

                        break;
                    }

                    if (!more)
                        yield break;

                    sent = true;
                    yield return chunks.Current;
                }
            }
            finally
            {
                await chunks.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public ModelContextWindow ResolveContextWindow(AgentRequest request) =>
        _inner is IModelContextResolver resolver ? resolver.ResolveContextWindow(request) : ModelContextWindow.Unknown;

    private CallTimeoutException TimedOut(int attempts) =>
        new($"The model had not answered after {Describe(Limit)}, on {attempts} attempt{(attempts == 1 ? "" : "s")}.");

    private static string Describe(TimeSpan limit) =>
        limit >= TimeSpan.FromMinutes(1)
            ? $"{limit.TotalMinutes:0.##} minutes"
            : $"{limit.TotalSeconds:0.###} seconds";
}

/// <summary>Holding a model's calls to a time limit.</summary>
public static class AgentModelCallTimeoutExtensions
{
    /// <summary>
    /// Holds every call to <paramref name="model"/> to <paramref name="limit"/>: an attempt past it is
    /// asked again, up to <paramref name="attempts"/> in all, and a call whose last attempt is past it
    /// throws <see cref="CallTimeoutException"/>, which ends the run. The limit is timed by
    /// <paramref name="timeProvider"/>, <see cref="TimeProvider.System"/> when <see langword="null"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// var executor = smallModel.WithCallTimeout(TimeSpan.FromMinutes(2));
    /// var supervisor = reasoningModel.WithCallTimeout(TimeSpan.FromMinutes(5));
    /// </code>
    /// </example>
    public static TimeLimitedAgentModel WithCallTimeout(
        this IAgentModel model, TimeSpan limit, int attempts = 2, TimeProvider? timeProvider = null) =>
        new(model, limit, attempts, timeProvider);
}

/// <summary>
/// A call that ran past its time limit on every attempt it was given. Not retried: it ends the run.
/// </summary>
public sealed class CallTimeoutException(string message) : Exception(message);
