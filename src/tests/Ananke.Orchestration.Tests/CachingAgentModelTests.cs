using System.Runtime.CompilerServices;
using Ananke.Abstractions.Distributed;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class CachingAgentModelTests
{
    private InMemoryDistributedLock _cache = null!;
    private CountingModel _inner = null!;

    [SetUp]
    public void SetUp()
    {
        _cache = new InMemoryDistributedLock();
        _inner = new CountingModel("hello world");
    }

    [TearDown]
    public ValueTask TearDown() => _cache.DisposeAsync();

    // ── Cache miss → hit ─────────────────────────────────────────

    [Test]
    public async Task GenerateAsync_CacheMiss_CallsInnerAndCaches()
    {
        var caching = new CachingAgentModel(_inner, _cache, TimeSpan.FromMinutes(5));

        var response = await caching.GenerateAsync(MakeRequest("What?"));

        response.Text.ShouldBe("hello world");
        _inner.GenerateCallCount.ShouldBe(1);
    }

    [Test]
    public async Task GenerateAsync_CacheHit_ReturnsFromCacheWithoutCallingInner()
    {
        var caching = new CachingAgentModel(_inner, _cache, TimeSpan.FromMinutes(5));
        var request = MakeRequest("What?");

        await caching.GenerateAsync(request); // warm cache
        var response = await caching.GenerateAsync(request); // cache hit

        response.Text.ShouldBe("hello world");
        _inner.GenerateCallCount.ShouldBe(1); // only called once
    }

    [Test]
    public async Task GenerateAsync_DifferentRequests_SeparateCacheEntries()
    {
        var caching = new CachingAgentModel(_inner, _cache, TimeSpan.FromMinutes(5));

        await caching.GenerateAsync(MakeRequest("Request A"));
        await caching.GenerateAsync(MakeRequest("Request B"));

        _inner.GenerateCallCount.ShouldBe(2); // different keys
    }

    // ── Tool-call responses not cached ───────────────────────────

    [Test]
    public async Task GenerateAsync_ToolCallResponse_NotCached()
    {
        var toolModel = new CountingModel("text", withToolCalls: true);
        var caching = new CachingAgentModel(toolModel, _cache, TimeSpan.FromMinutes(5));
        var request = MakeRequest("Use a tool");

        await caching.GenerateAsync(request);
        await caching.GenerateAsync(request);

        toolModel.GenerateCallCount.ShouldBe(2); // not cached
    }

    // ── Streaming: cache miss → hit ──────────────────────────────

    [Test]
    public async Task GenerateStreamAsync_CacheMiss_StreamsFromInnerAndCaches()
    {
        var caching = new CachingAgentModel(_inner, _cache, TimeSpan.FromMinutes(5));
        var request = MakeRequest("Stream this");

        var chunks = await CollectTextDeltas(caching.GenerateStreamAsync(request));
        chunks.ShouldBe(["hello", " world"]);

        _inner.StreamCallCount.ShouldBe(1);
    }

    [Test]
    public async Task GenerateStreamAsync_CacheHit_EmitsCachedResponseAsSingleChunk()
    {
        var caching = new CachingAgentModel(_inner, _cache, TimeSpan.FromMinutes(5));
        var request = MakeRequest("Stream this");

        await CollectTextDeltas(caching.GenerateStreamAsync(request)); // warm
        var chunks = await CollectTextDeltas(caching.GenerateStreamAsync(request)); // hit

        // Cached response emitted as a single text delta
        chunks.ShouldBe(["hello world"]);
        _inner.StreamCallCount.ShouldBe(1);
    }

    // ── System prompt affects cache key ──────────────────────────

    [Test]
    public async Task GenerateAsync_DifferentSystemPrompt_SeparateCacheEntries()
    {
        var caching = new CachingAgentModel(_inner, _cache, TimeSpan.FromMinutes(5));
        var r1 = new AgentRequest
        {
            SystemPrompt = "You are helpful.",
            Messages = [AgentMessage.User("hi")]
        };
        var r2 = new AgentRequest
        {
            SystemPrompt = "You are terse.",
            Messages = [AgentMessage.User("hi")]
        };

        await caching.GenerateAsync(r1);
        await caching.GenerateAsync(r2);

        _inner.GenerateCallCount.ShouldBe(2);
    }

    // ── TimeProvider injection (no ambient clocks) ────

    [Test]
    public async Task GenerateAsync_InjectedClockAdvancedPastTtl_TreatsEntryAsExpired()
    {
        var clock = new FakeTimeProvider();
        var caching = new CachingAgentModel(_inner, _cache, TimeSpan.FromMinutes(5), timeProvider: clock);
        var request = MakeRequest("What?");

        await caching.GenerateAsync(request); // warm cache
        clock.Advance(TimeSpan.FromMinutes(6)); // past TTL — no real wait

        await caching.GenerateAsync(request);

        _inner.GenerateCallCount.ShouldBe(2); // second call was a miss, not served from cache
    }

    [Test]
    public async Task GenerateAsync_InjectedClockWithinTtl_StillServesFromCache()
    {
        var clock = new FakeTimeProvider();
        var caching = new CachingAgentModel(_inner, _cache, TimeSpan.FromMinutes(5), timeProvider: clock);
        var request = MakeRequest("What?");

        await caching.GenerateAsync(request); // warm cache
        clock.Advance(TimeSpan.FromMinutes(4)); // still within TTL

        await caching.GenerateAsync(request);

        _inner.GenerateCallCount.ShouldBe(1); // second call was a hit
    }

    // ── Cache scope prevents cross-model contamination (F-1) ─────

    [Test]
    public async Task GenerateAsync_TwoModelsSharedCache_DoNotCrossContaminate()
    {
        var haiku = new CountingModel("haiku answer");
        var opus = new CountingModel("opus answer");

        var haikuWrapper = new CachingAgentModel(haiku, _cache, TimeSpan.FromMinutes(5), cacheScope: "haiku");
        var opusWrapper = new CachingAgentModel(opus, _cache, TimeSpan.FromMinutes(5), cacheScope: "opus");

        var request = MakeRequest("Same prompt to both");

        var haikuResponse = await haikuWrapper.GenerateAsync(request);
        var opusResponse = await opusWrapper.GenerateAsync(request);

        haikuResponse.Text.ShouldBe("haiku answer");
        opusResponse.Text.ShouldBe("opus answer");
        haiku.GenerateCallCount.ShouldBe(1);
        opus.GenerateCallCount.ShouldBe(1);
    }

    [Test]
    public async Task GenerateAsync_SameInnerTypeNoExplicitScope_SharesCacheAcrossInstances()
    {
        // Documents the default-scope limitation called out in the XML doc: inner.GetType().FullName
        // cannot distinguish two instances of the same provider class configured with different
        // models — callers in that situation must pass an explicit cacheScope (see the test above).
        var first = new CountingModel("first answer");
        var second = new CountingModel("second answer");

        var firstWrapper = new CachingAgentModel(first, _cache, TimeSpan.FromMinutes(5));
        var secondWrapper = new CachingAgentModel(second, _cache, TimeSpan.FromMinutes(5));

        var request = MakeRequest("Same prompt, same wrapped type, no explicit scope");

        var firstResponse = await firstWrapper.GenerateAsync(request);
        var secondResponse = await secondWrapper.GenerateAsync(request);

        secondResponse.Text.ShouldBe(firstResponse.Text); // served from firstWrapper's cache entry
        second.GenerateCallCount.ShouldBe(0); // never actually called — collision confirmed
    }

    // ── Validation ───────────────────────────────────────────────

    [Test]
    public void Constructor_ZeroTtl_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new CachingAgentModel(_inner, _cache, TimeSpan.Zero));
    }

    [Test]
    public void Constructor_NullInner_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            new CachingAgentModel(null!, _cache, TimeSpan.FromMinutes(1)));
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static AgentRequest MakeRequest(string text) => new()
    {
        Messages = [AgentMessage.User(text)]
    };

    private static async Task<List<string>> CollectTextDeltas(IAsyncEnumerable<AgentStreamChunk> stream)
    {
        var deltas = new List<string>();
        await foreach (var chunk in stream)
        {
            if (chunk.TextDelta is not null)
                deltas.Add(chunk.TextDelta);
        }
        return deltas;
    }

    /// <summary>
    /// Fake model that counts invocations and returns a fixed response.
    /// </summary>
    private sealed class CountingModel(string text, bool withToolCalls = false) : IStreamingAgentModel
    {
        public int GenerateCallCount { get; private set; }
        public int StreamCallCount { get; private set; }

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            GenerateCallCount++;
            return Task.FromResult(new AgentResponse
            {
                Text = text,
                ToolCalls = withToolCalls
                    ? [new AgentToolCall("tc1", "doStuff", "{}")]
                    : null
            });
        }

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            StreamCallCount++;
            var parts = text.Split(' ');
            var full = new System.Text.StringBuilder();

            for (var i = 0; i < parts.Length; i++)
            {
                var delta = i == 0 ? parts[i] : $" {parts[i]}";
                full.Append(delta);
                await Task.Yield();
                yield return new AgentStreamChunk { TextDelta = delta };
            }

            yield return new AgentStreamChunk
            {
                CompletedResponse = new AgentResponse
                {
                    Text = full.ToString(),
                    ToolCalls = withToolCalls
                        ? [new AgentToolCall("tc1", "doStuff", "{}")]
                        : null
                }
            };
        }
    }
}
