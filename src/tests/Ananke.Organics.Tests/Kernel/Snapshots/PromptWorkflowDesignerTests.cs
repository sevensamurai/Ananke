using Ananke.Abstractions.Agents;
using Ananke.Organics.Kernel.Snapshots;
using Shouldly;

namespace Ananke.Organics.Tests.Kernel.Snapshots;

[TestFixture]
public class PromptWorkflowDesignerTests
{
    // ── DesignAsync basics ──────────────────────────────────────────

    [Test]
    public async Task DesignAsync_ValidYamlResponse_ReturnsWorkflowSnapshot()
    {
        var yaml = BuildValidCellYaml("catalog-assistant", "catalog",
            ["search_catalog", "check_inventory"]);
        var model = new FakeModel(yaml);
        var designer = new PromptWorkflowDesigner(model);

        var cell = await designer.DesignAsync(
            "Create a catalog search assistant",
            ["search_catalog", "check_inventory", "process_payment"]);

        cell.Name.ShouldBe("catalog-assistant");
        cell.Domain.ShouldBe("catalog");
        cell.Tools.ShouldContain("search_catalog");
        cell.Tools.ShouldContain("check_inventory");
    }

    [Test]
    public async Task DesignAsync_ResponseWithMarkdownFences_StripsAndParses()
    {
        var yaml = "```yaml\n" + BuildValidCellYaml("orders", "orders",
            ["create_order"]) + "\n```";
        var model = new FakeModel(yaml);
        var designer = new PromptWorkflowDesigner(model);

        var cell = await designer.DesignAsync("Order manager", ["create_order"]);

        cell.Name.ShouldBe("orders");
    }

    [Test]
    public async Task DesignAsync_ResponseWithPlainFences_StripsAndParses()
    {
        var yaml = "```\n" + BuildValidCellYaml("orders", "orders",
            ["create_order"]) + "\n```";
        var model = new FakeModel(yaml);
        var designer = new PromptWorkflowDesigner(model);

        var cell = await designer.DesignAsync("Order manager", ["create_order"]);

        cell.Name.ShouldBe("orders");
    }

    [Test]
    public async Task DesignAsync_PromptIncludesAvailableTools()
    {
        string[] tools = ["search_catalog", "process_payment"];
        string? capturedPrompt = null;

        var model = new FakeModel(
            BuildValidCellYaml("test", "test", ["search_catalog"]),
            onRequest: req => capturedPrompt = req.Messages[0].Content);
        var designer = new PromptWorkflowDesigner(model);

        await designer.DesignAsync("Create a test assistant", tools);

        capturedPrompt.ShouldNotBeNull();
        capturedPrompt.ShouldContain("search_catalog");
        capturedPrompt.ShouldContain("process_payment");
    }

    [Test]
    public async Task DesignAsync_JobsAndConnections_Preserved()
    {
        var yaml = BuildValidCellYaml("catalog-assistant", "catalog",
            ["search_catalog"]);
        var model = new FakeModel(yaml);
        var designer = new PromptWorkflowDesigner(model);

        var cell = await designer.DesignAsync("Catalog assistant", ["search_catalog"]);

        cell.Jobs.ShouldContainKey("handle-request");
        cell.Connections.Count.ShouldBeGreaterThan(0);
    }

    // ── Error handling ──────────────────────────────────────────────

    [Test]
    public async Task DesignAsync_WithToolKit_IncludesDescriptions()
    {
        string? capturedPrompt = null;

        var toolKit = new Ananke.Orchestration.Tools.ToolKit("test-tools")
            .AddTool("search_catalog", "Search books by title or author",
                (_) => Ananke.Orchestration.Tools.ToolResult.Ok("ok"), "q", "query");

        var model = new FakeModel(
            BuildValidCellYaml("test", "test", ["search_catalog"]),
            onRequest: req => capturedPrompt = req.Messages[0].Content);
        var designer = new PromptWorkflowDesigner(model);

        await designer.DesignAsync("Create a test assistant", toolKit);

        capturedPrompt.ShouldNotBeNull();
        capturedPrompt.ShouldContain("search_catalog");
        capturedPrompt.ShouldContain("Search books by title or author");
    }

    [Test]
    public async Task DesignAsync_EmptyResponse_Throws()
    {
        var model = new FakeModel(null!);
        var designer = new PromptWorkflowDesigner(model);

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            designer.DesignAsync("Create something", ["tool_a"]));

        ex.Message.ShouldContain("empty response");
    }

    [Test]
    public async Task DesignAsync_InvalidYaml_ThrowsWithContext()
    {
        var model = new FakeModel("This is not valid YAML at all");
        var designer = new PromptWorkflowDesigner(model);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            designer.DesignAsync("Create something", ["tool_a"]));
    }

    [Test]
    public void DesignAsync_NullPrompt_Throws()
    {
        var designer = new PromptWorkflowDesigner(new FakeModel(""));

        Should.ThrowAsync<ArgumentException>(() =>
            designer.DesignAsync(null!, ["tool_a"]));
    }

    [Test]
    public void DesignAsync_NullTools_Throws()
    {
        var designer = new PromptWorkflowDesigner(new FakeModel(""));

        Should.ThrowAsync<ArgumentNullException>(() =>
            designer.DesignAsync("Create something", (IReadOnlyList<string>)null!));
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static string BuildValidCellYaml(string name, string domain, string[] tools)
    {
        var toolLines = string.Join("\n", tools.Select(t => $"      - {t}"));
        return $"""
            kernel: test
            version: 1
            taken_at: 2025-01-15T14:30:00Z

            cells:
              {name}:
                domain: {domain}
                tools:
            {toolLines}
                models:
                  default:
                    provider: openai
                    model: gpt-4o-mini
                jobs:
                  handle-request:
                    type: agent
                    model: default
                  respond:
                    type: code
                connections:
                  - handle-request -> respond
            """;
    }

    private sealed class FakeModel(string? responseText, Action<AgentRequest>? onRequest = null) : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            onRequest?.Invoke(request);
            return Task.FromResult(new AgentResponse { Text = responseText });
        }
    }
}
