using Ananke.Federation.Validation;

namespace Ananke.Tool.Platform;

/// <summary>
/// Announces the platforms that ship as <b>preview</b> in 1.0.
/// </summary>
/// <remarks>
/// <para>
/// The vendor adapters ship as preview rather than as part of the supported 1.0
/// surface, because they have never been exercised against a real platform: the federation axis has
/// no live tier at all (V3), and the two defects the review found there — a probe that registered
/// nothing and an Azure client constructed without a credential — were both invisible to every
/// offline test.
/// </para>
/// <para>
/// <b>Preview has to be visible to mean anything.</b> A caveat that lives only in the documentation
/// is a caveat the person running the command never sees, which is the same shape as a verb
/// reporting "no data" when it means "impossible". This prints to stderr, so it annotates an
/// interactive run without corrupting <c>--json</c> output that something downstream may be parsing.
/// </para>
/// </remarks>
internal static class PreviewNotice
{
    /// <summary>
    /// The built-in substrate is supported; every vendor adapter is preview until it has been
    /// proven against a live platform.
    /// </summary>
    private static bool IsPreview(string platform) =>
        !string.Equals(
            PlatformIdentifiers.Resolve(platform),
            PlatformHost.LocalPlatform,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Writes the preview notice for <paramref name="platform"/> when one applies.
    /// </summary>
    /// <param name="platform">Platform identifier as the user typed it.</param>
    public static void WriteIfPreview(string platform)
    {
        if (!IsPreview(platform))
            return;

        Console.Error.WriteLine(
            $"  ⚠ '{platform}' is a preview platform: its adapter has not been verified against a "
            + "live service. See 'nnke-platform adapters doctor'.");
    }
}

