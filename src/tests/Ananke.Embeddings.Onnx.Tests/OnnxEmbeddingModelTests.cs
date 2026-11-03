using Ananke.Abstractions.Agents;
using Shouldly;

namespace Ananke.Embeddings.Onnx.Tests;

/// <summary>
/// <see cref="OnnxEmbeddingModel"/> on <c>Fixtures/tiny-bert</c>: a 13-piece vocabulary and a "model" whose output for
/// each token is a fixed row of a lookup table (<c>make_tiny_model.py</c>). <c>[CLS]</c> (id 2) is [2, 1, 0, 0],
/// <c>[SEP]</c> (id 3) is [3, 1, 0, 0], and every other id i is [0, 0, i, 1], so each vector can be worked out by hand.
/// </summary>
[TestFixture]
public class OnnxEmbeddingModelTests
{
    private static readonly string TinyBert = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "tiny-bert");

    private static OnnxEmbeddingModel Model(OnnxPooling pooling = OnnxPooling.Cls, bool normalize = true, int maxTokens = 512, int batchSize = 16) =>
        new(new OnnxEmbeddingOptions
        {
            ModelDirectory = TinyBert,
            Pooling = pooling,
            Normalize = normalize,
            MaxTokens = maxTokens,
            BatchSize = batchSize,
        });

    [Test]
    public void Ids_SplitsWordsAndPunctuationIntoTheVocabularysPieces()
    {
        using var model = Model();

        // [CLS] core . identity is built on open ##iddict [SEP]
        model.Ids("Core.Identity is built on OpenIddict").ShouldBe([2, 4, 5, 6, 7, 8, 9, 10, 11, 3]);
    }

    [Test]
    public void Ids_AWordNotInTheVocabulary_IsUnknown() =>
        Model().Ids("zebra").ShouldBe([2, 1, 3]);

    [Test]
    public void Ids_ALongText_IsCutToMaxTokensWithItsSpecialTokens() =>
        Model(maxTokens: 4).Ids("core identity is built").ShouldBe([2, 4, 6, 3]);

    [Test]
    public async Task EmbedAsync_ClsPooling_TakesTheFirstTokensVectorNormalized()
    {
        using var model = Model(OnnxPooling.Cls);

        var vector = (await model.EmbedAsync("core identity")).ToArray();

        vector.ShouldBe(Unit(2, 1, 0, 0), tolerance: 1e-5);
    }

    [Test]
    public async Task EmbedAsync_MeanPooling_AveragesTheTextsTokens()
    {
        using var model = Model(OnnxPooling.Mean, normalize: false);

        var vector = (await model.EmbedAsync("core identity")).ToArray();

        // ([CLS] + core + identity + [SEP]) / 4
        vector.ShouldBe([5f / 4, 2f / 4, 10f / 4, 2f / 4], tolerance: 1e-5);
    }

    [Test]
    public async Task EmbedBatchAsync_MeanPooling_IgnoresTheBatchsPadding()
    {
        using var model = Model(OnnxPooling.Mean);

        var alone = (await model.EmbedAsync("core")).ToArray();
        var batch = await model.EmbedBatchAsync(["core", "core identity is built on openiddict"]);

        batch[0].ToArray().ShouldBe(alone, tolerance: 1e-5);
    }

    [Test]
    public async Task EmbedBatchAsync_MoreTextsThanABatch_KeepsTheirOrder()
    {
        using var model = Model(OnnxPooling.Mean, normalize: false, batchSize: 2);

        var vectors = await model.EmbedBatchAsync(["core", "identity", "built"]);

        vectors.Select(v => v.Span[2]).ShouldBe([4f / 3, 6f / 3, 8f / 3], tolerance: 1e-5);
    }

    [Test]
    public void Identity_NamesTheModelAndItsWidth()
    {
        using var model = new OnnxEmbeddingModel(new OnnxEmbeddingOptions { ModelDirectory = TinyBert, Revision = "test" });

        model.Identity.ShouldBe(new EmbeddingModelIdentity("onnx", "tiny-bert", 4, "test"));
    }

    [Test]
    public void Constructor_NoModelFile_Throws() =>
        Should.Throw<FileNotFoundException>(() =>
            new OnnxEmbeddingModel(new OnnxEmbeddingOptions { ModelDirectory = TinyBert, ModelFile = "missing.onnx" }));

    [Test]
    public void Pool_AllZero_StaysZeroWhenNormalized() =>
        OnnxEmbeddingModel.Pool(new float[8], 4, 2, OnnxPooling.Mean, normalize: true).ShouldBe([0f, 0f, 0f, 0f]);

    private static float[] Unit(params float[] v)
    {
        var norm = MathF.Sqrt(v.Sum(x => x * x));
        return [.. v.Select(x => x / norm)];
    }
}
