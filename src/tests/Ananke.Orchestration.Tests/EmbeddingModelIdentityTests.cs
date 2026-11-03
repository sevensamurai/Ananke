using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Google;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Orchestration.OpenAI;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Each embedding model says which model makes its vectors, so a store can tell when it is opened with another.
/// None of these calls a provider.
/// </summary>
[TestFixture]
public class EmbeddingModelIdentityTests
{
    [Test]
    public void Identity_OpenAI_NamesTheModel()
    {
        var identity = OpenAIEmbeddingModel.Create("key", "text-embedding-3-large").Identity;

        identity.ShouldBe(new EmbeddingModelIdentity("openai", "text-embedding-3-large"));
    }

    [Test]
    public void Identity_OpenAICompatibleEndpoint_NamesTheEndpointHost()
    {
        var identity = OpenAIEmbeddingModel.Create("key", "nomic-embed-text", new Uri("http://localhost:11434/v1")).Identity;

        identity.ShouldBe(new EmbeddingModelIdentity("localhost", "nomic-embed-text"));
    }

    [Test]
    public void Identity_Gemini_NamesTheModel()
    {
        var identity = GeminiEmbeddingModel.Create("key", "text-embedding-004").Identity;

        identity.ShouldBe(new EmbeddingModelIdentity("google", "text-embedding-004"));
    }

    [Test]
    public void Identity_InMemoryEmbedder_NamesItsDimensions()
    {
        var identity = new InMemoryEmbedder(32).Identity;

        identity.ShouldBe(new EmbeddingModelIdentity("ananke", "in-memory-hash", Dimensions: 32));
    }

    [Test]
    public void Identity_AnEmbedderThatSaysNothing_IsNull()
    {
        IEmbeddingModel embedder = new Silent();

        embedder.Identity.ShouldBeNull();
    }

    [Test]
    public void ToString_NamesEveryPartThatIsKnown()
    {
        new EmbeddingModelIdentity("onnx", "bge-small-en-v1.5", 384, "5c38ec7").ToString()
            .ShouldBe("onnx/bge-small-en-v1.5@5c38ec7 (384)");
        new EmbeddingModelIdentity("openai", "text-embedding-3-small").ToString()
            .ShouldBe("openai/text-embedding-3-small");
    }

    private sealed class Silent : IEmbeddingModel
    {
        public Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken ct = default) =>
            Task.FromResult<ReadOnlyMemory<float>>(new float[] { 1f });

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
            IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>([.. texts.Select(_ => (ReadOnlyMemory<float>)new float[] { 1f })]);
    }
}
