using Ananke.Federation.Validation;
using Shouldly;

namespace Ananke.Federation.Tests;

/// <summary>
/// Locks down the single alias source introduced by. Before it, the same alias
/// pairs were hard-coded in three places; these tests exist so a fourth copy cannot quietly appear
/// and so an identifier that has been persisted stays resolvable.
/// </summary>
[TestFixture]
public sealed class PlatformIdentifiersTests
{
    [TestCase("azure", "azure")]
    [TestCase("azure-ai", "azure")]
    [TestCase("foundry", "azure")]
    [TestCase("vertex-ai", "vertex-ai")]
    [TestCase("gemini-enterprise", "vertex-ai")]
    [TestCase("gemini-agent-platform", "vertex-ai")]
    [TestCase("claude", "claude")]
    public void Resolve_KnownIdentifierOrAlias_ReturnsCanonical(string input, string expected) =>
        PlatformIdentifiers.Resolve(input).ShouldBe(expected);

    [TestCase("FOUNDRY", "azure")]
    [TestCase("Vertex-AI", "vertex-ai")]
    public void Resolve_IsCaseInsensitive_AndNormalisesToDeclaredSpelling(string input, string expected) =>
        PlatformIdentifiers.Resolve(input).ShouldBe(expected);

    [Test]
    public void Resolve_UnknownIdentifier_ReturnsItUnchanged() =>
        PlatformIdentifiers.Resolve("bedrock-agentcore").ShouldBe("bedrock-agentcore");

    [Test]
    public void TryResolve_UnknownIdentifier_ReturnsFalseButStillEchoesInput()
    {
        PlatformIdentifiers.TryResolve("bedrock-agentcore", out var canonical).ShouldBeFalse();
        canonical.ShouldBe("bedrock-agentcore");
    }

    [TestCase("foundry")]
    [TestCase("claude")]
    public void IsKnown_CanonicalOrAlias_IsTrue(string platform) =>
        PlatformIdentifiers.IsKnown(platform).ShouldBeTrue();

    [Test]
    public void Canonical_MatchesTheCapabilityCatalogue()
    {
        // Two embedded files describe the same platform set. Them drifting apart is the failure
        // this whole slice exists to prevent, so assert it rather than trusting review.
        PlatformIdentifiers.Canonical.OrderBy(p => p)
            .ShouldBe(PlatformCapabilities.KnownPlatforms.OrderBy(p => p));
    }

    [Test]
    public void TryGetForPlatform_KnownPlatform_ReportsTrueWithItsCapabilities()
    {
        PlatformCapabilities.TryGetForPlatform("azure-ai", out var caps).ShouldBeTrue();
        caps.ShouldContain("bing_grounding");
    }

    [Test]
    public void TryGetForPlatform_UnknownPlatform_ReportsFalseRatherThanAnEmptySet()
    {
        // GetForPlatform cannot express this: it returns the same empty set either way, which is
        // how a renamed identifier turns into a validator that silently stops validating.
        PlatformCapabilities.TryGetForPlatform("bedrock-agentcore", out var caps).ShouldBeFalse();
        caps.ShouldBeEmpty();
        PlatformCapabilities.GetForPlatform("bedrock-agentcore").ShouldBeEmpty();
    }

    [Test]
    public void GetForPlatform_ResolvesAliases()
    {
        PlatformCapabilities.GetForPlatform("gemini-agent-platform")
            .ShouldBe(PlatformCapabilities.GetForPlatform("vertex-ai"));
    }

    // ── local as a first-class substrate (L2) ─────────────────────────────────

    /// <summary>
    /// Federation is <i>where a cell runs</i>, and the local process is one
    /// substrate among several. Before L2, <c>local</c> was documented <i>Stable</i> and was unknown
    /// to every mechanism that decides what a platform is.
    /// </summary>
    [Test]
    public void Local_is_a_known_canonical_platform()
    {
        PlatformIdentifiers.IsKnown("local").ShouldBeTrue();
        PlatformIdentifiers.Resolve("local").ShouldBe("local");
        PlatformIdentifiers.Canonical.ShouldContain("local");
    }

    /// <summary>
    /// <c>local</c> declares no platform-native capabilities of its own, and "known but declares
    /// none" must stay distinguishable from "unrecognised" — the silent-degradation hazard
    /// an earlier review recorded.
    /// </summary>
    [Test]
    public void Local_is_known_and_declares_no_capabilities()
    {
        PlatformCapabilities.TryGetForPlatform("local", out var caps).ShouldBeTrue();
        caps.ShouldBeEmpty();
    }
}
