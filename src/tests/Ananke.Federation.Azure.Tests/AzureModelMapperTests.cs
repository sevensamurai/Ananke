using Ananke.Abstractions.Agents;
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
    public void Anthropic_model_returns_null_pending_current_gen_mapping()
    {
        // Documents a known gap: this mapper has no entries for current-gen Anthropic models
        // (claude-sonnet-5, claude-opus-4-8, etc.) — it never did, and the retired-model cleanup
        // that removed the old claude-opus-4/claude-sonnet-4/claude-3-5-* entries didn't add
        // replacements. See the comment above the Anthropic section in AzureModelMapper.
        var result = _mapper.Map(new ModelDefinition { Provider = "anthropic", Model = "claude-sonnet-5" });
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
        var result = _mapper.Map(new ModelDefinition { Provider = "azure-ai", Model = Models.OpenAI.Gpt54Mini });
        result.ShouldBe(Models.OpenAI.Gpt54Mini);
    }

    [Test]
    public void Mapping_is_case_insensitive()
    {
        var result = _mapper.Map(new ModelDefinition { Provider = "OpenAI", Model = "GPT-4.1-Mini" });
        result.ShouldBe("gpt-4.1-mini");
    }
}
