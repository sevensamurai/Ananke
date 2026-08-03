using System.Reflection;
using Ananke.Abstractions.Providers;
using Ananke.Orchestration.Anthropic.Translators;
using Ananke.Orchestration.Google.Translators;
using Ananke.Orchestration.OpenAI.Translators;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Coherence checks shared by every <see cref="IModelMapper"/> implementation: an already-native
/// id must not be rewritten to a different model, and every id the mapper can produce must be
/// describable via <see cref="IModelMapper.GetCapabilities"/>. Runs via reflection over each
/// mapper's private lookup tables so a new cross-provider entry is checked the moment it's added.
/// </summary>
[TestFixture]
public class ModelMapperCoherenceTests
{
    private static IEnumerable<TestCaseData> Mappers()
    {
        yield return new TestCaseData(new AnthropicModelMapper()).SetName("{m}(Anthropic)");
        yield return new TestCaseData(new OpenAIModelMapper()).SetName("{m}(OpenAI)");
        yield return new TestCaseData(new GeminiModelMapper()).SetName("{m}(Google)");
    }

    [TestCaseSource(nameof(Mappers))]
    public void MapModelId_NativeId_PassesThroughVerbatim(IModelMapper mapper)
    {
        foreach (var nativeId in GetCapabilities(mapper).Keys)
        {
            mapper.MapModelId(nativeId).ShouldBe(nativeId,
                $"{mapper.Platform}: bare native id '{nativeId}' should pass through unchanged");

            var prefixed = $"{mapper.Platform}/{nativeId}";
            mapper.MapModelId(prefixed).ShouldBe(nativeId,
                $"{mapper.Platform}: '{prefixed}' should pass through as '{nativeId}', not be rewritten");
        }
    }

    [TestCaseSource(nameof(Mappers))]
    public void MapModelId_EveryMappedId_HasCapabilitiesEntry(IModelMapper mapper)
    {
        var capabilities = GetCapabilities(mapper);

        foreach (var (logicalId, nativeId) in GetMappings(mapper))
        {
            capabilities.ShouldContainKey(nativeId,
                $"{mapper.Platform}: '{logicalId}' maps to '{nativeId}', which has no Capabilities entry");
        }
    }

    private static Dictionary<string, string> GetMappings(IModelMapper mapper) =>
        (Dictionary<string, string>)GetStaticField(mapper.GetType(), "Mappings");

    private static Dictionary<string, ModelCapabilityFlags> GetCapabilities(IModelMapper mapper) =>
        (Dictionary<string, ModelCapabilityFlags>)GetStaticField(mapper.GetType(), "Capabilities");

    private static object GetStaticField(Type type, string name)
    {
        var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
        field.ShouldNotBeNull($"{type.Name} is expected to declare a private static '{name}' field");
        return field!.GetValue(null)!;
    }
}
