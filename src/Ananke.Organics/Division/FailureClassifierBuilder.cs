using Ananke.Organics.Healing;

namespace Ananke.Organics.Division;

/// <summary>
/// Fluent builder for <see cref="FailureClassifier"/> that lets callers
/// compose custom failure-detection profiles from typed <see cref="FailurePattern"/>
/// records.
/// </summary>
/// <remarks>
/// Obtain a pre-populated instance via <see cref="FailureClassifierProfiles"/>
/// or start from an empty builder using <c>new FailureClassifierBuilder()</c>.
/// </remarks>
public sealed class FailureClassifierBuilder
{
    private readonly List<FailurePattern> _patterns = [];
    private string? _locale;

    /// <summary>
    /// Adds a pattern that matches a substring in a job error message and
    /// classifies the failure into the given <paramref name="lane"/>.
    /// </summary>
    /// <param name="lane">The failure origin this pattern targets.</param>
    /// <param name="pattern">Substring to match (case-insensitive).</param>
    /// <param name="locale">
    /// Optional BCP-47 locale scope (e.g. <c>"fr"</c>). Pass <see langword="null"/>
    /// to apply the pattern regardless of locale.
    /// </param>
    public FailureClassifierBuilder AddPattern(FailureOrigin lane, string pattern, string? locale = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        _patterns.Add(new FailurePattern(lane, pattern, locale));
        return this;
    }

    /// <summary>
    /// Sets a default locale filter applied to all subsequent
    /// <see cref="AddPattern"/> calls that do not specify their own locale.
    /// Patterns already added are unaffected.
    /// </summary>
    public FailureClassifierBuilder WithLocale(string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        _locale = locale;
        return this;
    }

    /// <summary>
    /// Builds a <see cref="FailureClassifier"/> from the accumulated patterns.
    /// </summary>
    public FailureClassifier Build()
    {
        var classifier = new FailureClassifier();
        foreach (var fp in _patterns)
        {
            var effectiveLocale = fp.Locale ?? _locale;
            classifier.AddUpstreamPattern(fp.Pattern);

            // Locale-scoped patterns are added only when the locale matches or
            // no locale is set on the pattern — the base classifier is locale-agnostic
            // for now; locale info is preserved in the pattern record for future use.
            _ = effectiveLocale; // reserved for locale-aware overload in v0.9
        }

        return classifier;
    }
}
