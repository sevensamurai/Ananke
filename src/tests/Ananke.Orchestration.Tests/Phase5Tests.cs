using Ananke.Abstractions.Tools;
using Ananke.MCP;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Tools.Faults;
using Ananke.Orchestration.Tools.Gating;
using Ananke.Skills;
using Shouldly;

namespace Ananke.Orchestration.Tests;

// ── SkillCatalogMemorySync ─────────────────────────────────────────────────

[TestFixture]
public class SkillCatalogMemorySyncTests
{
    // Minimal fake catalog: SyncAsync is a no-op; SearchAsync returns a fixed list
    private sealed class FakeCatalog(IReadOnlyList<SkillDescriptor> skills) : ISkillCatalog
    {
        public Task<IReadOnlyList<SkillDescriptor>> SearchAsync(
            string query, IReadOnlyList<string>? tags, int limit, CancellationToken ct) =>
            Task.FromResult(skills);

        public Task<ToolDefinition> ResolveAsync(SkillDescriptor skill, CancellationToken ct) =>
            Task.FromResult(new ToolDefinition
            {
                Name = skill.Name,
                Description = skill.Description,
                Parameters = [],
                Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
            });

        public Task SyncAsync(CancellationToken ct) => Task.CompletedTask;
    }

    [Test]
    public async Task SyncAsync_ProjectsEachSkillIntoToolMemory()
    {
        var skills = new List<SkillDescriptor>
        {
            new() { Id = "s1", Name = "search_tool", Description = "Searches the web", Tags = ["search", "web"] },
            new() { Id = "s2", Name = "calc_tool", Description = "Calculates math expressions", Tags = ["math"] }
        };

        var memory = new InMemoryToolMemory();
        var sync = new SkillCatalogMemorySync(new FakeCatalog(skills), memory, kitName: "skill_kit");

        await sync.SyncAsync();

        var searchRecall = await memory.RecallAsync("search");
        searchRecall.Any(e => e.ToolName == "search_tool").ShouldBeTrue();

        var mathRecall = await memory.RecallAsync("math");
        mathRecall.Any(e => e.ToolName == "calc_tool").ShouldBeTrue();
    }

    [Test]
    public async Task SyncAsync_KitName_MatchesConfiguration()
    {
        var skills = new List<SkillDescriptor>
        {
            new() { Id = "s1", Name = "tool_a", Description = "Does something" }
        };

        var memory = new InMemoryToolMemory();
        var sync = new SkillCatalogMemorySync(new FakeCatalog(skills), memory, kitName: "my_kit");

        await sync.SyncAsync();

        var recalled = await memory.RecallAsync("something");
        recalled.Single(e => e.ToolName == "tool_a").KitName.ShouldBe("my_kit");
    }

    [Test]
    public async Task SyncAsync_TagsArePreserved()
    {
        var skills = new List<SkillDescriptor>
        {
            new() { Id = "s1", Name = "tagged_tool", Description = "Tagged", Tags = ["alpha", "beta"] }
        };

        var memory = new InMemoryToolMemory();
        var sync = new SkillCatalogMemorySync(new FakeCatalog(skills), memory, kitName: "k");

        await sync.SyncAsync();

        var recalled = await memory.RecallAsync("tagged");
        var entry = recalled.Single(e => e.ToolName == "tagged_tool");
        entry.Tags.ShouldContain("alpha");
        entry.Tags.ShouldContain("beta");
    }

    [Test]
    public async Task SearchAsync_ForwardsToInnerCatalog()
    {
        var skills = new List<SkillDescriptor>
        {
            new() { Id = "s1", Name = "found_tool", Description = "Found" }
        };
        var sync = new SkillCatalogMemorySync(
            new FakeCatalog(skills), new InMemoryToolMemory(), kitName: "k");

        var results = await sync.SearchAsync("found");

        results.Count.ShouldBe(1);
        results[0].Name.ShouldBe("found_tool");
    }

    [Test]
    public async Task ResolveAsync_ForwardsToInnerCatalog()
    {
        var skill = new SkillDescriptor { Id = "s1", Name = "tool_x", Description = "X" };
        var sync = new SkillCatalogMemorySync(
            new FakeCatalog([skill]), new InMemoryToolMemory(), kitName: "k");

        var def = await sync.ResolveAsync(skill);

        def.Name.ShouldBe("tool_x");
    }
}

// ── McpToolInvoker ─────────────────────────────────────────────────────────

[TestFixture]
public class McpToolInvokerTests
{
    private static readonly IReadOnlyDictionary<string, object?> NoArgs =
        new Dictionary<string, object?>();

    // Builds a fake server invoke delegate: always succeeds on server 0,
    // always throws on the other servers (unless overridden per-index)
    private static Func<int, string, IReadOnlyDictionary<string, object?>, CancellationToken, Task<ToolResult>>
        MakeInvoke(params Func<ToolResult>[] perServer) =>
        (idx, _, _, _) => Task.FromResult(perServer[idx]());

    private static Func<int, string, IReadOnlyDictionary<string, object?>, CancellationToken, Task<ToolResult>>
        AlwaysFail(int serverCount) =>
        (_, _, _, _) => throw new InvalidOperationException("server down");

    // ── Round-robin distributes calls ──────────────────────────────

    [Test]
    public async Task InvokeAsync_RoundRobin_DistributesAcrossServers()
    {
        var hits = new int[3];
        Func<int, string, IReadOnlyDictionary<string, object?>, CancellationToken, Task<ToolResult>> invoke =
            (idx, _, _, _) => { hits[idx]++; return Task.FromResult(ToolResult.Ok("ok")); };

        var invoker = new McpToolInvoker(3, invoke);

        await invoker.InvokeAsync("kit", "tool", NoArgs, default);
        await invoker.InvokeAsync("kit", "tool", NoArgs, default);
        await invoker.InvokeAsync("kit", "tool", NoArgs, default);

        // Each server should have been hit exactly once
        hits.ShouldAllBe(h => h == 1);
    }

    // ── Faulted server is skipped ──────────────────────────────────

    [Test]
    public async Task InvokeAsync_FaultedServer_IsSkippedAfterThreshold()
    {
        // Server 0 always throws; server 1 always succeeds.
        // faultThreshold=1 means a single failure exhausts server 0's budget.
        Func<int, string, IReadOnlyDictionary<string, object?>, CancellationToken, Task<ToolResult>> invoke =
            (idx, _, _, _) =>
            {
                if (idx == 0) throw new InvalidOperationException("server 0 down");
                return Task.FromResult(ToolResult.Ok("ok"));
            };

        var invoker = new McpToolInvoker(2, invoke, faultThreshold: 1);

        // First call hits server 0 (round-robin starts at 0), fails → fault[0] = 1 = threshold
        await invoker.InvokeAsync("kit", "tool", NoArgs, default);

        // Next call: server 0 is at threshold → skipped; server 1 is used
        var result = await invoker.InvokeAsync("kit", "tool", NoArgs, default);

        result.IsError.ShouldBeFalse();
        result.Value.ShouldBe("ok");
    }

    // ── Success resets fault count ─────────────────────────────────

    [Test]
    public async Task InvokeAsync_SuccessAfterFault_ResetsFaultCount()
    {
        var callCount = 0;
        Func<int, string, IReadOnlyDictionary<string, object?>, CancellationToken, Task<ToolResult>> invoke =
            (idx, _, _, _) =>
            {
                callCount++;
                if (callCount == 1) throw new InvalidOperationException("transient");
                return Task.FromResult(ToolResult.Ok("ok"));
            };

        var invoker = new McpToolInvoker(1, invoke, faultThreshold: 3);

        await invoker.InvokeAsync("kit", "tool", NoArgs, default); // fault count = 1
        await invoker.InvokeAsync("kit", "tool", NoArgs, default); // success → reset to 0

        invoker.FaultCounts[0].ShouldBe(0);
    }

    // ── Fault observer is notified on failure ──────────────────────

    [Test]
    public async Task InvokeAsync_OnFailure_ReportsFaultEvent()
    {
        var memory = new InMemoryToolMemory();
        await memory.UpsertAsync(new ToolMemoryEntry
        { KitName = "kit", ToolName = "tool", Description = "t" });

        var observer = new InMemoryToolFaultObserver(memory);

        Func<int, string, IReadOnlyDictionary<string, object?>, CancellationToken, Task<ToolResult>> invoke =
            (_, _, _, _) => throw new InvalidOperationException("boom");

        var invoker = new McpToolInvoker(1, invoke, faultObserver: observer);

        await invoker.InvokeAsync("kit", "tool", NoArgs, default);

        var recalled = await memory.RecallAsync("tool", topK: 5);
        // Tool should now be in Cooldown (Transient=true was reported)
        // Note: RecallAsync excludes Offline by default; Cooldown is still returned
        recalled.Any(e => e.ToolName == "tool" && e.Health == ToolHealth.Cooldown)
            .ShouldBeTrue();
    }

    // ── All faulted: graceful degradation ─────────────────────────

    [Test]
    public async Task InvokeAsync_AllServersFaulted_StillReturnsResult()
    {
        var callCount = 0;
        Func<int, string, IReadOnlyDictionary<string, object?>, CancellationToken, Task<ToolResult>> invoke =
            (_, _, _, _) =>
            {
                callCount++;
                if (callCount <= 3) throw new InvalidOperationException("all down");
                return Task.FromResult(ToolResult.Ok("recovered"));
            };

        var invoker = new McpToolInvoker(1, invoke, faultThreshold: 2);

        // Fill fault budget
        await invoker.InvokeAsync("kit", "tool", NoArgs, default);
        await invoker.InvokeAsync("kit", "tool", NoArgs, default);

        // All servers faulted — should still attempt and return an error result (not throw)
        await Should.NotThrowAsync(() => invoker.InvokeAsync("kit", "tool", NoArgs, default));
    }
}
