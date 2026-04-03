using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class InMemoryEmbedderTests
{
    private readonly InMemoryEmbedder _embedder = new();

    [Test]
    public async Task EmbedAsync_ReturnsDeterministicVector()
    {
        var a = await _embedder.EmbedAsync("hello world");
        var b = await _embedder.EmbedAsync("hello world");

        a.Span.SequenceEqual(b.Span).ShouldBeTrue();
    }

    [Test]
    public async Task EmbedAsync_DefaultDims_Returns64()
    {
        var vec = await _embedder.EmbedAsync("test");

        vec.Length.ShouldBe(64);
    }

    [Test]
    public async Task Constructor_CustomDims_ReturnsCorrectLength()
    {
        var embedder = new InMemoryEmbedder(dims: 128);

        var vec = await embedder.EmbedAsync("test");

        vec.Length.ShouldBe(128);
    }

    [Test]
    public void Constructor_ZeroDims_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new InMemoryEmbedder(dims: 0));
    }

    [Test]
    public async Task EmbedAsync_NormalizesVector()
    {
        var vec = await _embedder.EmbedAsync("some words to embed");
        var span = vec.Span;

        var norm = MathF.Sqrt(span.ToArray().Sum(v => v * v));
        norm.ShouldBe(1f, tolerance: 1e-5f);
    }

    [Test]
    public async Task EmbedBatchAsync_ReturnsOneVectorPerInput()
    {
        var texts = new List<string> { "alpha", "beta", "gamma" };

        var results = await _embedder.EmbedBatchAsync(texts);

        results.Count.ShouldBe(3);
    }

    [Test]
    public async Task EmbedBatchAsync_MatchesSingleEmbed()
    {
        var text = "consistent result";
        var single = await _embedder.EmbedAsync(text);
        var batch = await _embedder.EmbedBatchAsync([text]);

        single.Span.SequenceEqual(batch[0].Span).ShouldBeTrue();
    }

    [Test]
    public async Task EmbedAsync_DifferentTexts_ProduceDifferentVectors()
    {
        var a = await _embedder.EmbedAsync("cats are great");
        var b = await _embedder.EmbedAsync("quantum physics theory");

        a.Span.SequenceEqual(b.Span).ShouldBeFalse();
    }
}
