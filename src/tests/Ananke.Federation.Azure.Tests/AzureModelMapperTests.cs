using Ananke.Design;
using Shouldly;

namespace Ananke.Federation.Azure.Tests;

[TestFixture]
public sealed class AzureModelMapperTests
{
    private AzureModelMapper _mapper = null!;

    [SetUp]
    public void SetUp() => _mapper = new AzureModelMapper();

    [TestCase("openai", "gpt-4.1", "gpt-4.1")]
    [TestCase("openai", "gpt-4.1-mini", "gpt-4.1-mini")]
    [TestCase("openai", "gpt-4.1-nano", "gpt-4.1-nano")]
    [TestCase("openai", "gpt-4o", "gpt-4o")]
    [TestCase("openai", "gpt-4o-mini", "gpt-4o-mini")]
    [TestCase("openai", "o3", "o3")]
    [TestCase("openai", "o3-mini", "o3-mini")]
    [TestCase("openai", "o4-mini", "o4-mini")]
    [TestCase("google", "gemini-2.5-pro", "gpt-4.1")]
    [TestCase("google", "gemini-2.5-flash", "gpt-4.1-mini")]
    [TestCase("google", "gemini-2.0-flash", "gpt-4.1-mini")]
    [TestCase("google", "gemini-2.0-flash-lite", "gpt-4.1-nano")]
    [TestCase("anthropic", "claude-opus-4", "gpt-4.1")]
    [TestCase("anthropic", "claude-sonnet-4", "gpt-4.1")]
    [TestCase("anthropic", "claude-3-5-sonnet", "gpt-4.1")]
    [TestCase("anthropic", "claude-3-5-haiku", "gpt-4.1-mini")]
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
    public void OpenAI_provider_passes_through_unknown_model()
    {
        var result = _mapper.Map(new ModelDefinition { Provider = "openai", Model = "gpt-5-turbo" });
        result.ShouldBe("gpt-5-turbo");
    }

    [Test]
    public void Azure_provider_passes_through()
    {
        var result = _mapper.Map(new ModelDefinition { Provider = "azure", Model = "my-custom-deployment" });
        result.ShouldBe("my-custom-deployment");
    }

    [Test]
    public void AzureAI_provider_passes_through()
    {
        var result = _mapper.Map(new ModelDefinition { Provider = "azure-ai", Model = "gpt-4.1-mini" });
        result.ShouldBe("gpt-4.1-mini");
    }

    [Test]
    public void Mapping_is_case_insensitive()
    {
        var result = _mapper.Map(new ModelDefinition { Provider = "OpenAI", Model = "GPT-4.1-Mini" });
        result.ShouldBe("gpt-4.1-mini");
    }
}
