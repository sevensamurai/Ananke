using Ananke.Orchestration.Knowledge.Tests;

namespace Ananke.Postgres.Tests.Containers;

/// <summary><see cref="ChunkIdCases"/> on <see cref="PostgresKnowledgeStore"/>.</summary>
[TestFixture]
public class PostgresChunkIdTests
{
    [Test]
    public Task SearchAndList_ADocument_ReturnTheIdUpserted() =>
        ChunkIdCases.SearchAndList_ADocument_ReturnTheIdUpserted(new PostgresKnowledgeStore(
            PostgresDatabase.Require(), new HashedEmbedder(8), new PostgresKnowledgeOptions { Table = PostgresDatabase.NewTable("ids") }));
}
