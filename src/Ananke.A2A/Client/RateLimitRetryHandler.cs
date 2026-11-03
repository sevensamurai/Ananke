using System.Net;
using Ananke.Orchestration.Agents;

namespace Ananke.A2A.Client;

/// <summary>
/// Retries a request the remote agent answered with HTTP 429, as a provider SDK does for its own
/// calls. An agent job leaves a plain 429 to the transport, so an A2A call without this handler is
/// not retried at all.
/// </summary>
/// <remarks>
/// <para>
/// The wait is the agent's <c>Retry-After</c> header, or a delay stated in the body (read by
/// <see cref="ProviderRetryDelay"/>), or else <see cref="BaseDelay"/> doubled per retry. A stated
/// wait longer than <see cref="ProviderRetryDelay.Longest"/>, or a body that
/// <see cref="TerminalProviderError"/> reads as an account out of allowance, returns the 429 at once.
/// </para>
/// <para>
/// <see cref="A2AAgentModel"/> uses it when <see cref="A2AAgentModelOptions.HttpClient"/> is not
/// set. A caller that supplies its own <see cref="HttpClient"/> adds it to that client's handlers.
/// </para>
/// </remarks>
public sealed class RateLimitRetryHandler(TimeProvider? timeProvider = null) : DelegatingHandler
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>Attempts in all, the first included. Defaults to 3.</summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>The wait before the first retry when the agent states none; doubled for each one after.</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // The same request is sent again, so its body must be readable more than once.
        if (request.Content is not null)
            await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);

        for (var attempt = 1; ; attempt++)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= MaxAttempts)
                return response;

            var wait = await WaitForAsync(response, attempt, cancellationToken).ConfigureAwait(false);
            if (wait is not { } delay)
                return response;

            response.Dispose();
            await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<TimeSpan?> WaitForAsync(HttpResponseMessage response, int attempt, CancellationToken ct)
    {
        var header = response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - _timeProvider.GetUtcNow(),
            _ => (TimeSpan?)null
        };

        // A date already past means retry now.
        if (header is { } stated)
            return stated > ProviderRetryDelay.Longest ? null : stated < TimeSpan.Zero ? TimeSpan.Zero : stated;

        // Buffered, so the body is still there for the caller if the 429 is returned.
        await response.Content.LoadIntoBufferAsync(ct).ConfigureAwait(false);
        var body = new HttpRequestException(
            await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), null, response.StatusCode);

        if (ProviderRetryDelay.From(body, _timeProvider) is { } inBody)
            return inBody;

        if (TerminalProviderError.Is(body))
            return null;

        return BaseDelay * Math.Pow(2, attempt - 1);
    }
}
