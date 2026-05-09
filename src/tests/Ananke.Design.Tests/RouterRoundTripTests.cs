using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tools.Routing;
using Ananke.Design.Tools;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Tools.Gating;
using Ananke.Orchestration.Tools.Routing;
using Shouldly;

namespace Ananke.Design.Tests;

[TestFixture]
public class RouterRoundTripTests
{
    // ── Parse: single llm stage ────────────────────────────────────────

    [Test]
    public void Parse_RouterBlock_LlmOnly()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  fast:",
            "    provider: openai",
            "    model: gpt-4.1-mini",
            "tools:",
            "  search:",
            "    name: search",
            "    description: Searches the web",
            "    binding:",
            "      kind: code",
            "      reference: code:search",
            "jobs:",
            "  plan:",
            "    type: agent",
            "    model: fast",
            "    tools:",
            "      - search",
            "    router:",
            "      - kind: llm",
            "        model: fast",
            "        max_selected: 3",
            "connections:",
            "  - plan -> END",
        ]);

        var job = manifest.Jobs["plan"];
        job.Router.Count.ShouldBe(1);

        var stage = job.Router[0].ShouldBeOfType<LlmStageDescriptor>();
        stage.Kind.ShouldBe("llm");
        stage.Model.ShouldBe("fast");
        stage.MaxSelected.ShouldBe(3);
    }

    // ── Parse: full chain (Example B) ────────────────────────────────

    [Test]
    public void Parse_RouterBlock_FullChain()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  fast:",
            "    provider: openai",
            "    model: gpt-4.1-mini",
            "tools:",
            "  search:",
            "    name: search",
            "    description: Searches the web",
            "    binding:",
            "      kind: code",
            "      reference: code:search",
            "  list_tools:",
            "    name: list_tools",
            "    description: Lists available tools",
            "    binding:",
            "      kind: code",
            "      reference: code:list_tools",
            "jobs:",
            "  plan:",
            "    type: agent",
            "    model: fast",
            "    tools:",
            "      - search",
            "      - list_tools",
            "    router:",
            "      - kind: pinned",
            "        tools: [list_tools]",
            "      - kind: health_filter",
            "      - kind: semantic_recall",
            "        top_k: 8",
            "      - kind: affinity_rerank",
            "      - kind: llm",
            "        model: fast",
            "        max_selected: 3",
            "connections:",
            "  - plan -> END",
        ]);

        var stages = manifest.Jobs["plan"].Router;
        stages.Count.ShouldBe(5);

        stages[0].ShouldBeOfType<PinnedStageDescriptor>()
            .Tools.ShouldContain("list_tools");

        stages[1].ShouldBeOfType<HealthFilterStageDescriptor>();

        var recall = stages[2].ShouldBeOfType<SemanticRecallStageDescriptor>();
        recall.TopK.ShouldBe(8);

        stages[3].ShouldBeOfType<AffinityRerankStageDescriptor>();

        var llm = stages[4].ShouldBeOfType<LlmStageDescriptor>();
        llm.Model.ShouldBe("fast");
        llm.MaxSelected.ShouldBe(3);
    }

    // ── Parse: job without router → empty list ─────────────────────────

    [Test]
    public void Parse_NoRouterBlock_RouterIsEmpty()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  fast:",
            "    provider: openai",
            "    model: gpt-4.1-mini",
            "tools:",
            "  search:",
            "    name: search",
            "    description: Searches the web",
            "    binding:",
            "      kind: code",
            "      reference: code:search",
            "jobs:",
            "  plan:",
            "    type: agent",
            "    model: fast",
            "    tools:",
            "      - search",
            "connections:",
        ]);

        manifest.Jobs["plan"].Router.ShouldBeEmpty();
    }

    // ── Parse: unknown kind throws ANANKE_ROUTER_001 ──────────────────

    [Test]
    public void Parse_UnknownRouterKind_Throws()
    {
        Should.Throw<InvalidOperationException>(() => WorkflowManifest.Parse([
            "name: test",
            "models:",
            "jobs:",
            "  plan:",
            "    type: agent",
            "    model: fast",
            "    tools:",
            "    router:",
            "      - kind: turbo_magic",
            "connections:",
        ])).Message.ShouldContain("ANANKE_ROUTER_001");
    }

    // ── Parse: llm stage missing model throws ANANKE_ROUTER_002 ───────

    [Test]
    public void Parse_LlmStageMissingModel_Throws()
    {
        Should.Throw<InvalidOperationException>(() => WorkflowManifest.Parse([
            "name: test",
            "models:",
            "jobs:",
            "  plan:",
            "    type: agent",
            "    model: fast",
            "    tools:",
            "    router:",
            "      - kind: llm",
            "connections:",
        ])).Message.ShouldContain("ANANKE_ROUTER_002");
    }

    // ── Parse: semantic_recall defaults top_k to 8 ────────────────────

    [Test]
    public void Parse_SemanticRecall_DefaultsTopK()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "jobs:",
            "  plan:",
            "    type: agent",
            "    tools:",
            "    router:",
            "      - kind: semantic_recall",
            "connections:",
        ]);

        manifest.Jobs["plan"].Router[0]
            .ShouldBeOfType<SemanticRecallStageDescriptor>()
            .TopK.ShouldBe(8);
    }
}

[TestFixture]
public class RouterStageFactoryTests
{
    // ── Builds semantic_recall stage ──────────────────────────────────

    [Test]
    public void Build_SemanticRecallStage_RequiresMemory()
    {
        var stages = new List<RouterStageDescriptor>
        {
            new SemanticRecallStageDescriptor { Kind = "semantic_recall" }
        };

        Should.Throw<InvalidOperationException>(
            () => RouterStageFactory.Build(stages, memory: null))
            .Message.ShouldContain("ANANKE_ROUTER_001");
    }

    [Test]
    public void Build_SemanticRecallStage_WithMemory_Succeeds()
    {
        var memory = new InMemoryToolMemory();
        var stages = new List<RouterStageDescriptor>
        {
            new SemanticRecallStageDescriptor { Kind = "semantic_recall", TopK = 5 }
        };

        var router = RouterStageFactory.Build(stages, memory: memory);
        router.ShouldNotBeNull();
    }

    // ── Builds llm stage ──────────────────────────────────────────────

    [Test]
    public void Build_LlmStage_WithoutResolver_Throws()
    {
        var stages = new List<RouterStageDescriptor>
        {
            new LlmStageDescriptor { Kind = "llm", Model = "fast" }
        };

        Should.Throw<InvalidOperationException>(
            () => RouterStageFactory.Build(stages, modelResolver: null))
            .Message.ShouldContain("ANANKE_ROUTER_002");
    }

    [Test]
    public void Build_LlmStage_WithUnresolvableModel_Throws()
    {
        var stages = new List<RouterStageDescriptor>
        {
            new LlmStageDescriptor { Kind = "llm", Model = "nonexistent" }
        };

        Should.Throw<InvalidOperationException>(
            () => RouterStageFactory.Build(stages, modelResolver: _ => null))
            .Message.ShouldContain("ANANKE_ROUTER_002");
    }

    [Test]
    public void Build_LlmStage_WithResolver_Succeeds()
    {
        var stages = new List<RouterStageDescriptor>
        {
            new LlmStageDescriptor { Kind = "llm", Model = "fast" }
        };

        var router = RouterStageFactory.Build(stages,
            modelResolver: _ => new StubAgentModel());
        router.ShouldNotBeNull();
    }

    // ── Full chain materialises without error ─────────────────────────

    [Test]
    public void Build_FullChain_Succeeds()
    {
        var memory = new InMemoryToolMemory();
        var stages = new List<RouterStageDescriptor>
        {
            new PinnedStageDescriptor { Kind = "pinned", Tools = ["list_tools"] },
            new HealthFilterStageDescriptor { Kind = "health_filter" },
            new SemanticRecallStageDescriptor { Kind = "semantic_recall", TopK = 8 },
            new AffinityRerankStageDescriptor { Kind = "affinity_rerank" },
            new HeuristicTagsStageDescriptor { Kind = "heuristic_tags" },
            new LlmStageDescriptor { Kind = "llm", Model = "fast" },
        };

        var router = RouterStageFactory.Build(stages, memory,
            modelResolver: _ => new StubAgentModel());
        router.ShouldNotBeNull();
    }

    // ── WorkflowToolResolver attaches router to kit ───────────────────

    [Test]
    public async Task ResolveJobToolKitsAsync_WithRouter_AttachesRouterToKit()
    {
        var manifest = new WorkflowManifest
        {
            Name = "test",
            Models = [],
            Tools = new Dictionary<string, ToolManifestEntry>
            {
                ["search"] = new() { Key = "search", Name = "search", Description = "Search",
                    Binding = new ToolManifestBinding { Reference = "code:search" } }
            },
            Jobs = new Dictionary<string, JobDefinition>
            {
                ["plan"] = new JobDefinition
                {
                    Tools = ["search"],
                    Router =
                    [
                        new HealthFilterStageDescriptor { Kind = "health_filter" },
                    ],
                }
            },
            Connections = [],
        };

        var tool = new ToolDefinition
        {
            Name = "search", Description = "Search",
            Parameters = [],
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
        };
        var resolver = new InMemoryToolBindingResolver().Register("code:search", tool);

        var kits = await WorkflowToolResolver.ResolveJobToolKitsAsync(manifest, resolver);

        kits["plan"].Router.ShouldNotBeNull();
    }

    // ── WorkflowToolResolver: no router → kit.Router is null ──────────

    [Test]
    public async Task ResolveJobToolKitsAsync_WithoutRouter_KitRouterIsNull()
    {
        var manifest = new WorkflowManifest
        {
            Name = "test",
            Models = [],
            Tools = new Dictionary<string, ToolManifestEntry>
            {
                ["search"] = new() { Key = "search", Name = "search", Description = "Search",
                    Binding = new ToolManifestBinding { Reference = "code:search" } }
            },
            Jobs = new Dictionary<string, JobDefinition>
            {
                ["plan"] = new() { Tools = ["search"] }
            },
            Connections = [],
        };

        var tool = new ToolDefinition
        {
            Name = "search", Description = "Search",
            Parameters = [],
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
        };
        var resolver = new InMemoryToolBindingResolver().Register("code:search", tool);

        var kits = await WorkflowToolResolver.ResolveJobToolKitsAsync(manifest, resolver);

        kits["plan"].Router.ShouldBeNull();
    }

    // ── helpers ───────────────────────────────────────────────────────

    private sealed class StubAgentModel : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse { Text = "{\"useTools\":false,\"selectedToolNames\":[],\"confidence\":\"high\"}" });
    }
}
