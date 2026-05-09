using Shouldly;

namespace Ananke.Design.Tests;

[TestFixture]
public sealed class ModelCatalogTests
{
    // ── Valid known models ────────────────────────────────────────────

    [TestCase("openai", "gpt-4.1")]
    [TestCase("openai", "gpt-4.1-mini")]
    [TestCase("openai", "o3")]
    [TestCase("anthropic", "claude-sonnet-4")]
    [TestCase("anthropic", "claude-opus-4")]
    [TestCase("anthropic", "claude-3-5-haiku")]
    [TestCase("google", "gemini-2.5-pro")]
    [TestCase("google", "gemini-2.5-flash")]
    public void Known_model_is_valid(string provider, string model)
    {
        var result = ModelCatalog.Validate(provider, model);
        result.IsValid.ShouldBeTrue();
        result.Message.ShouldBeNull();
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
    public void Sonnet_suggests_sonnet4_and_sonnet35()
    {
        var result = ModelCatalog.Validate("anthropic", "sonnet");
        result.Suggestions.ShouldContain(Models.Anthropic.Sonnet4);
        result.Suggestions.ShouldContain(Models.Anthropic.Sonnet35);
    }

    // ── Single-member family is not ambiguous ────────────────────────

    [Test]
    public void Haiku_with_single_version_is_not_ambiguous()
    {
        var result = ModelCatalog.Validate("anthropic", "haiku");
        // haiku has only one version, so it's not ambiguous — but it's not a known model either
        // (the known model is "claude-3-5-haiku", not "haiku")
        result.IsValid.ShouldBeTrue();
        result.Message.ShouldNotBeNull(); // warning: not in catalog
    }

    // ── Pinned versions pass through ─────────────────────────────────

    [TestCase("anthropic", "claude-sonnet-4-20250514")]
    [TestCase("anthropic", "claude-3-5-haiku-20241022")]
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
        models.ShouldContain(Models.Anthropic.Sonnet4);
        models.ShouldContain(Models.Anthropic.Opus4);
        models.ShouldContain(Models.Anthropic.Haiku35);
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
        ModelCatalog.Validate("Anthropic", "Claude-Sonnet-4").IsValid.ShouldBeTrue();
        ModelCatalog.Validate("OPENAI", "GPT-4.1-MINI").IsValid.ShouldBeTrue();
    }
}
