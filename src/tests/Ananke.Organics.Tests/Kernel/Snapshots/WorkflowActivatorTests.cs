using Ananke.Orchestration.Workflows;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Tools;
using Ananke.Organics.Kernel.Snapshots;
using Shouldly;

namespace Ananke.Organics.Tests.Kernel.Snapshots;

[TestFixture]
public class WorkflowActivatorTests
{
    private ToolKit _tools = null!;
    private FakeModel _fakeModel = null!;

    [SetUp]
    public void SetUp()
    {
        _tools = new ToolKit("test-tools")
            .AddTool("search", "Searches", () => ToolResult.Ok("result"))
            .AddTool("lookup", "Looks up", (q) => ToolResult.Ok($"found: {q}"), "query", "Query");

        _fakeModel = new FakeModel("Hello from agent");
    }

    // ── Hydrate basics ──────────────────────────────────────────────

    [Test]
    public void Hydrate_CodeOnlyCell_ProducesRunnableWorkflow()
    {
        var cell = new WorkflowSnapshot
        {
            Name = "simple",
            Domain = "test",
            Tools = [],
            Connections = ["process -> End"],
            Jobs = new Dictionary<string, JobSnapshot>
            {
                ["process"] = new() { Type = "code" }
            },
            Models = new Dictionary<string, ModelSnapshot>()
        };

        var hydrator = new WorkflowActivator<TestState>();
        var workflow = hydrator.Hydrate(cell);

        workflow.ShouldNotBeNull();
    }

    [Test]
    public async Task Hydrate_CodeOnlyCell_RunsSuccessfully()
    {
        var cell = new WorkflowSnapshot
        {
            Name = "simple",
            Domain = "test",
            Tools = [],
            Connections = ["process -> End"],
            Jobs = new Dictionary<string, JobSnapshot>
            {
                ["process"] = new() { Type = "code" }
            },
            Models = new Dictionary<string, ModelSnapshot>()
        };

        var hydrator = new WorkflowActivator<TestState>()
            .WithCodeJobHandler((state, _) =>
                Task.FromResult(state with { Output = "processed" }));

        var workflow = hydrator.Hydrate(cell);
        var result = await workflow.RunAsync(new TestState());

        result.State.Output.ShouldBe("processed");
    }

    [Test]
    public void Hydrate_AgentJob_RequiresModelFactory()
    {
        var cell = BuildAgentCell();

        var hydrator = new WorkflowActivator<TestState>()
            .WithTools(_tools)
            .WithPromptBuilder((s, _) => s.Input)
            .WithResultMapper((s, _, text) => s with { Output = text });

        Should.Throw<InvalidOperationException>(() => hydrator.Hydrate(cell))
            .Message.ShouldContain("model factory");
    }

    [Test]
    public void Hydrate_AgentJob_RequiresPromptBuilder()
    {
        var cell = BuildAgentCell();

        var hydrator = new WorkflowActivator<TestState>()
            .WithTools(_tools)
            .WithModelFactory(_ => _fakeModel)
            .WithResultMapper((s, _, text) => s with { Output = text });

        Should.Throw<InvalidOperationException>(() => hydrator.Hydrate(cell))
            .Message.ShouldContain("prompt builder");
    }

    [Test]
    public void Hydrate_AgentJob_RequiresResultMapper()
    {
        var cell = BuildAgentCell();

        var hydrator = new WorkflowActivator<TestState>()
            .WithTools(_tools)
            .WithModelFactory(_ => _fakeModel)
            .WithPromptBuilder((s, _) => s.Input);

        Should.Throw<InvalidOperationException>(() => hydrator.Hydrate(cell))
            .Message.ShouldContain("result mapper");
    }

    [Test]
    public void Hydrate_MissingTool_ThrowsDescriptiveError()
    {
        var cell = new WorkflowSnapshot
        {
            Name = "bad-tools",
            Domain = "test",
            Tools = ["nonexistent_tool"],
            Connections = ["handle -> End"],
            Jobs = new Dictionary<string, JobSnapshot>
            {
                ["handle"] = new() { Type = "agent", ModelAlias = "default" }
            },
            Models = new Dictionary<string, ModelSnapshot>
            {
                ["default"] = new() { Provider = "openai", Model = Models.OpenAI.Gpt54Mini }
            }
        };

        var hydrator = new WorkflowActivator<TestState>()
            .WithTools(_tools)
            .WithModelFactory(_ => _fakeModel)
            .WithPromptBuilder((s, _) => s.Input)
            .WithResultMapper((s, _, text) => s with { Output = text });

        Should.Throw<InvalidOperationException>(() => hydrator.Hydrate(cell))
            .Message.ShouldContain("nonexistent_tool");
    }

    [Test]
    public void Hydrate_MissingModelAlias_ThrowsDescriptiveError()
    {
        var cell = new WorkflowSnapshot
        {
            Name = "bad-model",
            Domain = "test",
            Tools = [],
            Connections = ["handle -> End"],
            Jobs = new Dictionary<string, JobSnapshot>
            {
                ["handle"] = new() { Type = "agent", ModelAlias = "missing-alias" }
            },
            Models = new Dictionary<string, ModelSnapshot>
            {
                ["default"] = new() { Provider = "openai", Model = Models.OpenAI.Gpt54Mini }
            }
        };

        var hydrator = new WorkflowActivator<TestState>()
            .WithModelFactory(_ => _fakeModel)
            .WithPromptBuilder((s, _) => s.Input)
            .WithResultMapper((s, _, text) => s with { Output = text });

        Should.Throw<InvalidOperationException>(() => hydrator.Hydrate(cell))
            .Message.ShouldContain("missing-alias");
    }

    [Test]
    public void Hydrate_NullCell_Throws()
    {
        var hydrator = new WorkflowActivator<TestState>();
        Should.Throw<ArgumentNullException>(() => hydrator.Hydrate(null!));
    }

    [Test]
    public void Hydrate_MultipleToolKits_ResolvesAcrossAll()
    {
        var kit1 = new ToolKit("kit-a")
            .AddTool("tool_a", "Tool A", () => ToolResult.Ok("a"));
        var kit2 = new ToolKit("kit-b")
            .AddTool("tool_b", "Tool B", () => ToolResult.Ok("b"));

        var cell = new WorkflowSnapshot
        {
            Name = "multi-kit",
            Domain = "test",
            Tools = ["tool_a", "tool_b"],
            Connections = ["handle -> End"],
            Jobs = new Dictionary<string, JobSnapshot>
            {
                ["handle"] = new() { Type = "agent", ModelAlias = "default" }
            },
            Models = new Dictionary<string, ModelSnapshot>
            {
                ["default"] = new() { Provider = "openai", Model = Models.OpenAI.Gpt54Mini }
            }
        };

        var hydrator = new WorkflowActivator<TestState>()
            .WithTools(kit1)
            .WithTools(kit2)
            .WithModelFactory(_ => _fakeModel)
            .WithPromptBuilder((s, _) => s.Input)
            .WithResultMapper((s, _, text) => s with { Output = text });

        // Should not throw — tools resolved across both kits
        var workflow = hydrator.Hydrate(cell);
        workflow.ShouldNotBeNull();
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static WorkflowSnapshot BuildAgentCell() => new()
    {
        Name = "agent-cell",
        Domain = "test",
        Tools = ["search"],
        Connections = ["handle -> End"],
        Jobs = new Dictionary<string, JobSnapshot>
        {
            ["handle"] = new() { Type = "agent", ModelAlias = "default" }
        },
        Models = new Dictionary<string, ModelSnapshot>
        {
            ["default"] = new() { Provider = "openai", Model = Models.OpenAI.Gpt54Mini }
        }
    };

    private sealed record TestState
    {
        public string Input { get; init; } = "test input";
        public string? Output { get; init; }
    }

    private sealed class FakeModel(string response) : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = response });
    }
}
