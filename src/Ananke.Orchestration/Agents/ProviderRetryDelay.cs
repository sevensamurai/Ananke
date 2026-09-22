using System.Globalization;
using System.Text.RegularExpressions;

namespace Ananke.Orchestration.Agents;

/// <summary>
/// How long a provider said to wait before trying again, when it said so.
/// </summary>
/// <remarks>
/// <para>
/// A rate-limited provider usually answers with the one number that matters — <c>Retry-After</c>, or
/// a <c>retryDelay</c> in the error body. Ignoring it and backing off exponentially from a base of a
/// second means three attempts inside four seconds against a limit measured in minutes: every
/// attempt is spent before the window it is waiting for has moved at all.
/// </para>
/// <para>
/// <b>Read the same way rate limits are detected</b> — by duck-typing the exception and, failing
/// that, reading the message. Provider SDKs surface this in their own types, and the alternative is
/// a package reference per provider inside the engine. Parsing a message is not elegant; spending a
/// run's retries in four seconds is worse.
/// </para>
/// </remarks>
public static class ProviderRetryDelay
{
    /// <summary>
    /// The longest stated delay that will be waited out. Beyond it the caller is better served by
    /// failing with the provider's own message than by sleeping through the run's timeout.
    /// </summary>
    public static readonly TimeSpan Longest = TimeSpan.FromMinutes(2);

    // "retryDelay": "58s" — google.rpc.RetryInfo, as it appears in the error body.
    private static readonly Regex RetryDelayField = new(
        """["']?retryDelay["']?\s*[:=]\s*["']?(\d+(?:\.\d+)?)s""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    // "Please retry in 58.78s", "retry after 30 seconds".
    private static readonly Regex RetryInPhrase = new(
        @"retry (?:in|after)\s+(\d+(?:\.\d+)?)\s*(?:s\b|seconds?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    // A Retry-After header quoted into the message, in seconds.
    private static readonly Regex RetryAfterHeader = new(
        @"retry-after\s*[:=]\s*(\d+(?:\.\d+)?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    /// <summary>
    /// What <paramref name="exception"/> says to wait, or <see langword="null"/> when it says
    /// nothing — including when it asks for longer than <see cref="Longest"/>.
    /// </summary>
    public static TimeSpan? From(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var stated = FromProperty(current) ?? FromMessage(current.Message);

            if (stated is { } delay && delay > TimeSpan.Zero && delay <= Longest)
                return delay;
        }

        return null;
    }

    /// <summary>An SDK that models this properly usually exposes it as a property.</summary>
    private static TimeSpan? FromProperty(Exception exception) =>
        exception.GetType().GetProperty("RetryAfter")?.GetValue(exception) switch
        {
            TimeSpan span => span,
            DateTimeOffset at => at - DateTimeOffset.UtcNow,
            int seconds => TimeSpan.FromSeconds(seconds),
            _ => null
        };

    private static TimeSpan? FromMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return null;

        foreach (var pattern in new[] { RetryDelayField, RetryInPhrase, RetryAfterHeader })
        {
            var match = pattern.Match(message);

            if (match.Success && double.TryParse(
                    match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }

        return null;
    }
}
