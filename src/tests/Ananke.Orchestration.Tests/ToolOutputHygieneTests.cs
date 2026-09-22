using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Work-tier hygiene: bounding a tool result as it arrives, and reclaiming an old one that is
/// still being carried.
/// </summary>
/// <remarks>
/// The defect these close is that there was <b>no tool-output size cap anywhere in the
/// orchestration layer</b> — a tool returning a four-megabyte file read put four megabytes into the
/// next request, and into every request after it. The tests are written so that the fix and the
/// guarantee it must not break (an agent with no policy behaves exactly as before) are asserted
/// side by side.
/// </remarks>
[TestFixture]
public class ToolOutputHygieneTests
{
    // ── Capping a result as it arrives ──

    [Test]
    public async Task Cap_ResultWithinTheLimit_IsCarriedWhole()
    {
        var policy = new ToolOutputPolicy { MaxResultChars = 100 };

        var (value, record) = await ToolOutputHygiene.CapAsync(
            policy, "read", new string('x', 100), CancellationToken.None);

        value.Length.ShouldBe(100);
        record.ShouldBeNull();
    }

    [Test]
    public async Task Cap_OversizedResultWithNoStore_TruncatesAndSaysWhatWasLost()
    {
        var policy = new ToolOutputPolicy { MaxResultChars = 100, PreviewChars = 20 };

        var (value, record) = await ToolOutputHygiene.CapAsync(
            policy, "read", new string('x', 5_000), CancellationToken.None);

        record.ShouldNotBeNull();
        record.CharsBefore.ShouldBe(5_000);
        record.CharsAfter.ShouldBe(value.Length);
        record.Recoverable.ShouldBeFalse();

        value.ShouldStartWith(new string('x', 20));
        value.ShouldContain("truncated");
        value.ShouldContain("4,980");
    }

    [Test]
    public async Task Cap_OversizedResultWithAStore_KeepsThePreviewAndTheLocator()
    {
        using var root = new TempRoot();
        var store = new FileToolOutputSpillStore(root.Path);
        var policy = new ToolOutputPolicy { MaxResultChars = 100, PreviewChars = 20, SpillStore = store };
        var full = new string('x', 5_000);

        var (value, record) = await ToolOutputHygiene.CapAsync(
            policy, "read", full, CancellationToken.None);

        record.ShouldNotBeNull();
        record.Recoverable.ShouldBeTrue();

        // The model is told where the rest is, and the rest is actually there.
        value.ShouldContain(record.Spilled!.Locator);
        value.ShouldContain(record.Spilled.RetrievalHint);
        (await File.ReadAllTextAsync(record.Spilled.Locator)).ShouldBe(full);
    }

    // ── The spill store's security shape ──

    [Test]
    public async Task FileSpillStore_ATraversingToolName_IsAHintNeverAPath()
    {
        using var root = new TempRoot();
        var store = new FileToolOutputSpillStore(root.Path);

        var spilled = await store.SaveAsync("../../etc/passwd", "payload");

        Path.GetDirectoryName(Path.GetFullPath(spilled.Locator))
            .ShouldBe(Path.GetFullPath(root.Path));
        Path.GetFileName(spilled.Locator).ShouldStartWith("etcpasswd-");
    }

    [Test]
    public async Task FileSpillStore_RepeatedSaves_NeverCollide()
    {
        using var root = new TempRoot();
        var store = new FileToolOutputSpillStore(root.Path);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(i => store.SaveAsync("read", $"payload-{i}")));

        results.Select(r => r.Locator).Distinct().Count().ShouldBe(20);
        for (var i = 0; i < 20; i++)
            (await File.ReadAllTextAsync(results[i].Locator)).ShouldBe($"payload-{i}");
    }

    [Test]
    public async Task FileSpillStore_OnUnix_WritesOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file modes do not apply on Windows.");
            return;
        }

        using var root = new TempRoot();
        var store = new FileToolOutputSpillStore(Path.Combine(root.Path, "spill"));

        var spilled = await store.SaveAsync("read", "payload");

        File.GetUnixFileMode(spilled.Locator)
            .ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.GetUnixFileMode(store.Root)
            .ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [Test]
    public async Task FileSpillStore_ReportsTheExactByteCount()
    {
        using var root = new TempRoot();
        var store = new FileToolOutputSpillStore(root.Path);

        // Two characters, four UTF-8 bytes — a char count would report the wrong number here.
        var spilled = await store.SaveAsync("read", "éé");

        spilled.Bytes.ShouldBe(4);
    }

    // ── Pruning what is already in the transcript ──

    [Test]
    public void Prune_ResultsAlreadySmall_AreLeftAlone()
    {
        var messages = Transcript(("a", 50), ("b", 50), ("c", 50));

        ToolOutputHygiene.Prune(messages, new ToolOutputPolicy { PrunedResultChars = 100, KeepRecentResults = 0 })
            .ShouldBeNull();
    }

    [Test]
    public void Prune_TheMostRecentResults_AreExempt()
    {
        var messages = Transcript(("a", 5_000), ("b", 5_000), ("c", 5_000));
        var policy = new ToolOutputPolicy { PrunedResultChars = 100, KeepRecentResults = 2 };

        var reduced = ToolOutputHygiene.Prune(messages, policy);

        reduced.ShouldNotBeNull();
        reduced.Value.ReplacedCount.ShouldBe(1);

        var tools = reduced.Value.Messages.Where(m => m.Role == AgentRole.Tool).ToList();
        tools[0].Content!.Length.ShouldBeLessThan(5_000);   // the oldest, reduced
        tools[1].Content!.Length.ShouldBe(5_000);           // still being worked from
        tools[2].Content!.Length.ShouldBe(5_000);
    }

    [Test]
    public void Prune_DoesNotTouchTheListItWasGiven()
    {
        var messages = Transcript(("a", 5_000), ("b", 5_000));
        var policy = new ToolOutputPolicy { PrunedResultChars = 100, KeepRecentResults = 0 };

        ToolOutputHygiene.Prune(messages, policy).ShouldNotBeNull();

        // The accumulated history a workflow persists must survive intact; only what is *sent*
        // is reduced.
        messages.Where(m => m.Role == AgentRole.Tool)
            .ShouldAllBe(m => m.Content!.Length == 5_000);
    }

    [Test]
    public async Task Prune_KeepsASpillFooter_SoTheOutputStaysRecoverable()
    {
        using var root = new TempRoot();
        var store = new FileToolOutputSpillStore(root.Path);
        var capPolicy = new ToolOutputPolicy { MaxResultChars = 100, PreviewChars = 90, SpillStore = store };

        var (capped, capRecord) = await ToolOutputHygiene.CapAsync(
            capPolicy, "read", new string('x', 5_000), CancellationToken.None);

        var messages = new List<AgentMessage>
        {
            AgentMessage.User("go"),
            AgentMessage.ToolResult("c1", capped),
            AgentMessage.User("continue")
        };

        var reduced = ToolOutputHygiene.Prune(
            messages, new ToolOutputPolicy { PrunedResultChars = 10, KeepRecentResults = 0 });

        reduced.ShouldNotBeNull();
        var pruned = reduced.Value.Messages[1].Content!;
        pruned.Length.ShouldBeLessThan(capped.Length);

        // Pruning away the locator would turn a bounded result into a lost one.
        pruned.ShouldContain(capRecord!.Spilled!.Locator);
        pruned.ShouldContain("pruned");
    }

    // ── End to end through the agent engine ──

    [Test]
    public async Task Agent_WithNoPolicy_StillCarriesAHugeToolResultWhole()
    {
        // The behaviour a policy opts *out* of. Asserted so the opt-in stays genuinely opt-in.
        var model = new RecordingModel();
        var job = ToolJob("uncapped", model, policy: null);

        await job.ExecuteAsync(new HygieneState { Input = "go" });

        LastToolContent(model).Length.ShouldBe(HugePayload.Length);
    }

    [Test]
    public async Task Agent_WithAPolicy_DoesNotCarryAHugeToolResultIntoTheNextRequest()
    {
        using var root = new TempRoot();
        var model = new RecordingModel();
        var job = ToolJob("capped", model, new ToolOutputPolicy
        {
            MaxResultChars = 500,
            PreviewChars = 100,
            SpillStore = new FileToolOutputSpillStore(root.Path)
        });

        await job.ExecuteAsync(new HygieneState { Input = "go" });

        var carried = LastToolContent(model);
        carried.Length.ShouldBeLessThan(500);
        carried.ShouldStartWith(new string('z', 100));

        // Bounded, not lost.
        var locator = Directory.GetFiles(root.Path).ShouldHaveSingleItem();
        (await File.ReadAllTextAsync(locator)).ShouldBe(HugePayload);
        carried.ShouldContain(locator);
    }

    [Test]
    public async Task Agent_CappingAToolResult_IsReportedToTheObserver()
    {
        var observer = new CollectingObserver();
        using var scope = ContextObserving.BeginScope(observer);

        var job = ToolJob("observed", new RecordingModel(), new ToolOutputPolicy
        {
            MaxResultChars = 500,
            PreviewChars = 100
        });

        await job.ExecuteAsync(new HygieneState { Input = "go" });

        var capped = observer.Capped.ShouldHaveSingleItem();
        capped.ToolName.ShouldBe("dump");
        capped.CharsBefore.ShouldBe(HugePayload.Length);
        capped.CharsAfter.ShouldBeLessThan(500);
        capped.Recoverable.ShouldBeFalse();
    }

    /// <summary>
    /// D3's actual payoff: shedding stale tool output can bring a request back under budget
    /// <em>without</em> a summarization model call — no cost, no latency, no lossy rewrite.
    /// </summary>
    [Test]
    public async Task Agent_PruningAlone_AvertsTheSummarizationCall()
    {
        var observer = new CollectingObserver();
        using var scope = ContextObserving.BeginScope(observer);

        var job = PruningJob("averted", withPolicy: true);

        await Should.NotThrowAsync(() => job.ExecuteAsync(new HygieneState { Input = "go" }));

        var record = observer.Compacted.Last(r => r.Pruning is not null);
        record.Pruning!.ReplacedCount.ShouldBe(1);
        record.Pruning.CharsReclaimed.ShouldBeGreaterThan(0);
        record.Pruning.AvertedCompaction.ShouldBeTrue();
    }

    [Test]
    public async Task Agent_WithoutPruning_TheSameRunPaysForTheSummary()
    {
        // The control for the test above: identical setup minus the policy. Without it the
        // summarizer is reached, which is what "averted" is measured against.
        var job = PruningJob("not-averted", withPolicy: false);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => job.ExecuteAsync(new HygieneState { Input = "go" }));

        ex.Message.ShouldBe("Summarisation should not have been triggered.");
    }

    // ── Configuration ──

    [Test]
    public void WithToolOutputPolicy_APreviewLargerThanTheCap_IsRejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            AgentJobFactory.Create<HygieneState>("bad", new RecordingModel())
                .WithPrompt(s => s.Input)
                .WithToolOutputPolicy(new ToolOutputPolicy { MaxResultChars = 100, PreviewChars = 200 })
                .MapResult((s, text) => s with { Output = text })
                .Build());
    }

    // ── Fixtures ──

    private const string ToolName = "dump";
    private static readonly string HugePayload = new('z', 200_000);

    private static List<AgentMessage> Transcript(params (string CallId, int Length)[] results)
    {
        var messages = new List<AgentMessage> { AgentMessage.User("go") };
        foreach (var (callId, length) in results)
        {
            messages.Add(AgentMessage.Assistant(string.Empty, [new AgentToolCall(callId, ToolName, "{}")]));
            messages.Add(AgentMessage.ToolResult(callId, new string('x', length)));
        }

        return messages;
    }

    private static string LastToolContent(RecordingModel model) =>
        model.Requests[^1].Messages.Last(m => m.Role == AgentRole.Tool).Content!;

    private static TextAgentJob<HygieneState> ToolJob(
        string name, IAgentModel model, ToolOutputPolicy? policy)
    {
        var builder = AgentJobFactory.Create<HygieneState>(name, model)
            .WithPrompt(s => s.Input)
            .WithTools(new ToolKit("k").AddTool(
                ToolName, "Returns a very large payload", () => ToolResult.Ok(HugePayload)));

        if (policy is not null)
            builder = builder.WithToolOutputPolicy(policy);

        return builder.MapResult((s, text) => s with { Output = text }).Build();
    }

    /// <summary>
    /// A run whose single tool result is far over the allocation, with a summarizer that throws if
    /// it is ever reached. <c>KeepRecentResults = 0</c> so one round is enough to show the
    /// mechanism; the exemption itself is covered by its own unit test.
    /// </summary>
    private static TextAgentJob<HygieneState> PruningJob(string name, bool withPolicy)
    {
        var builder = AgentJobFactory.Create<HygieneState>(name, new RecordingModel())
            .WithPrompt(s => s.Input)
            .WithTools(new ToolKit("k").AddTool(
                ToolName, "Returns a large payload", () => ToolResult.Ok(new string('x', 40_000))))
            .WithContextLimit(1_000)
            .WithContextStrategy(new SummarizingContextStrategy(
                new ThrowingSummarizer(), thresholdTokens: 1, recentMessageCount: 1));

        if (withPolicy)
        {
            builder = builder.WithToolOutputPolicy(new ToolOutputPolicy
            {
                MaxResultChars = 1_000_000,   // capping deliberately out of the way
                PrunedResultChars = 100,
                KeepRecentResults = 0
            });
        }

        return builder.MapResult((s, text) => s with { Output = text }).Build();
    }

    private sealed record HygieneState
    {
        public string Input { get; init; } = string.Empty;
        public string Output { get; init; } = string.Empty;
    }

    /// <summary>Calls the tool once, then answers — recording every request it was sent.</summary>
    private sealed class RecordingModel : IAgentModel
    {
        private int _index;

        public List<AgentRequest> Requests { get; } = [];

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(_index++ == 0
                ? new AgentResponse { Text = "r1", ToolCalls = [new AgentToolCall("c1", ToolName, "{}")] }
                : new AgentResponse { Text = "done" });
        }
    }

    private sealed class ThrowingSummarizer : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("Summarisation should not have been triggered.");
    }

    private sealed class CollectingObserver : IContextObserver
    {
        public List<ToolOutputCapRecord> Capped { get; } = [];
        public List<ContextCompactionRecord> Compacted { get; } = [];

        public ValueTask OnContextAssembledAsync(
            ContextObservation observation, CancellationToken ct = default) => ValueTask.CompletedTask;

        public ValueTask OnContextCompactedAsync(
            ContextCompactionRecord record, CancellationToken ct = default)
        {
            Compacted.Add(record);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnToolOutputCappedAsync(
            ToolOutputCapRecord record, CancellationToken ct = default)
        {
            Capped.Add(record);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a test over.
            }
        }
    }
}
