using Ananke.Abstractions.Agents;
using Shouldly;

namespace Ananke.Design.Tests;

[TestFixture]
public sealed class ModelCatalogTests
{
    // ── Valid known models (Current or Legacy — no lifecycle message) ──

    [TestCase("anthropic", "claude-opus-4-8")]
    [TestCase("anthropic", "claude-sonnet-4-6")]
    [TestCase("anthropic", "claude-haiku-4-5")]
    [TestCase("anthropic", "claude-sonnet-5")]
    [TestCase("anthropic", "claude-fable-5")]
    [TestCase("openai", "gpt-5.4")]
    [TestCase("openai", "gpt-5.5")]
    [TestCase("openai", "gpt-5.6-sol")]
    [TestCase("openai", "gpt-5.6-terra")]
    [TestCase("openai", "gpt-5.6-luna")]
    [TestCase("google", "gemini-3.5-flash")]
    [TestCase("google", "gemini-3.1-flash-lite")]
    public void Known_model_is_valid(string provider, string model)
    {
        var result = ModelCatalog.Validate(provider, model);
        result.IsValid.ShouldBeTrue();
        result.Message.ShouldBeNull();
    }

    // ── Deprecated known models — still valid, but warn with a replacement ──

    [TestCase("openai", "gpt-4.1", "gpt-5.6-sol")]
    [TestCase("openai", "gpt-4.1-mini", "gpt-5.6-terra")]
    [TestCase("openai", "o3", "gpt-5.6-sol")]
    [TestCase("openai", "gpt-5", "gpt-5.6-sol")]
    [TestCase("openai", "gpt-5.2", "gpt-5.6-sol")]
    [TestCase("anthropic", "claude-opus-4-1", "claude-opus-4-8")]
    [TestCase("google", "gemini-2.5-pro", "gemini-3.1-pro")]
    [TestCase("google", "gemini-2.5-flash", "gemini-3.5-flash")]
    public void Deprecated_known_model_is_valid_with_replacement_suggestion(
        string provider, string model, string expectedReplacement)
    {
        var result = ModelCatalog.Validate(provider, model);
        result.IsValid.ShouldBeTrue();
        result.Message!.ShouldContain("deprecated");
        result.Message!.ShouldContain(expectedReplacement);
        result.Suggestions.ShouldContain(expectedReplacement);
    }

    // ── Ambiguous family names ───────────────────────────────────────

    [TestCase("anthropic", "sonnet")]
    [TestCase("google", "flash")]
    [TestCase("google", "gemini")]
    public void Ambiguous_family_is_invalid_with_suggestions(string provider, string model)
    {
        var result = ModelCatalog.Validate(provider, model);
        result.IsValid.ShouldBeFalse();
        result.Suggestions.ShouldNotBeEmpty();
        result.Message!.ShouldContain("ambiguous");
    }

    [Test]
    public void Sonnet_suggests_sonnet46_and_sonnet5()
    {
        var result = ModelCatalog.Validate("anthropic", "sonnet");
        result.Suggestions.ShouldContain(Models.Anthropic.Sonnet46);
        result.Suggestions.ShouldContain(Models.Anthropic.Sonnet5);
    }

    // ── Haiku family is single-version again after claude-3-5-haiku was retired ──

    [Test]
    public void Haiku_with_single_version_is_not_ambiguous()
    {
        // "haiku" briefly became ambiguous once claude-haiku-4-5 joined claude-3-5-haiku in the
        // catalog (see Phase 3.2). claude-3-5-haiku has since been retired and removed entirely,
        // leaving claude-haiku-4-5 as the only version again — not ambiguous, but still not a
        // known model itself (the known model is "claude-haiku-4-5", not "haiku").
        var result = ModelCatalog.Validate("anthropic", "haiku");
        result.IsValid.ShouldBeTrue();
        result.Message.ShouldNotBeNull(); // warning: not in catalog
    }

    // No test exercises ModelStatus.Retired end-to-end: a Retired model is removed from the
    // catalog entirely rather than kept as a Retired entry (see
    // docs/reference/model-deprecations.md), so there is no real constant to validate against.
    // The Retired branch in ModelCatalog.Validate() is
    // structurally identical to the tested Deprecated branch (same shape, IsValid flips to false) —
    // lower risk than leaving it silently untested would suggest.

    // ── Pinned versions pass through ─────────────────────────────────

    [TestCase("anthropic", "claude-sonnet-4-6-20260201")]
    [TestCase("anthropic", "claude-haiku-4-5-20251001")]
    public void Pinned_version_passes_through_with_warning(string provider, string model)
    {
        var result = ModelCatalog.Validate(provider, model);
        result.IsValid.ShouldBeTrue();
        result.Message.ShouldNotBeNull(); // not in catalog, but allowed
    }

    // ── Unknown provider ─────────────────────────────────────────────

    [Test]
    public void Unknown_provider_passes_through()
    {
        var result = ModelCatalog.Validate("ollama", "llama3.2");
        result.IsValid.ShouldBeTrue();
        result.Message!.ShouldContain("Unknown provider");
    }

    // ── Unknown model for known provider ─────────────────────────────

    [Test]
    public void Unknown_model_for_known_provider_warns_with_suggestions()
    {
        var result = ModelCatalog.Validate("openai", "gpt-99");
        result.IsValid.ShouldBeTrue();
        result.Message.ShouldNotBeNull();
        result.Suggestions.ShouldNotBeEmpty();
    }

    // ── GetModels / GetProviders ─────────────────────────────────────

    [Test]
    public void GetProviders_returns_known_providers()
    {
        var providers = ModelCatalog.GetProviders();
        providers.ShouldContain("openai");
        providers.ShouldContain("anthropic");
        providers.ShouldContain("google");
    }

    [Test]
    public void GetModels_returns_known_models_for_provider()
    {
        var models = ModelCatalog.GetModels("anthropic");
        models.ShouldContain(Models.Anthropic.Sonnet5);
        models.ShouldContain(Models.Anthropic.Fable5);
        models.ShouldContain(Models.Anthropic.Haiku45);
    }

    [Test]
    public void GetModels_returns_empty_for_unknown_provider()
    {
        ModelCatalog.GetModels("ollama").ShouldBeEmpty();
    }

    // ── Case insensitivity ───────────────────────────────────────────

    [Test]
    public void Validation_is_case_insensitive()
    {
        ModelCatalog.Validate("Anthropic", "Claude-Sonnet-4-6").IsValid.ShouldBeTrue();
        ModelCatalog.Validate("OPENAI", "GPT-4.1-MINI").IsValid.ShouldBeTrue();
    }
}
