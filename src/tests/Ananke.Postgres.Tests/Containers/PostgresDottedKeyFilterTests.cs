using Ananke.Orchestration.Knowledge.Tests;

namespace Ananke.Postgres.Tests.Containers;

/// <summary><see cref="DottedKeyFilterCases"/> and <see cref="EmptyFilterDeleteCases"/> on <see cref="PostgresKnowledgeStore"/>.</summary>
[TestFixture]
public class PostgresDottedKeyFilterTests
{
    private static PostgresKnowledgeStore NewStore() => new(
        PostgresDatabase.Require(), new HashedEmbedder(8), new PostgresKnowledgeOptions { Table = PostgresDatabase.NewTable("dotted") });

    [Test]
    public Task List_ADottedKey_MatchesTheDocumentHoldingIt() =>
        DottedKeyFilterCases.List_ADottedKey_MatchesTheDocumentHoldingIt(NewStore());

    [Test]
    public Task Delete_ADottedKey_RemovesTheDocumentHoldingIt() =>
        DottedKeyFilterCases.Delete_ADottedKey_RemovesTheDocumentHoldingIt(NewStore());

    [Test]
    public Task Delete_AnEmptyFilter_ThrowsAndDeletesNothing() =>
        EmptyFilterDeleteCases.Delete_AnEmptyFilter_ThrowsAndDeletesNothing(NewStore());
}
