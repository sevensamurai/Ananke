using Ananke.Orchestration.Conformance.NUnit;

namespace Ananke.Orchestration.Bedrock.Tests;

/// <summary>
/// Bedrock needs a different question asked of it.
/// </summary>
/// <remarks>
/// Bedrock ships <b>no wire code</b> — it is auth and endpoint construction, and requests are served
/// by the OpenAI and Anthropic adapters against Bedrock's compatible endpoints).
/// So there is no Bedrock adapter type for a coverage gate to find, and the useful assertion is that
/// this stays true: the day someone adds one, it must arrive with a conformance fixture rather than
/// slipping past a gate that had nothing to check.
/// <para>
/// The composition itself is covered by <see cref="BedrockChatCompletionsConformanceTests"/> and
/// <see cref="BedrockMessagesConformanceTests"/>, which run both routes end to end.
/// </para>
/// </remarks>
[TestFixture]
public sealed class BedrockConformanceCoverageTests
{
    [Test]
    public void Bedrock_ShipsNoAdapterType_SoTheCompositionFixturesAreTheWholeStory() =>
        ConformanceCoverage.AssertShipsNoAdapter(
            typeof(BedrockAgentModel).Assembly,
            "Bedrock is auth and endpoint construction only)");
}
