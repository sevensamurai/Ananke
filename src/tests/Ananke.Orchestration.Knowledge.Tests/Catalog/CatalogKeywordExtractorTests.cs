using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge.Catalog;
using Shouldly;

namespace Ananke.Orchestration.Knowledge.Tests.Catalog;

[TestFixture]
public class CatalogKeywordExtractorTests
{
    [Test]
    public void Constructor_NullModel_Throws() =>
        Should.Throw<ArgumentNullException>(() => new CatalogKeywordExtractor(null!));

    [Test]
    public async Task ExtractAsync_BlankText_Throws()
    {
        var extractor = new CatalogKeywordExtractor(new FakeAgentModel("{}"));

        await Should.ThrowAsync<ArgumentException>(() => extractor.ExtractAsync("   "));
    }

    [Test]
    public async Task ExtractAsync_WellFormedResponse_ParsesKeywordsCategoryAndSummary()
    {
        var model = new FakeAgentModel(
            """{"keywords":["async","cancellation"],"category":"software-engineering","summary":"A doc about async patterns."}""");
        var extractor = new CatalogKeywordExtractor(model);

        var result = await extractor.ExtractAsync("some document text");

        result.Keywords.ShouldBe(["async", "cancellation"]);
        result.Category.ShouldBe("software-engineering");
        result.Summary.ShouldBe("A doc about async patterns.");
    }

    [Test]
    public async Task ExtractAsync_MissingFields_DefaultToEmpty()
    {
        var model = new FakeAgentModel("{}");
        var extractor = new CatalogKeywordExtractor(model);

        var result = await extractor.ExtractAsync("text");

        result.Keywords.ShouldBeEmpty();
        result.Category.ShouldBe(string.Empty);
        result.Summary.ShouldBe(string.Empty);
    }

    [Test]
    public async Task ExtractAsync_MalformedJson_FallsBackToEmptyKeywordsAndRawSummary()
    {
        var model = new FakeAgentModel("not json at all");
        var extractor = new CatalogKeywordExtractor(model);

        var result = await extractor.ExtractAsync("text");

        result.Keywords.ShouldBeEmpty();
        result.Category.ShouldBe(string.Empty);
        result.Summary.ShouldBe("not json at all");
    }

    [Test]
    public async Task ExtractAsync_MalformedJsonLongerThan200Chars_SummaryIsTruncated()
    {
        var raw = new string('x', 250);
        var model = new FakeAgentModel(raw);
        var extractor = new CatalogKeywordExtractor(model);

        var result = await extractor.ExtractAsync("text");

        result.Summary.Length.ShouldBe(200);
    }

    [Test]
    public async Task ExtractAsync_NullResponseModel_DefaultsToEmptyJson()
    {
        // response.Text is null: ExtractAsync falls back to "{}", not a throw.
        var model = new FakeAgentModel(null);
        var extractor = new CatalogKeywordExtractor(model);

        var result = await extractor.ExtractAsync("text");

        result.Keywords.ShouldBeEmpty();
        result.Summary.ShouldBe(string.Empty);
    }

    [Test]
    public async Task ExtractAsync_TextLongerThanMaxLength_IsTruncatedBeforeSendingToModel()
    {
        var model = new FakeAgentModel("""{"keywords":[],"category":"","summary":""}""");
        var extractor = new CatalogKeywordExtractor(model, maxTextLength: 10);

        await extractor.ExtractAsync(new string('a', 100));

        model.LastRequestUserContent!.Length.ShouldBe(10);
    }

    [Test]
    public async Task ExtractAsync_KeywordsArrayWithEmptyStrings_AreFilteredOut()
    {
        var model = new FakeAgentModel("""{"keywords":["real","","also-real"],"category":"c","summary":"s"}""");
        var extractor = new CatalogKeywordExtractor(model);

        var result = await extractor.ExtractAsync("text");

        result.Keywords.ShouldBe(["real", "also-real"]);
    }

    private sealed class FakeAgentModel(string? responseText) : IAgentModel
    {
        public string? LastRequestUserContent { get; private set; }

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            LastRequestUserContent = request.Messages[0].Content;
            return Task.FromResult(new AgentResponse { Text = responseText });
        }
    }
}
