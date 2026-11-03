using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Ananke.Orchestration.Planning;

/// <summary>Something a step found, and where anybody can check it again.</summary>
/// <remarks>
/// <para>
/// <b>Evidence only because of <see cref="Source"/>.</b> A step's summary is narration because nothing
/// can re-take it; a finding names the URL, the file and line, the package and version, or the command
/// it came from, so somebody else can.
/// </para>
/// <para>
/// <b>Not required by the type.</b> A finding with no source still arrives, so the loop can refuse it in
/// words the step can act on, rather than the whole report failing to read.
/// </para>
/// </remarks>
[Experimental(ExperimentalApis.PlanTier, Message = ExperimentalApis.PlanTierMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed record NodeFinding
{
    /// <summary>What was found, in a sentence.</summary>
    [Description("What you found, in one sentence: a fact about the world, not about your own work.")]
    public string Claim { get; init; } = string.Empty;

    /// <summary>Where it can be checked again.</summary>
    [Description(
        "Where anybody can check it again: a URL, a file and line, a package and version, or a command. "
        + "A finding with no source is not used.")]
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// What the claim rests on at <see cref="Source"/>, exactly as the source holds it, one entry each.
    /// </summary>
    /// <remarks>
    /// <b>A field, so nothing reads it out of the claim.</b> A claim is a sentence for a person; code it
    /// quotes there could only be found again by guessing where the quote starts and ends. What is here
    /// can be looked for at the source as it is.
    /// </remarks>
    [Description(
        "What the claim rests on at the source, copied exactly as the source holds it, one entry each: "
        + "part of a line, a whole line, or the name of what it declares. Never a path or a file's name — "
        + "the source already says which file. Empty when the claim quotes nothing.")]
    public IReadOnlyList<string> Quotes { get; init; } = [];

    /// <summary>Equal when the claim, the source and every quote, in order, are.</summary>
    /// <remarks>
    /// By value, quotes included: a finding read back from a checkpoint is the finding that was written,
    /// though its list of quotes is a new one.
    /// </remarks>
    public bool Equals(NodeFinding? other) =>
        other is not null
        && Claim == other.Claim
        && Source == other.Source
        && Quotes.SequenceEqual(other.Quotes);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Claim, Source, Quotes.Count);
}
