using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Polly;
using Polly.Retry;

namespace Ananke.Orchestration.Agents;

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
        _shouldRetry = shouldRetry ?? IsRateLimitException;
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
    /// Defaults to <see cref="IsRateLimitException"/> which detects HTTP 429 across providers.
    /// </param>
    public static ResilientAgentModel Create(
        IStreamingAgentModel inner,
        int maxRetryAttempts = 5,
        TimeSpan? baseDelay = null,
        Func<Exception, bool>? shouldRetry = null)
    {
        var retryPredicate = shouldRetry ?? IsRateLimitException;
        var delay = baseDelay ?? TimeSpan.FromSeconds(1);

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ex => retryPredicate(ex)),
                MaxRetryAttempts = maxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = delay,
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
            async token => await _inner.GenerateAsync(request, token), ct);

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (enumerator, firstChunk) = await EstablishStreamAsync(request, ct);

        if (firstChunk is null)
        {
            await enumerator.DisposeAsync();
            yield break;
        }

        yield return firstChunk;

        try
        {
            while (await enumerator.MoveNextAsync())
                yield return enumerator.Current;
        }
        finally
        {
            await enumerator.DisposeAsync();
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
                if (await enumerator.MoveNextAsync())
                    return (enumerator, enumerator.Current);
                return (enumerator, null);
            }
            catch (Exception ex) when (attempt <= _maxStreamRetryAttempts && _shouldRetry(ex))
            {
                if (enumerator is not null) await enumerator.DisposeAsync();

                var delay = CalculateBackoff(attempt);
                RecordRetryOtelEvent(ex, attempt, delay);
                await Task.Delay(delay, ct);
            }
            catch
            {
                if (enumerator is not null) await enumerator.DisposeAsync();
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

    private static int? TryGetHttpStatus(Exception ex)
    {
        var type = ex.GetType();

        // Covers System.ClientModel.ClientResultException (OpenAI SDK) which has int Status
        if (type.GetProperty("Status")?.GetValue(ex) is int status)
            return status;

        // Covers exceptions with HttpStatusCode StatusCode property
        if (type.GetProperty("StatusCode")?.GetValue(ex) is HttpStatusCode httpStatus)
            return (int)httpStatus;

        // Covers exceptions with int StatusCode property
        if (type.GetProperty("StatusCode")?.GetValue(ex) is int statusCode)
            return statusCode;

        return null;
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
