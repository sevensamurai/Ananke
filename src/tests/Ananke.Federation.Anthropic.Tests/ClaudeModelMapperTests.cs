using Ananke.Design;
using Shouldly;

namespace Ananke.Federation.Anthropic.Tests;

[TestFixture]
public sealed class ClaudeModelMapperTests
{
    private ClaudeModelMapper _mapper = null!;

    [SetUp]
    public void SetUp() => _mapper = new ClaudeModelMapper();

    [TestCase("anthropic", "claude-opus-4", "claude-opus-4")]
    [TestCase("anthropic", "claude-sonnet-4", "claude-sonnet-4")]
    [TestCase("anthropic", "claude-3-5-sonnet", "claude-3-5-sonnet")]
    [TestCase("anthropic", "claude-3-5-haiku", "claude-3-5-haiku")]
    [TestCase("openai", "gpt-4.1", "claude-sonnet-5")]
    [TestCase("openai", "gpt-4.1-mini", "claude-haiku-4-5")]
    [TestCase("openai", "gpt-4.1-nano", "claude-haiku-4-5")]
    [TestCase("openai", "gpt-4o", "claude-sonnet-5")]
    [TestCase("openai", "gpt-4o-mini", "claude-haiku-4-5")]
    [TestCase("openai", "o3", "claude-sonnet-5")]
    [TestCase("openai", "o3-mini", "claude-haiku-4-5")]
    [TestCase("openai", "o4-mini", "claude-haiku-4-5")]
    [TestCase("google", "gemini-2.5-pro", "claude-sonnet-5")]
    [TestCase("google", "gemini-2.5-flash", "claude-haiku-4-5")]
    public void Known_models_map_correctly(string provider, string model, string expected)
    {
        var result = _mapper.Map(new ModelDefinition { Provider = provider, Model = model });
        result.ShouldBe(expected);
    }

    [Test]
    public void Unknown_model_returns_null()
    {
        var result = _mapper.Map(new ModelDefinition { Provider = "unknown", Model = "mystery-v1" });
        result.ShouldBeNull();
    }

    [Test]
    public void Anthropic_provider_passes_through_unknown_model()
    {
        var result = _mapper.Map(new ModelDefinition { Provider = "anthropic", Model = "claude-4-opus" });
        result.ShouldBe("claude-4-opus");
    }

    [Test]
    public void Claude_provider_passes_through()
    {
        var result = _mapper.Map(new ModelDefinition { Provider = "claude", Model = "claude-sonnet-4" });
        result.ShouldBe("claude-sonnet-4");
    }

    [Test]
    public void Mapping_is_case_insensitive()
    {
        var result = _mapper.Map(new ModelDefinition { Provider = "OpenAI", Model = "GPT-4.1" });
        result.ShouldBe("claude-sonnet-5");
    }
}
