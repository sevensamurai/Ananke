using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge;
using Shouldly;

namespace Ananke.Postgres.Tests;

/// <summary>
/// Keyword search on a table of 603 rows under <c>simple</c>, with no stop list: 300 English and 300 Spanish rows from
/// templates, an English answer, a Spanish answer, and a long chunk dense with words common in both languages. The
/// templates put <c>is</c>, <c>the</c>, <c>el</c> and <c>que</c> in half the rows and <c>what</c>, <c>and</c>,
/// <c>plan</c>, <c>es</c>, <c>la</c>, <c>y</c> in a third; <c>backend</c>, <c>telemetry</c> and <c>telemetría</c> are
/// only in the answers.
/// </summary>
[TestFixture]
public class PostgresKeywordSearchTests
{
    private const string EnglishQuery = "what is the telemetry backend";
    private const string SpanishQuery = "cuál es el backend de telemetría";
    private const string AnswerEn = "The telemetry backend is SigNoz, which replaced Grafana Cloud";
    private const string AnswerEs = "El backend de telemetría es SigNoz, que reemplazó a Grafana Cloud";

    private static readonly string LongCommon = string.Concat(Enumerable.Repeat(
        "what is the plan and what is the schedule, el plan es lo que es y la fecha es la que es. ", 20));

    private static readonly float[] Near = [1f, 0f, 0f];
    private static readonly float[] Second = [0.7f, 0.7f, 0f];
    private static readonly float[] Far = [0f, 1f, 0f];
    private static readonly float[] Opposite = [-1f, 0f, 0f];

    // Dense search finds the long chunk nearest the English query and the English answer second; the Spanish answer is
    // under the score threshold, and every other row is equally far.
    private static readonly Dictionary<string, float[]> Vectors = new()
    {
        [EnglishQuery] = Near,
        [LongCommon] = Near,
        [AnswerEn] = Second,
        [AnswerEs] = Opposite,
    };

    private string _table = null!;

    [OneTimeSetUp]
    public async Task FillAsync()
    {
        _table = PostgresDatabase.NewTable("keywords");
        await Store().UpsertAsync(Rows());
    }

    [Test]
    public async Task SearchAsync_SparseMode_AQueryWithCommonWords_RanksTheAnswerFirst()
    {
        var results = await Store().SearchAsync(EnglishQuery, new SearchOptions { Mode = SearchMode.Sparse, TopK = 10 });

        results[0].Id.ShouldBe("answer-en");
        RankOf(results, "long-common").ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task SearchAsync_SparseMode_ASpanishQueryWithCommonWords_RanksTheSpanishAnswerFirst()
    {
        var results = await Store().SearchAsync(SpanishQuery, new SearchOptions { Mode = SearchMode.Sparse, TopK = 10 });

        results[0].Id.ShouldBe("answer-es");
        RankOf(results, "long-common").ShouldBeGreaterThan(0);
    }

    [Test]
    public async Task SearchAsync_SparseMode_EveryTermScored_RanksTheAnswerAboveTheLongChunk()
    {
        // Every term counts as rare, so every row holding any query term is scored: BM25 alone orders them.
        var store = Store(new PostgresKnowledgeOptions { Table = _table, RareTermShare = 1 });

        var english = await store.SearchAsync(EnglishQuery, new SearchOptions { Mode = SearchMode.Sparse, TopK = 1000 });
        var spanish = await store.SearchAsync(SpanishQuery, new SearchOptions { Mode = SearchMode.Sparse, TopK = 1000 });

        english.Count.ShouldBe(203);
        spanish.Count.ShouldBe(303);
        english[0].Id.ShouldBe("answer-en");
        RankOf(english, "answer-en").ShouldBeLessThan(RankOf(english, "long-common"));
        spanish[0].Id.ShouldBe("answer-es");
        RankOf(spanish, "answer-es").ShouldBeLessThan(RankOf(spanish, "long-common"));
    }

    [Test]
    public async Task SearchAsync_SparseMode_ScoresOnlyTheRowsHoldingARareTerm()
    {
        var english = await Store().SearchAsync(EnglishQuery, new SearchOptions { Mode = SearchMode.Sparse, TopK = 1000 });
        var spanish = await Store().SearchAsync(SpanishQuery, new SearchOptions { Mode = SearchMode.Sparse, TopK = 1000 });

        english.Select(r => r.Id).ShouldBe(["answer-en", "answer-es"]);
        spanish.Select(r => r.Id).ShouldBe(["answer-es", "answer-en"]);
    }

    [Test]
    public async Task SearchAsync_SparseMode_NoRareTerm_RequiresEveryTerm()
    {
        // what, is, the and plan are all common: only the long chunk and the English rows holding all four (every
        // sixth) are scored.
        var results = await Store().SearchAsync("what is the plan", new SearchOptions { Mode = SearchMode.Sparse, TopK = 1000 });

        results.Count.ShouldBe(51);
        results.ShouldAllBe(r => r.Id == "long-common" || r.Id.StartsWith("en-"));
    }

    [Test]
    public async Task SearchAsync_SparseMode_ATermNoRowHolds_IsLeftOut()
    {
        var results = await Store().SearchAsync("telemetry xyzzy", new SearchOptions { Mode = SearchMode.Sparse, TopK = 10 });

        results.Select(r => r.Id).ShouldBe(["answer-en"]);
    }

    [Test]
    public async Task SearchAsync_HybridMode_FusesTheNewKeywordRanking()
    {
        // Dense puts the long chunk first and the English answer second; keyword search finds the answer and not the
        // long chunk, so the answer comes first.
        var results = await Store().SearchAsync(EnglishQuery, new SearchOptions { Mode = SearchMode.Hybrid, TopK = 3 });

        results[0].Id.ShouldBe("answer-en");
        results[1].Id.ShouldBe("long-common");
    }

    [Test]
    public void Constructor_ARareTermShareOutsideZeroToOne_IsRefused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Store(new PostgresKnowledgeOptions { Table = _table, RareTermShare = 0 }));
        Should.Throw<ArgumentOutOfRangeException>(() => Store(new PostgresKnowledgeOptions { Table = _table, RareTermShare = 1.5 }));
    }

    private static int RankOf(IReadOnlyList<KnowledgeChunk> results, string id)
    {
        var rank = results.Select(r => r.Id).ToList().IndexOf(id);
        return rank < 0 ? int.MaxValue : rank;
    }

    private PostgresKnowledgeStore Store(PostgresKnowledgeOptions? options = null) =>
        new(PostgresDatabase.Require(), new FixedEmbedder(Vectors, new EmbeddingModelIdentity("test", "fixed", 3)),
            options ?? new PostgresKnowledgeOptions { Table = _table });

    private static List<KnowledgeDocument> Rows()
    {
        var rows = new List<KnowledgeDocument>();
        for (var i = 0; i < 300; i++)
        {
            var text = $"Row {i} about release {i % 40}";
            if (i % 2 == 0)
                text += " the schedule is set";
            if (i % 3 == 0)
                text += " what and plan";
            rows.Add(Row($"en-{i:000}", text));
        }

        for (var i = 0; i < 300; i++)
        {
            var text = $"Fila {i} de la versión {i % 40}";
            if (i % 2 == 0)
                text += " el plan que sigue";
            if (i % 3 == 0)
                text += " es lo que las y";
            rows.Add(Row($"es-{i:000}", text));
        }

        rows.Add(Row("answer-en", AnswerEn));
        rows.Add(Row("answer-es", AnswerEs));
        rows.Add(Row("long-common", LongCommon));
        return rows;
    }

    private static KnowledgeDocument Row(string id, string text)
    {
        Vectors.TryAdd(text, Far);
        return new KnowledgeDocument { Id = id, Text = text };
    }
}
