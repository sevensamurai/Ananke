using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tools.Routing;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Tools.Gating;
using Ananke.Orchestration.Tools.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests.Agents.Middleware;

[TestFixture]
public class SmartToolRouterMiddlewareTests
{
    // ── No tools on request — no-op ────────────────────────────────────

    [Test]
    public async Task OnBeforeGenerateAsync_NoTools_RequestUnchanged()
    {
        var kit = new ToolKit("agent");
        kit.AddTool("ping", "Ping", () => ToolResult.Ok("pong"));
        var mw = new SmartToolRouterMiddleware(kit);

        var request = new AgentRequest { Messages = [AgentMessage.User("hi")] };
        var result = await mw.OnBeforeGenerateAsync(request);

        ReferenceEquals(result, request).ShouldBeTrue();
    }

    // ── No router → PassThrough → all tools returned ───────────────────

    [Test]
    public async Task NoRouter_PassesAllToolsThrough()
    {
        var kit = new ToolKit("agent");
        kit.AddTool("tool_a", "Tool A", () => ToolResult.Ok("a"));
        kit.AddTool("tool_b", "Tool B", () => ToolResult.Ok("b"));

        var mw = new SmartToolRouterMiddleware(kit);
        var request = MakeRequestWithTools("do something", "tool_a", "tool_b");

        var result = await mw.OnBeforeGenerateAsync(request);

        result.Tools!.Count.ShouldBe(2);
        result.Tools.Select(t => t.Name).ShouldBe(["tool_a", "tool_b"], ignoreOrder: true);
    }

    // ── Router replaces request tools ──────────────────────────────────

    [Test]
    public async Task Router_ReplacesRequestTools()
    {
        var kit = new ToolKit("agent");
        kit.AddTool("keep", "Tool to keep", () => ToolResult.Ok("k"));
        kit.AddTool("drop", "Tool to drop", () => ToolResult.Ok("d"));

        // Router that only keeps "keep"
        var router = new FuncRouter(req => new ToolRoutingDecision
        {
            UseTools = true,
            Confidence = RoutingConfidence.High,
            SelectedTools = req.Candidates.Where(e => e.ToolName == "keep").ToList(),
        });

        var mw = new SmartToolRouterMiddleware(kit, router);
        var request = MakeRequestWithTools("query", "keep", "drop");

        var result = await mw.OnBeforeGenerateAsync(request);

        result.Tools!.ShouldHaveSingleItem();
        result.Tools[0].Name.ShouldBe("keep");
    }

    // ── UseTools=false clears all tools ───────────────────────────────

    [Test]
    public async Task UseToolsFalse_ClearsRequestTools()
    {
        var kit = new ToolKit("agent");
        kit.AddTool("tool_a", "Tool A", () => ToolResult.Ok("a"));

        var router = new FuncRouter(_ => new ToolRoutingDecision
        {
            UseTools = false,
            Confidence = RoutingConfidence.High,
        });

        var mw = new SmartToolRouterMiddleware(kit, router);
        var request = MakeRequestWithTools("query", "tool_a");

        var result = await mw.OnBeforeGenerateAsync(request);

        result.Tools.ShouldBeEmpty();
    }

    // ── Kit.WithRouter is respected when no explicit router given ──────

    [Test]
    public async Task KitRouter_UsedWhenNoExplicitRouterProvided()
    {
        var kit = new ToolKit("agent");
        kit.AddTool("a", "A", () => ToolResult.Ok("a"));
        kit.AddTool("b", "B", () => ToolResult.Ok("b"));

        var router = new FuncRouter(req => new ToolRoutingDecision
        {
            UseTools = true,
            Confidence = RoutingConfidence.High,
            SelectedTools = req.Candidates.Take(1).ToList(),
        });
        kit.WithRouter(router);

        var mw = new SmartToolRouterMiddleware(kit);  // no explicit router
        var request = MakeRequestWithTools("q", "a", "b");

        var result = await mw.OnBeforeGenerateAsync(request);

        result.Tools!.Count.ShouldBe(1);
    }

    // ── Explicit router overrides kit router ───────────────────────────

    [Test]
    public async Task ExplicitRouter_OverridesKitRouter()
    {
        var kit = new ToolKit("agent");
        kit.AddTool("a", "A", () => ToolResult.Ok("a"));
        kit.AddTool("b", "B", () => ToolResult.Ok("b"));

        // Kit router drops everything; explicit router keeps everything
        kit.WithRouter(new FuncRouter(_ => new ToolRoutingDecision
        {
            UseTools = true,
            Confidence = RoutingConfidence.High,
            SelectedTools = [],
        }));

        var explicitRouter = new FuncRouter(req => new ToolRoutingDecision
        {
            UseTools = true,
            Confidence = RoutingConfidence.High,
            SelectedTools = req.Candidates,
        });

        var mw = new SmartToolRouterMiddleware(kit, explicitRouter);
        var request = MakeRequestWithTools("q", "a", "b");

        var result = await mw.OnBeforeGenerateAsync(request);

        result.Tools!.Count.ShouldBe(2);
    }

    // ── Inflammation advisory injected ────────────────────────────────

    [Test]
    public async Task InflammationAdvisory_StillInjected_WhenMemoryProvided()
    {
        var memory = new InMemoryToolMemory();
        var kit = new ToolKit("agent").WithMemory(memory);
        kit.AddTool("broken", "A broken tool", () => ToolResult.Ok("x"));
        await kit.PopulateMemoryAsync();
        await memory.MarkHealthAsync("agent", "broken", ToolHealth.Degraded);

        var mw = new SmartToolRouterMiddleware(kit);
        var request = MakeRequestWithTools("use the tool", "broken");

        var result = await mw.OnBeforeGenerateAsync(request);

        result.SystemPrompt.ShouldNotBeNullOrEmpty();
        result.SystemPrompt!.ShouldContain("`broken`");
        result.SystemPrompt.ShouldContain("degraded");
    }

    // ── No user message → no-op ────────────────────────────────────────

    [Test]
    public async Task NoUserMessage_RequestUnchanged()
    {
        var kit = new ToolKit("agent");
        kit.AddTool("ping", "Ping", () => ToolResult.Ok("pong"));

        var mw = new SmartToolRouterMiddleware(kit);
        var request = new AgentRequest
        {
            Messages = [AgentMessage.System("system only")],
            Tools = [new AgentTool("ping", "Ping", "{}")]
        };

        var result = await mw.OnBeforeGenerateAsync(request);

        ReferenceEquals(result, request).ShouldBeTrue();
    }

    // ── OnAfterGenerateAsync is always a pass-through ─────────────────

    [Test]
    public async Task OnAfterGenerateAsync_AlwaysPassThrough()
    {
        var kit = new ToolKit("agent");
        var mw = new SmartToolRouterMiddleware(kit);
        var response = new AgentResponse { Text = "hello" };
        var result = await mw.OnAfterGenerateAsync(response, new AgentRequest { Messages = [] });
        ReferenceEquals(result, response).ShouldBeTrue();
    }

    // ── helpers ───────────────────────────────────────────────────────

    private static AgentRequest MakeRequestWithTools(string msg, params string[] toolNames) =>
        new()
        {
            Messages = [AgentMessage.User(msg)],
            Tools = toolNames.Select(n => new AgentTool(n, $"Desc {n}", "{}")).ToList(),
        };

    private sealed class FuncRouter(Func<ToolRoutingRequest, ToolRoutingDecision> fn) : ISmartToolRouter
    {
        public Task<ToolRoutingDecision> RouteAsync(ToolRoutingRequest request, CancellationToken ct = default)
            => Task.FromResult(fn(request));
    }
}
