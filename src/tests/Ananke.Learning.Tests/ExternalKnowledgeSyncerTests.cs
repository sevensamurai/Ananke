using Ananke.Learning.Ingestion;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

namespace Ananke.Learning.Tests;

[TestFixture]
public class ExternalKnowledgeSyncerTests
{
    private InMemoryEmbedder _embedder = null!;
    private InMemoryKnowledgeStore _knowledgeStore = null!;

    [SetUp]
    public void SetUp()
    {
        _embedder = new InMemoryEmbedder();
        _knowledgeStore = new InMemoryKnowledgeStore(_embedder);
    }

    // ── Single event sync ────────────────────────────────────────

    [Test]
    public async Task Sync_UpsertsDocumentsToKnowledgeStore()
    {
        var source = new FakeReleaseSource(batch: new ResolvedKnowledgeBatch
        {
            Documents =
            [
                new KnowledgeDocument
                {
                    Id = "release:api-gw:v3.1.2",
                    Text = "api-gateway v3.1.2 deployed. PR #42: refactor connection pool.",
                    Metadata = new Dictionary<string, string>
                    {
                        ["service"] = "api-gateway",
                        ["release"] = "v3.1.2"
                    }
                }
            ]
        });
        var syncer = new ExternalKnowledgeSyncer<FakeReleaseEvent>(
            source, _knowledgeStore);

        var result = await syncer.SyncAsync(new FakeReleaseEvent("v3.1.2"));

        result.DocumentsUpserted.ShouldBe(1);
        result.IsFullySuccessful.ShouldBeTrue();
        _knowledgeStore.Count.ShouldBe(1);
    }

    [Test]
    public async Task Sync_MultipleDocuments_UpsertsAll()
    {
        var source = new FakeReleaseSource(batch: new ResolvedKnowledgeBatch
        {
            Documents =
            [
                new KnowledgeDocument
                {
                    Id = "release:api-gw:v3.1.2",
                    Text = "api-gateway v3.1.2 deployed to au-prod",
                    Metadata = new Dictionary<string, string> { ["service"] = "api-gateway" }
                },
                new KnowledgeDocument
                {
                    Id = "release:worker:v2.8.1",
                    Text = "background-worker v2.8.1 deployed to nz-prod",
                    Metadata = new Dictionary<string, string> { ["service"] = "background-worker" }
                }
            ]
        });
        var syncer = new ExternalKnowledgeSyncer<FakeReleaseEvent>(
            source, _knowledgeStore);

        var result = await syncer.SyncAsync(new FakeReleaseEvent("v3.1.2"));

        result.DocumentsUpserted.ShouldBe(2);
        result.IsFullySuccessful.ShouldBeTrue();
        _knowledgeStore.Count.ShouldBe(2);
    }

    // ── Skip / empty ─────────────────────────────────────────────

    [Test]
    public async Task Sync_EmptyBatch_ReturnsSkipped()
    {
        var source = new FakeReleaseSource(batch: ResolvedKnowledgeBatch.Empty);
        var syncer = new ExternalKnowledgeSyncer<FakeReleaseEvent>(source, _knowledgeStore);

        var result = await syncer.SyncAsync(new FakeReleaseEvent("v3.1.2"));

        result.IsSkipped.ShouldBeTrue();
        result.DocumentsUpserted.ShouldBe(0);
    }

    // ── Resolution failure ───────────────────────────────────────

    [Test]
    public async Task Sync_SourceThrows_ReturnsFailed()
    {
        var source = new FailingSource(new InvalidOperationException("GitHub API down"));
        var syncer = new ExternalKnowledgeSyncer<FakeReleaseEvent>(source, _knowledgeStore);

        var result = await syncer.SyncAsync(new FakeReleaseEvent("v3.1.2"));

        result.Error.ShouldNotBeNull();
        result.Error.ShouldBeOfType<InvalidOperationException>();
        result.DocumentsUpserted.ShouldBe(0);
        result.IsFullySuccessful.ShouldBeFalse();
    }

    // ── Batch sync ───────────────────────────────────────────────

    [Test]
    public async Task SyncBatch_ProcessesAllEvents()
    {
        var callCount = 0;
        string[] descriptions =
        [
            "api-gateway v3.1.2 deployed to au-prod with connection pool refactor",
            "background-worker v2.8.1 deployed with redis client upgrade for OOM fix",
            "iot-ingestion v1.7.0 deployed with mqtt reconnect backoff update"
        ];
        var source = new CallbackSource(_ =>
        {
            var idx = callCount++;
            return Task.FromResult(new ResolvedKnowledgeBatch
            {
                Documents =
                [
                    new KnowledgeDocument
                    {
                        Id = $"release:{idx}",
                        Text = descriptions[idx]
                    }
                ]
            });
        });
        var syncer = new ExternalKnowledgeSyncer<FakeReleaseEvent>(source, _knowledgeStore);

        var result = await syncer.SyncBatchAsync(
        [
            new FakeReleaseEvent("v3.1.2"),
            new FakeReleaseEvent("v2.8.1"),
            new FakeReleaseEvent("v1.7.0")
        ]);

        result.DocumentsUpserted.ShouldBe(3);
        result.IsFullySuccessful.ShouldBeTrue();
        _knowledgeStore.Count.ShouldBe(3);
    }

    [Test]
    public async Task SyncBatch_MixedResults_AggregatesCorrectly()
    {
        var callIndex = 0;
        var source = new CallbackSource(_ =>
        {
            callIndex++;
            return callIndex switch
            {
                1 => Task.FromResult(new ResolvedKnowledgeBatch
                {
                    Documents =
                    [
                        new KnowledgeDocument { Id = "doc1", Text = "ok release" }
                    ]
                }),
                2 => Task.FromResult(ResolvedKnowledgeBatch.Empty),
                3 => throw new InvalidOperationException("API error"),
                _ => Task.FromResult(ResolvedKnowledgeBatch.Empty)
            };
        });
        var syncer = new ExternalKnowledgeSyncer<FakeReleaseEvent>(source, _knowledgeStore);

        var result = await syncer.SyncBatchAsync(
        [
            new FakeReleaseEvent("v1"),
            new FakeReleaseEvent("v2"),
            new FakeReleaseEvent("v3")
        ]);

        result.DocumentsUpserted.ShouldBe(1);
        result.EventsSkipped.ShouldBe(1);
        result.EventsFailed.ShouldBe(1);
    }

    // ── Idempotency ──────────────────────────────────────────────

    [Test]
    public async Task Sync_SameEventTwice_UpsertIsIdempotent()
    {
        var source = new FakeReleaseSource(batch: new ResolvedKnowledgeBatch
        {
            Documents =
            [
                new KnowledgeDocument
                {
                    Id = "release:api-gw:v3.1.2",
                    Text = "api-gateway v3.1.2 deployment details"
                }
            ]
        });
        var syncer = new ExternalKnowledgeSyncer<FakeReleaseEvent>(source, _knowledgeStore);

        await syncer.SyncAsync(new FakeReleaseEvent("v3.1.2"));
        await syncer.SyncAsync(new FakeReleaseEvent("v3.1.2"));

        // UpsertAsync overwrites — count stays 1
        _knowledgeStore.Count.ShouldBe(1);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private sealed record FakeReleaseEvent(string ReleaseTag);

    private sealed class FakeReleaseSource(ResolvedKnowledgeBatch batch)
        : IExternalKnowledgeSource<FakeReleaseEvent>
    {
        public Task<ResolvedKnowledgeBatch> ResolveAsync(
            FakeReleaseEvent @event, CancellationToken ct = default)
            => Task.FromResult(batch);
    }

    private sealed class FailingSource(Exception exception)
        : IExternalKnowledgeSource<FakeReleaseEvent>
    {
        public Task<ResolvedKnowledgeBatch> ResolveAsync(
            FakeReleaseEvent @event, CancellationToken ct = default)
            => throw exception;
    }

    private sealed class CallbackSource(
        Func<FakeReleaseEvent, Task<ResolvedKnowledgeBatch>> callback)
        : IExternalKnowledgeSource<FakeReleaseEvent>
    {
        public Task<ResolvedKnowledgeBatch> ResolveAsync(
            FakeReleaseEvent @event, CancellationToken ct = default)
            => callback(@event);
    }
}
