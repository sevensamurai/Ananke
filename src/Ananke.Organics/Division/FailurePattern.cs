using Ananke.Organics.Healing;

namespace Ananke.Organics.Division;

/// <summary>
/// An error-message pattern that classifies a failure into a specific
/// <see cref="FailureOrigin"/> lane.
/// </summary>
/// <param name="Lane">The failure origin this pattern targets.</param>
/// <param name="Pattern">Substring to match (case-insensitive) in the job error message.</param>
/// <param name="Locale">
/// Optional BCP-47 locale tag (e.g. <c>"en"</c>, <c>"fr"</c>).
/// <see langword="null"/> means the pattern applies to all locales.
/// </param>
public sealed record FailurePattern(FailureOrigin Lane, string Pattern, string? Locale = null);
