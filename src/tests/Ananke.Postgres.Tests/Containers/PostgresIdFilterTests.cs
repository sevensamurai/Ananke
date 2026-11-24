using Ananke.Orchestration.Knowledge.Tests;

namespace Ananke.Postgres.Tests.Containers;

/// <summary><see cref="IdFilterCases"/> on <see cref="PostgresKnowledgeStore"/>.</summary>
[TestFixture]
public class PostgresIdFilterTests
{
    private static PostgresKnowledgeStore NewStore() => new(
        PostgresDatabase.Require(), new HashedEmbedder(8), new PostgresKnowledgeOptions { Table = PostgresDatabase.NewTable("ids") });

    [Test]
    public Task ListSearchAndDelete_ById_TouchExactlyThoseChunks() =>
        IdFilterCases.ListSearchAndDelete_ById_TouchExactlyThoseChunks(NewStore());

    [Test]
    public Task LinkedSearch_DocumentsIndexedThroughTheProcessor_ReturnsTheLinkedChunk() =>
        IdFilterCases.LinkedSearch_DocumentsIndexedThroughTheProcessor_ReturnsTheLinkedChunk(NewStore());

    [Test]
    public Task LinkedSearch_LinkedChunkWithNegativeCosine_IsReturned() =>
        IdFilterCases.LinkedSearch_LinkedChunkWithNegativeCosine_IsReturned(embedder => new PostgresKnowledgeStore(
            PostgresDatabase.Require(), embedder, new PostgresKnowledgeOptions { Table = PostgresDatabase.NewTable("ids") }));
}
