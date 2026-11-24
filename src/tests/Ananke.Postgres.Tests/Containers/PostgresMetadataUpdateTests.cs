using Ananke.Orchestration.Knowledge.Tests;

namespace Ananke.Postgres.Tests.Containers;

/// <summary><see cref="MetadataUpdateCases"/> on <see cref="PostgresKnowledgeStore"/>.</summary>
[TestFixture]
public class PostgresMetadataUpdateTests
{
    private static PostgresKnowledgeStore NewStore() => new(
        PostgresDatabase.Require(), new HashedEmbedder(8), new PostgresKnowledgeOptions { Table = PostgresDatabase.NewTable("updates") });

    [Test]
    public Task UpdateMetadata_AsListed_ReplacesTheMetadataAndKeepsTheText() =>
        MetadataUpdateCases.UpdateMetadata_AsListed_ReplacesTheMetadataAndKeepsTheText(NewStore());

    [Test]
    public Task UpdateMetadata_WrittenSinceListed_KeepsTheWrite() =>
        MetadataUpdateCases.UpdateMetadata_WrittenSinceListed_KeepsTheWrite(NewStore());

    [Test]
    public Task UpdateMetadata_DeletedSinceListed_StaysDeleted() =>
        MetadataUpdateCases.UpdateMetadata_DeletedSinceListed_StaysDeleted(NewStore());
}
