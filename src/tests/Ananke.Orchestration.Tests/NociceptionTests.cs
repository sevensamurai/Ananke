using Ananke.Abstractions.Tools;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Tools.Gating;
using Ananke.Orchestration.Tools.Faults;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class InMemoryToolFaultObserverTests
{
    private InMemoryToolMemory _memory = null!;
    private InMemoryToolFaultObserver _observer = null!;

    [SetUp]
    public void SetUp()
    {
        _memory = new InMemoryToolMemory();
        _observer = new InMemoryToolFaultObserver(_memory);
    }

    private async Task SeedAsync(string kit, string tool, ToolHealth health = ToolHealth.Healthy)
    {
        await _memory.UpsertAsync(new ToolMemoryEntry
        {
            KitName = kit,
            ToolName = tool,
            Description = $"Tool {tool}",
            Health = health
        });
    }

    // ── ContractBreak → Offline ────────────────────────────────────

    [Test]
    public async Task FireAsync_ContractBreak_MarksOffline()
    {
        await SeedAsync("kit", "buy_stock");

        await _observer.ReportAsync(new ToolFaultEvent(
            "kit", "buy_stock", "schema mismatch", ContractBreak: true, Transient: false));

        var recalled = await _memory.RecallAsync("buy_stock", topK: 5);
        recalled.ShouldBeEmpty(); // Offline tools are excluded from recall
    }

    // ── Transient → Cooldown ───────────────────────────────────────

    [Test]
    public async Task FireAsync_Transient_MarksCooldown()
    {
        await SeedAsync("kit", "fetch_price");

        await _observer.ReportAsync(new ToolFaultEvent(
            "kit", "fetch_price", "network timeout", ContractBreak: false, Transient: true));

        var recalled = await _memory.RecallAsync("fetch_price price", topK: 5);
        recalled.ShouldNotBeEmpty();
        recalled[0].Health.ShouldBe(ToolHealth.Cooldown);
    }

    // ── Neither → Degraded ─────────────────────────────────────────

    [Test]
    public async Task FireAsync_NeitherFlag_MarkesDegraded()
    {
        await SeedAsync("kit", "summarize");

        await _observer.ReportAsync(new ToolFaultEvent(
            "kit", "summarize", "unexpected error", ContractBreak: false, Transient: false));

        var recalled = await _memory.RecallAsync("summarize", topK: 5);
        recalled.ShouldNotBeEmpty();
        recalled[0].Health.ShouldBe(ToolHealth.Degraded);
    }

    // ── Unknown tool — no-op ───────────────────────────────────────

    [Test]
    public async Task FireAsync_UnknownTool_DoesNotThrow()
    {
        // Should be a no-op (MarkHealthAsync on InMemoryToolMemory is a no-op for unknown keys)
        await Should.NotThrowAsync(() =>
            _observer.ReportAsync(new ToolFaultEvent(
                "kit", "ghost_tool", "missing", ContractBreak: true, Transient: false)).AsTask());
    }

    // ── ContractBreak takes precedence over Transient ──────────────

    [Test]
    public async Task FireAsync_ContractBreakAndTransient_OfflineTakesPrecedence()
    {
        await SeedAsync("kit", "dual_flag");

        await _observer.ReportAsync(new ToolFaultEvent(
            "kit", "dual_flag", "both set", ContractBreak: true, Transient: true));

        // Offline → excluded from recall
        var recalled = await _memory.RecallAsync("dual flag", topK: 5);
        recalled.Any(e => e.ToolName == "dual_flag").ShouldBeFalse();
    }
}

[TestFixture]
public class ToolHealthRecoveryTests
{
    private InMemoryToolMemory _memory = null!;
    private ToolHealthRecovery _decay = null!;
    private FakeTimeProvider _clock = null!;

    [SetUp]
    public void SetUp()
    {
        _memory = new InMemoryToolMemory();
        _clock = new FakeTimeProvider();
        _decay = new ToolHealthRecovery(_memory, timeProvider: _clock)
        {
            CooldownDuration = TimeSpan.FromMilliseconds(50),
            DegradedDuration = TimeSpan.FromMilliseconds(50)
        };
    }

    [TearDown]
    public async Task TearDown() => await _decay.DisposeAsync();

    private async Task SeedAsync(string kit, string tool, ToolHealth health)
    {
        await _memory.UpsertAsync(new ToolMemoryEntry
        {
            KitName = kit,
            ToolName = tool,
            Description = $"Tool {tool}",
            Health = health
        });
    }

    // ── Cooldown → Healthy after duration ─────────────────────────

    [Test]
    public async Task TickAsync_CooldownExpired_RestoresHealthy()
    {
        await SeedAsync("kit", "tool_a", ToolHealth.Cooldown);
        _decay.TrackRecovery("kit", "tool_a", ToolHealth.Cooldown);

        _clock.Advance(TimeSpan.FromMilliseconds(100)); // exceed 50ms CooldownDuration
        await _decay.TickAsync();

        var recalled = await _memory.RecallAsync("tool a", topK: 5);
        recalled.ShouldNotBeEmpty();
        recalled[0].Health.ShouldBe(ToolHealth.Healthy);
    }

    // ── Degraded → Healthy after duration ─────────────────────────

    [Test]
    public async Task TickAsync_DegradedExpired_RestoresHealthy()
    {
        await SeedAsync("kit", "tool_b", ToolHealth.Degraded);
        _decay.TrackRecovery("kit", "tool_b", ToolHealth.Degraded);

        _clock.Advance(TimeSpan.FromMilliseconds(100));
        await _decay.TickAsync();

        var recalled = await _memory.RecallAsync("tool b", topK: 5);
        recalled.ShouldNotBeEmpty();
        recalled[0].Health.ShouldBe(ToolHealth.Healthy);
    }

    // ── Not expired yet → stays in current state ──────────────────

    [Test]
    public async Task TickAsync_NotExpired_HealthUnchanged()
    {
        _decay = new ToolHealthRecovery(_memory, timeProvider: _clock)
        {
            CooldownDuration = TimeSpan.FromHours(1),
            DegradedDuration = TimeSpan.FromHours(1)
        };

        await SeedAsync("kit", "tool_c", ToolHealth.Cooldown);
        _decay.TrackRecovery("kit", "tool_c", ToolHealth.Cooldown);

        // Do NOT advance — duration not elapsed
        await _decay.TickAsync();

        var recalled = await _memory.RecallAsync("tool c", topK: 5);
        recalled.ShouldNotBeEmpty();
        recalled[0].Health.ShouldBe(ToolHealth.Cooldown);
    }

    // ── Offline is never decayed ────────────────────────────────────

    [Test]
    public async Task RecordPain_Offline_NotTracked()
    {
        await SeedAsync("kit", "tool_d", ToolHealth.Offline);
        _decay.TrackRecovery("kit", "tool_d", ToolHealth.Offline); // should be ignored

        _clock.Advance(TimeSpan.FromMilliseconds(100));
        await _decay.TickAsync();

        // Offline tool stays excluded from recall
        var recalled = await _memory.RecallAsync("tool d", topK: 5);
        recalled.Any(e => e.ToolName == "tool_d").ShouldBeFalse();
    }

    // ── Healthy is never tracked ────────────────────────────────────

    [Test]
    public async Task RecordPain_Healthy_NotTracked()
    {
        await SeedAsync("kit", "tool_e", ToolHealth.Healthy);
        _decay.TrackRecovery("kit", "tool_e", ToolHealth.Healthy);

        await _decay.TickAsync();

        var recalled = await _memory.RecallAsync("tool e", topK: 5);
        recalled.ShouldNotBeEmpty();
        recalled[0].Health.ShouldBe(ToolHealth.Healthy);
    }
}

[TestFixture]
public class ToolKitFaultObserverTests
{
    // ── Fatal error fires ContractBreak pain signal ────────────────

    [Test]
    public async Task AddTool_WithFaultObserver_FatalResult_MarksOffline()
    {
        var memory = new InMemoryToolMemory();
        var observer = new InMemoryToolFaultObserver(memory);

        var kit = new ToolKit("agent")
            .WithFaultObserver(observer)
            .WithMemory(memory);

        kit.AddTool("bad_tool", "A tool with a fatal bug", () => ToolResult.Fatal("schema broken"));
        await kit.PopulateMemoryAsync();

        // Execute the tool — should fire a ContractBreak pain signal
        await kit.Tools["bad_tool"].ExecuteAsync(new Dictionary<string, object?>());

        // Offline → excluded from recall
        var recalled = await memory.RecallAsync("bad tool", topK: 5);
        recalled.Any(e => e.ToolName == "bad_tool").ShouldBeFalse();
    }

    // ── Retryable error fires Transient pain signal ────────────────

    [Test]
    public async Task AddTool_WithFaultObserver_RetryableError_MarksCooldown()
    {
        var memory = new InMemoryToolMemory();
        var observer = new InMemoryToolFaultObserver(memory);

        var kit = new ToolKit("agent")
            .WithFaultObserver(observer)
            .WithMemory(memory);

        kit.AddTool("flaky_tool", "A tool that sometimes fails transiently",
            () => ToolResult.Error("timeout")); // IsRetryable defaults to true
        await kit.PopulateMemoryAsync();

        await kit.Tools["flaky_tool"].ExecuteAsync(new Dictionary<string, object?>());

        var recalled = await memory.RecallAsync("flaky tool", topK: 5);
        recalled.ShouldNotBeEmpty();
        recalled[0].Health.ShouldBe(ToolHealth.Cooldown);
    }

    // ── Successful result fires no pain signal ─────────────────────

    [Test]
    public async Task AddTool_WithFaultObserver_SuccessResult_HealthUnchanged()
    {
        var memory = new InMemoryToolMemory();
        var observer = new InMemoryToolFaultObserver(memory);

        var kit = new ToolKit("agent")
            .WithFaultObserver(observer)
            .WithMemory(memory);

        kit.AddTool("good_tool", "A healthy tool", () => ToolResult.Ok("ok"));
        await kit.PopulateMemoryAsync();

        await kit.Tools["good_tool"].ExecuteAsync(new Dictionary<string, object?>());

        var recalled = await memory.RecallAsync("good tool", topK: 5);
        recalled.ShouldNotBeEmpty();
        recalled[0].Health.ShouldBe(ToolHealth.Healthy);
    }

    // ── Retroactive wrapping: observer registered after AddTool ──

    [Test]
    public async Task WithFaultObserver_RegisteredAfterAddTool_WrapsExistingTools()
    {
        var memory = new InMemoryToolMemory();
        var observer = new InMemoryToolFaultObserver(memory);

        var kit = new ToolKit("agent").WithMemory(memory);
        kit.AddTool("pre_existing", "Tool added before observer", () => ToolResult.Fatal("crash"));
        await kit.PopulateMemoryAsync();

        // Register observer AFTER the tool was added
        kit.WithFaultObserver(observer);

        await kit.Tools["pre_existing"].ExecuteAsync(new Dictionary<string, object?>());

        // Should still be marked offline via retroactive wrapping
        var recalled = await memory.RecallAsync("pre existing", topK: 5);
        recalled.Any(e => e.ToolName == "pre_existing").ShouldBeFalse();
    }
}
