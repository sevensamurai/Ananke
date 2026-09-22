using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Polly;
using Polly.Retry;

namespace Ananke.Orchestration.Agents.Middleware;

/// <summary>
/// Decorator that adds retry-with-backoff for transient LLM provider errors (HTTP 429 rate-limit)
/// to any <see cref="IStreamingAgentModel"/>. Retry attempts are recorded as OTel events on
/// <see cref="Activity.Current"/> when a trace is active.
/// </summary>
/// <remarks>
/// <para>
/// For non-streaming calls, the decorator uses a <see cref="ResiliencePipeline"/> from Polly.
/// For streaming calls, retry is applied only while establishing the stream (first chunk).
/// Once chunks start flowing, errors propagate to the caller — partial streams cannot be
/// transparently retried.
/// </para>
/// <para>
/// The default retry predicate detects HTTP 429 across provider SDKs without taking a hard
/// dependency on any of them: it checks <see cref="HttpRequestException.StatusCode"/> and
/// duck-types a <c>Status</c> or <c>StatusCode</c> property on unknown exception types.
/// </para>
/// </remarks>
public sealed class ResilientAgentModel : IStreamingAgentModel
{
    private readonly IStreamingAgentModel _inner;
    private readonly ResiliencePipeline _pipeline;
    private readonly Func<Exception, bool> _shouldRetry;
    private readonly int _maxStreamRetryAttempts;
    private readonly TimeSpan _baseDelay;

    /// <summary>
    /// Creates a resilient wrapper using a custom <see cref="ResiliencePipeline"/>.
    /// The pipeline is used for non-streaming calls. Streaming calls use built-in retry with
    /// <paramref name="shouldRetry"/> for the stream-establishment phase.
    /// </summary>
    public ResilientAgentModel(
        IStreamingAgentModel inner,
        ResiliencePipeline pipeline,
        Func<Exception, bool>? shouldRetry = null,
        int maxStreamRetryAttempts = 5,
        TimeSpan? baseDelay = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(pipeline);

        _inner = inner;
        _pipeline = pipeline;
        _shouldRetry = shouldRetry ?? IsTransientRateLimit;
        _maxStreamRetryAttempts = maxStreamRetryAttempts;
        _baseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// Creates a resilient wrapper with default retry settings for HTTP 429 handling.
    /// </summary>
    /// <param name="inner">The model to wrap.</param>
    /// <param name="maxRetryAttempts">Maximum number of retry attempts. Default is 5.</param>
    /// <param name="baseDelay">Initial delay between retries (exponential backoff). Default is 1 second.</param>
    /// <param name="shouldRetry">
    /// Optional predicate to determine if an exception is retryable.
    /// Defaults to <see cref="IsTransientRateLimit"/> — HTTP 429 across providers, minus the ones
    /// that say the account is out of allowance rather than going too fast.
    /// </param>
    public static ResilientAgentModel Create(
        IStreamingAgentModel inner,
        int maxRetryAttempts = 5,
        TimeSpan? baseDelay = null,
        Func<Exception, bool>? shouldRetry = null)
    {
        var retryPredicate = shouldRetry ?? IsTransientRateLimit;
        var delay = baseDelay ?? TimeSpan.FromSeconds(1);

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ex => retryPredicate(ex)),
                MaxRetryAttempts = maxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = delay,

                // A provider that states how long to wait knows better than the curve. Returning
                // null leaves the exponential backoff in charge, which is what happens when it
                // says nothing.
                DelayGenerator = args => ValueTask.FromResult(
                    ProviderRetryDelay.From(args.Outcome.Exception)),
                OnRetry = args =>
                {
                    RecordRetryOtelEvent(args.Outcome.Exception!, args.AttemptNumber + 1, args.RetryDelay);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();

        return new ResilientAgentModel(inner, pipeline, retryPredicate, maxRetryAttempts, delay);
    }

    /// <inheritdoc />
    public async Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        => await _pipeline.ExecuteAsync(
            async token => await _inner.GenerateAsync(request, token).ConfigureAwait(false), ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (enumerator, firstChunk) = await EstablishStreamAsync(request, ct).ConfigureAwait(false);

        if (firstChunk is null)
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
            yield break;
        }

        yield return firstChunk;

        try
        {
            while (await enumerator.MoveNextAsync().ConfigureAwait(false))
                yield return enumerator.Current;
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Establishes the stream by obtaining the first chunk, retrying on rate-limit errors.
    /// This is a regular async method (no <c>yield</c>) so try-catch works without restriction.
    /// </summary>
    private async Task<(IAsyncEnumerator<AgentStreamChunk> enumerator, AgentStreamChunk? firstChunk)>
        EstablishStreamAsync(AgentRequest request, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            IAsyncEnumerator<AgentStreamChunk>? enumerator = null;
            try
            {
                enumerator = _inner.GenerateStreamAsync(request, ct).GetAsyncEnumerator(ct);
                if (await enumerator.MoveNextAsync().ConfigureAwait(false))
                    return (enumerator, enumerator.Current);
                return (enumerator, null);
            }
            catch (Exception ex) when (attempt <= _maxStreamRetryAttempts && _shouldRetry(ex))
            {
                if (enumerator is not null) await enumerator.DisposeAsync().ConfigureAwait(false);

                var delay = ProviderRetryDelay.From(ex) ?? CalculateBackoff(attempt);
                RecordRetryOtelEvent(ex, attempt, delay);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch
            {
                if (enumerator is not null) await enumerator.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    private TimeSpan CalculateBackoff(int attempt)
    {
        // Exponential backoff with jitter, matching Polly's default behaviour
        var exponential = _baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var jitter = Random.Shared.NextDouble() * exponential * 0.25;
        return TimeSpan.FromMilliseconds(exponential + jitter);
    }

    /// <summary>
    /// A rate limit that waiting can clear: HTTP 429, <b>excluding</b> the ones that mean the
    /// account has run out of allowance.
    /// </summary>
    /// <remarks>
    /// The two are the same status code everywhere, so retrying on 429 alone spends every attempt
    /// against a wall that does not move — and shows the message explaining why on the last attempt
    /// instead of the first. <see cref="IsRateLimitException"/> still answers the narrower question
    /// it always did, which is what a caller supplying its own predicate is composing from.
    /// </remarks>
    public static bool IsTransientRateLimit(Exception ex) =>
        IsRateLimitException(ex) && !TerminalProviderError.Is(ex);

    /// <summary>
    /// Detects HTTP 429 (Too Many Requests) errors across provider SDKs without referencing
    /// provider-specific types. Walks the exception chain checking:
    /// <list type="number">
    /// <item><see cref="HttpRequestException.StatusCode"/> (standard .NET)</item>
    /// <item>A <c>Status</c> or <c>StatusCode</c> property via duck-typing (covers
    /// <c>ClientResultException</c> from the OpenAI SDK, Anthropic exceptions, etc.)</item>
    /// </list>
    /// </summary>
    public static bool IsRateLimitException(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests })
                return true;

            if (TryGetHttpStatus(current) == 429)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The HTTP status an exception carries under a conventionally-named property, or
    /// <see langword="null"/> when it carries none this can read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It must never throw, and that is not defensiveness.</b> This is reached from
    /// <c>catch (…) when (!ShouldRetry(ex))</c>, and the CLR swallows an exception raised inside an
    /// exception filter and reads the filter as <see langword="false"/>. So a throw here does not
    /// surface as a bug — it silently inverts the decision, and every error becomes retryable.
    /// </para>
    /// <para>
    /// <b>Which is what <see cref="Type.GetProperty(string)"/> did.</b> It raises
    /// <see cref="System.Reflection.AmbiguousMatchException"/> when a derived type hides a base
    /// property of the same name, and <c>Google.GenAI.ClientError</c> hides
    /// <see cref="HttpRequestException.StatusCode"/> with an <see cref="int"/> of its own. Every
    /// Gemini failure therefore classified as retryable, terminal ones included — a depleted account
    /// paying for three round trips per node, which is the exact fault
    /// <see cref="TerminalProviderError"/> exists to prevent.
    /// </para>
    /// <para>
    /// Enumerating rather than resolving also fixes the reason the hiding was a problem: both
    /// declarations are considered, and the first that holds a value answers.
    /// </para>
    /// </remarks>
    private static int? TryGetHttpStatus(Exception ex)
    {
        // "Status" first, then "StatusCode": System.ClientModel.ClientResultException (OpenAI SDK)
        // carries an int Status, and a type with both means the more specific one by that name.
        return Read(ex, "Status") ?? Read(ex, "StatusCode");

        static int? Read(Exception ex, string name)
        {
            foreach (var property in ex.GetType().GetProperties())
            {
                if (!string.Equals(property.Name, name, StringComparison.Ordinal))
                    continue;

                object? value;

                // An indexer, a property that throws on read, a type that will not let us look:
                // none of them are an answer, and none of them may escape.
                try
                {
                    value = property.GetIndexParameters().Length == 0 ? property.GetValue(ex) : null;
                }
                catch
                {
                    continue;
                }

                // Null is what an unset hidden base property reads as, so it is skipped rather than
                // answered with — the declaration that actually holds the status is the other one.
                switch (value)
                {
                    case int status:
                        return status;

                    case HttpStatusCode http:
                        return (int)http;
                }
            }

            return null;
        }
    }

    private static void RecordRetryOtelEvent(Exception ex, int attempt, TimeSpan delay)
    {
        var activity = Activity.Current;
        if (activity is null) return;

        activity.AddEvent(new ActivityEvent("llm.rate_limit_retry", tags: new ActivityTagsCollection
        {
            { "retry.attempt", attempt },
            { "retry.delay_ms", (int)delay.TotalMilliseconds },
            { "exception.type", ex.GetType().FullName },
            { "exception.message", ex.Message }
        }));

        activity.SetTag("llm.retries", attempt);
    }
}
