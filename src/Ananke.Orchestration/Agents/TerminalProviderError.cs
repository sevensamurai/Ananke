using System.Text.RegularExpressions;

namespace Ananke.Orchestration.Agents;

/// <summary>
/// Whether a provider error is one that waiting cannot fix — a depleted account rather than a busy
/// one — even though it arrived looking exactly like a rate limit.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both arrive as HTTP 429.</b> "You are going too fast" and "you have run out of credit" are the
/// same status code at every provider that matters, so the retry loop treated a dead account as a
/// busy one: found live, where a depleted key cost <b>three round trips per node</b> across a whole
/// plan, and the message that said why was shown on the third attempt instead of the first.
/// </para>
/// <para>
/// <b>The provider's own instruction outranks anything read out of its prose.</b> An error that
/// states a retry delay is transient by its own account, whatever words surround it — which matters
/// because a per-minute limit is routinely worded as a quota, and Google's ordinary rate-limit body
/// says <c>Quota exceeded for quota metric …</c> while carrying a <c>retryDelay</c>.
/// </para>
/// <para>
/// <b>When in doubt, retry.</b> A wrong "terminal" ends a run that waiting would have rescued; a
/// wrong "transient" costs two more calls. Only depletion this recognises outright is treated as
/// terminal, and everything else is left exactly as it was.
/// </para>
/// </remarks>
public static class TerminalProviderError
{
    // The account is out of money or allowance. OpenAI's insufficient_quota, Anthropic's credit
    // balance, and the plan/billing wording every provider uses when the fix is a payment.
    private static readonly Regex Depleted = new(
        """
        insufficient_quota
        |exceeded\s+your\s+current\s+quota
        |check\s+your\s+plan\s+and\s+billing
        |billing\s+details
        |credit\s+balance\s+is\s+too\s+low
        |credits?\s+(?:are|is|have\s+been)\s+depleted
        |prepayment\s+credits?
        |out\s+of\s+credits?
        |quota\s+has\s+been\s+exhausted
        |account\s+(?:is\s+)?(?:suspended|deactivated)
        |payment\s+(?:required|method)
        """,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace,
        TimeSpan.FromMilliseconds(200));

    // A window that moves on its own. Whatever else the message says, something that resets per
    // minute, hour or day is the case retrying exists for. Written to match a quota id as well as
    // prose — `GenerateRequestsPerMinutePerProjectPerModel-FreeTier` is how a free tier says it, and
    // it says it beside wording borrowed word for word from a depleted account.
    private static readonly Regex MovingWindow = new(
        """
        per[\s_-]*(?:minute|hour|day|second)
        |requests?[\s_-]*per
        |tokens?[\s_-]*per
        |rate\s*limit
        |too\s+many\s+requests
        |slow\s+down
        """,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace,
        TimeSpan.FromMilliseconds(200));

    /// <summary>
    /// Whether <paramref name="exception"/> — or anything it wraps — says the account cannot serve
    /// this call at all, as opposed to not right now.
    /// </summary>
    /// <remarks>
    /// Read the same way a rate limit is: down the exception chain, from the message, because the
    /// alternative is a package reference per provider inside the engine.
    /// </remarks>
    public static bool Is(Exception? exception)
    {
        // Said to wait, so waiting is the answer. Checked once over the whole chain, before any
        // message is read: a provider that quotes a delay has already classified its own error.
        if (ProviderRetryDelay.From(exception) is not null)
            return false;

        for (var current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message;

            if (string.IsNullOrEmpty(message))
                continue;

            if (MovingWindow.IsMatch(message))
                return false;

            if (Depleted.IsMatch(message))
                return true;
        }

        return false;
    }
}
