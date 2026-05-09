using Ananke.Design;
using Ananke.Federation.Google;
using Shouldly;

namespace Ananke.Federation.Google.Tests;

[TestFixture]
public sealed class VertexAIModelMapperTests
{
    private VertexAIModelMapper _mapper = null!;

    [SetUp]
    public void SetUp() => _mapper = new VertexAIModelMapper();

    [TestCase("openai", "gpt-4.1",       "gemini-3.1-pro")]
    [TestCase("openai", "gpt-4.1-mini",  "gemini-3.1-flash")]
    [TestCase("openai", "gpt-4.1-nano",  "gemini-2.0-flash-lite")]
    [TestCase("openai", "gpt-4o",        "gemini-3.1-pro")]
    [TestCase("openai", "gpt-4o-mini",   "gemini-3.1-flash")]
    [TestCase("openai", "o3",            "gemini-3.1-pro")]
    [TestCase("openai", "o3-mini",       "gemini-3.1-flash")]
    [TestCase("openai", "o4-mini",       "gemini-3.1-flash")]
    [TestCase("anthropic", "claude-opus-4",    "gemini-3.1-pro")]
    [TestCase("anthropic", "claude-sonnet-4",  "gemini-3.1-pro")]
    [TestCase("anthropic", "claude-3-5-sonnet","gemini-3.1-pro")]
    [TestCase("anthropic", "claude-3-5-haiku", "gemini-3.1-flash")]
    [TestCase("google", "gemini-3.1-pro",   "gemini-3.1-pro")]
    [TestCase("google", "gemini-3.1-flash", "gemini-3.1-flash")]
    [TestCase("google", "gemini-2.5-pro",   "gemini-2.5-pro")]
    [TestCase("google", "gemini-2.5-flash", "gemini-2.5-flash")]
    [TestCase("google", "gemini-2.0-flash", "gemini-2.0-flash")]
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
    public void Google_provider_passes_through_unknown_model()
    {
        var result = _mapper.Map(new ModelDefinition { Provider = "google", Model = "gemini-3.0-ultra" });
        result.ShouldBe("gemini-3.0-ultra");
    }

    [Test]
    public void VertexAI_provider_passes_through()
    {
        var result = _mapper.Map(new ModelDefinition { Provider = "vertex-ai", Model = "gemini-2.5-flash" });
        result.ShouldBe("gemini-2.5-flash");
    }

    [Test]
    public void Mapping_is_case_insensitive()
    {
        var result = _mapper.Map(new ModelDefinition { Provider = "OpenAI", Model = "GPT-4.1-Mini" });
        result.ShouldBe("gemini-3.1-flash");
    }
}
