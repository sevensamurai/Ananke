using Ananke.Orchestration;
using Ananke.Orchestration.Routing;
using Shouldly;

namespace Ananke.Design.Tests;

[TestFixture]
public class WorkflowDiagramExtensionsTests
{
    // ── Mermaid: basic chain ─────────────────────────────────────────

    [Test]
    public void ToMermaid_SimpleChain_ContainsNodesAndEdges()
    {
        var workflow = new Workflow<ScaffoldState>("test")
            .Job("step1", (s, _) => Task.FromResult(s))
            .Job("step2", (s, _) => Task.FromResult(s))
            .Then("step1", "step2")
            .Then("step2", Workflow.End);

        var mermaid = workflow.ToMermaid();

        mermaid.ShouldContain("graph TD");
        mermaid.ShouldContain("step1");
        mermaid.ShouldContain("step2");
        mermaid.ShouldContain("_end");
        mermaid.ShouldContain("-->");
    }

    [Test]
    public void ToMermaid_EntryJob_StyledGreen()
    {
        var workflow = new Workflow<ScaffoldState>("test")
            .Job("start", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.End);

        var mermaid = workflow.ToMermaid();

        mermaid.ShouldContain("style j_start fill:#4CAF50");
    }

    [Test]
    public void ToMermaid_EntryJob_LabelledWithArrow()
    {
        var workflow = new Workflow<ScaffoldState>("test")
            .Job("entry", (s, _) => Task.FromResult(s))
            .Then("entry", Workflow.End);

        var mermaid = workflow.ToMermaid();

        mermaid.ShouldContain("▶ entry");
    }

    // ── Mermaid: fork ────────────────────────────────────────────────

    [Test]
    public void ToMermaid_Fork_ShowsForkEdges()
    {
        var workflow = new Workflow<ScaffoldState>("test")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("a", (s, _) => Task.FromResult(s))
            .Job("b", (s, _) => Task.FromResult(s))
            .Job("merge", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork("a", "b"))
            .Join(["a", "b"], "merge", states => states[0])
            .Then("merge", Workflow.End);

        var mermaid = workflow.ToMermaid();

        mermaid.ShouldContain("fork");
        mermaid.ShouldContain("j_a");
        mermaid.ShouldContain("j_b");
    }

    [Test]
    public void ToMermaid_ForkBestEffort_ShowsLabel()
    {
        var workflow = new Workflow<ScaffoldState>("test")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("a", (s, _) => Task.FromResult(s))
            .Job("b", (s, _) => Task.FromResult(s))
            .Job("merge", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork(ForkMode.BestEffort, "a", "b"))
            .Join(["a", "b"], "merge", states => states[0])
            .Then("merge", Workflow.End);

        var mermaid = workflow.ToMermaid();

        mermaid.ShouldContain("best-effort");
    }

    // ── Mermaid: join ────────────────────────────────────────────────

    [Test]
    public void ToMermaid_Join_ShowsJoinEdges()
    {
        var workflow = new Workflow<ScaffoldState>("test")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("a", (s, _) => Task.FromResult(s))
            .Job("b", (s, _) => Task.FromResult(s))
            .Job("combine", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork("a", "b"))
            .Join(["a", "b"], "combine", states => states[0])
            .Then("combine", Workflow.End);

        var mermaid = workflow.ToMermaid();

        mermaid.ShouldContain("join");
        mermaid.ShouldContain("j_combine");
    }

    // ── Mermaid: router ──────────────────────────────────────────────

    [Test]
    public void ToMermaid_Router_ShowsDiamondShape()
    {
        var workflow = new Workflow<ScaffoldState>("test")
            .Job("classify", (s, _) => Task.FromResult(s))
            .Job("high", (s, _) => Task.FromResult(s))
            .Job("low", (s, _) => Task.FromResult(s))
            .Then("classify", new SimpleRouter())
            .Then("high", Workflow.End)
            .Then("low", Workflow.End);

        var mermaid = workflow.ToMermaid();

        // Router nodes get diamond/rhombus shape: {" "}
        mermaid.ShouldContain("j_classify{");
    }

    // ── Mermaid: End node ────────────────────────────────────────────

    [Test]
    public void ToMermaid_WithEndConnection_ShowsEndNode()
    {
        var workflow = new Workflow<ScaffoldState>("test")
            .Job("a", (s, _) => Task.FromResult(s))
            .Then("a", Workflow.End);

        var mermaid = workflow.ToMermaid();

        mermaid.ShouldContain("_end");
        mermaid.ShouldContain("End");
    }

    // ── Markdown wrapper ─────────────────────────────────────────────

    [Test]
    public void ToMarkdownMermaid_ContainsMermaidCodeBlock()
    {
        var workflow = new Workflow<ScaffoldState>("my-workflow")
            .Job("a", (s, _) => Task.FromResult(s))
            .Then("a", Workflow.End);

        var md = workflow.ToMarkdownMermaid();

        md.ShouldContain("## my-workflow");
        md.ShouldContain("```mermaid");
        md.ShouldContain("graph TD");
        md.ShouldContain("```");
    }

    // ── WorkflowDefinition overload ──────────────────────────────────

    [Test]
    public void ToMermaid_OnDefinition_SameAsOnWorkflow()
    {
        var workflow = new Workflow<ScaffoldState>("test")
            .Job("a", (s, _) => Task.FromResult(s))
            .Then("a", Workflow.End);

        var fromWorkflow = workflow.ToMermaid();
        var fromDefinition = workflow.Build().ToMermaid();

        fromWorkflow.ShouldBe(fromDefinition);
    }

    // ── Mermaid: loop ──────────────────────────────────────────────

    [Test]
    public void ToMermaid_Loop_ShowsLoopAndExitEdges()
    {
        var workflow = new Workflow<ScaffoldState>("test")
            .Job("generate", (s, _) => Task.FromResult(s))
            .Job("critique", (s, _) => Task.FromResult(s))
            .Job("publish", (s, _) => Task.FromResult(s))
            .Then("generate", "critique")
            .Loop("critique", loopTarget: "generate", exitTarget: "publish",
                  until: _ => true, maxIterations: 5)
            .Then("publish", Workflow.End);

        var mermaid = workflow.ToMermaid();

        mermaid.ShouldContain("loop (max 5)");
        mermaid.ShouldContain("exit");
        mermaid.ShouldContain("j_generate");
        mermaid.ShouldContain("j_critique");
        mermaid.ShouldContain("j_publish");
    }

    [Test]
    public void ToMermaid_LoopExitToEnd_ShowsEndNode()
    {
        var workflow = new Workflow<ScaffoldState>("test")
            .Job("refine", (s, _) => Task.FromResult(s))
            .Loop("refine", loopTarget: "refine", exitTarget: Workflow.End,
                  until: _ => true, maxIterations: 10);

        var mermaid = workflow.ToMermaid();

        mermaid.ShouldContain("_end");
        mermaid.ShouldContain("loop (max 10)");
        mermaid.ShouldContain("exit");
    }

    // ── Test helpers ─────────────────────────────────────────────────

    private sealed class SimpleRouter : IRouter<ScaffoldState>
    {
        public Task<string> RouteAsync(ScaffoldState state, CancellationToken ct) =>
            Task.FromResult(state.Value >= 5 ? "high" : "low");
    }
}
