using Ananke.Orchestration;
using Ananke.Orchestration.Routing;
using Shouldly;

namespace Ananke.Design.Tests;

[TestFixture]
public class WorkflowTopologyExporterTests
{
    // ── ToDsl: simple chain ──────────────────────────────────────────

    [Test]
    public void ToDsl_SimpleChain_EmitsDirectConnections()
    {
        var workflow = new Workflow<ScaffoldState>("test")
            .Job("a", (s, _) => Task.FromResult(s))
            .Job("b", (s, _) => Task.FromResult(s))
            .Then("a", "b")
            .Then("b", Workflow.End);

        var lines = workflow.ToDsl();

        lines.Count.ShouldBe(2);
        lines[0].ShouldBe("a -> b");
        lines[1].ShouldBe("b -> __end__");
    }

    // ── ToDsl: fork ──────────────────────────────────────────────────

    [Test]
    public void ToDsl_Fork_EmitsForkSyntax()
    {
        var workflow = new Workflow<ScaffoldState>("test")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("a", (s, _) => Task.FromResult(s))
            .Job("b", (s, _) => Task.FromResult(s))
            .Job("merge", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork("a", "b"))
            .Join(["a", "b"], "merge", states => states[0])
            .Then("merge", Workflow.End);

        var lines = workflow.ToDsl();

        lines.ShouldContain("start -> fork(a, b)");
        lines.ShouldContain("join(a, b) -> merge");
        lines.ShouldContain("merge -> __end__");
    }

    [Test]
    public void ToDsl_ForkBestEffort_EmitsMode()
    {
        var workflow = new Workflow<ScaffoldState>("test")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("a", (s, _) => Task.FromResult(s))
            .Job("b", (s, _) => Task.FromResult(s))
            .Job("merge", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork(ForkMode.BestEffort, "a", "b"))
            .Join(["a", "b"], "merge", states => states[0])
            .Then("merge", Workflow.End);

        var lines = workflow.ToDsl();

        lines.ShouldContain("start -> fork(a, b, mode: best-effort)");
    }

    // ── ToDsl: loop ──────────────────────────────────────────────────

    [Test]
    public void ToDsl_Loop_EmitsAnnotation()
    {
        var workflow = new Workflow<ScaffoldState>("test")
            .Job("generate", (s, _) => Task.FromResult(s))
            .Job("critique", (s, _) => Task.FromResult(s))
            .Then("generate", "critique")
            .Loop("critique", loopTarget: "generate", exitTarget: Workflow.End,
                  until: _ => true, maxIterations: 5);

        var lines = workflow.ToDsl();

        lines.ShouldContain("generate -> critique");
        lines.ShouldContain(l => l.Contains("loop") && l.Contains("max: 5"));
    }

    // ── ToDsl: router ────────────────────────────────────────────────

    [Test]
    public void ToDsl_Router_EmitsRouterPlaceholder()
    {
        var workflow = new Workflow<ScaffoldState>("test")
            .Job("classify", (s, _) => Task.FromResult(s))
            .Job("high", (s, _) => Task.FromResult(s))
            .Job("low", (s, _) => Task.FromResult(s))
            .Then("classify", new SimpleRouter())
            .Then("high", Workflow.End)
            .Then("low", Workflow.End);

        var lines = workflow.ToDsl();

        lines.ShouldContain(l => l.Contains("classify") && l.Contains("router"));
    }

    // ── ToDsl: interrupt ─────────────────────────────────────────────

    [Test]
    public void ToDsl_Interrupt_EmitsInterruptDirective()
    {
        var workflow = new Workflow<ScaffoldState>("test")
            .Job("process", (s, _) => Task.FromResult(s))
            .Job("review", (s, _) => Task.FromResult(s))
            .Then("process", "review")
            .Then("review", Workflow.End)
            .InterruptBefore("review");

        var lines = workflow.ToDsl();

        lines.ShouldContain("interrupt(review)");
    }

    // ── ToDsl: AgenticPattern round-trip ─────────────────────────────

    [Test]
    public void ToDsl_ReviewCritiquePattern_EmitsLoopTopology()
    {
        var workflow = AgenticPattern.ReviewCritique<ScaffoldState>("draft-review")
            .WithGenerator("writer", (s, _) => Task.FromResult(s))
            .WithCritic("editor", (s, _) => Task.FromResult(s))
            .Until(_ => true)
            .MaxIterations(3)
            .Build();

        var lines = workflow.ToDsl();

        lines.ShouldContain("writer -> editor");
        lines.ShouldContain(l => l.Contains("loop") && l.Contains("writer"));
    }

    [Test]
    public void ToDsl_IterativeRefinementPattern_EmitsSelfLoop()
    {
        var workflow = AgenticPattern.IterativeRefinement<ScaffoldState>("polish")
            .WithAgent("refine", (s, _) => Task.FromResult(s))
            .Until(_ => true)
            .MaxIterations(8)
            .Build();

        var lines = workflow.ToDsl();

        lines.ShouldContain(l => l.Contains("loop") && l.Contains("refine") && l.Contains("max: 8"));
    }

    // ── ToManifestYaml ───────────────────────────────────────────────

    [Test]
    public void ToManifestYaml_SimpleChain_ContainsStructure()
    {
        var workflow = new Workflow<ScaffoldState>("my-pipeline")
            .Job("extract", (s, _) => Task.FromResult(s))
            .Job("transform", (s, _) => Task.FromResult(s))
            .Then("extract", "transform")
            .Then("transform", Workflow.End);

        var yaml = workflow.ToManifestYaml();

        yaml.ShouldContain("name: my-pipeline");
        yaml.ShouldContain("models:");
        yaml.ShouldContain("jobs:");
        yaml.ShouldContain("extract:");
        yaml.ShouldContain("transform:");
        yaml.ShouldContain("connections:");
        yaml.ShouldContain("extract -> transform");
    }

    [Test]
    public void ToManifestYaml_ForkJoin_ContainsDslLines()
    {
        var workflow = new Workflow<ScaffoldState>("parallel")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("a", (s, _) => Task.FromResult(s))
            .Job("b", (s, _) => Task.FromResult(s))
            .Job("merge", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork("a", "b"))
            .Join(["a", "b"], "merge", states => states[0])
            .Then("merge", Workflow.End);

        var yaml = workflow.ToManifestYaml();

        yaml.ShouldContain("fork(a, b)");
        yaml.ShouldContain("join(a, b) -> merge");
    }

    [Test]
    public void ToManifestYaml_DefinitionOverload_SameAsWorkflow()
    {
        var workflow = new Workflow<ScaffoldState>("test")
            .Job("a", (s, _) => Task.FromResult(s))
            .Then("a", Workflow.End);

        var fromWorkflow = workflow.ToManifestYaml();
        var fromDefinition = workflow.Build().ToManifestYaml();

        fromWorkflow.ShouldBe(fromDefinition);
    }

    // ── Test helpers ─────────────────────────────────────────────────

    private sealed class SimpleRouter : IRouter<ScaffoldState>
    {
        public Task<string> RouteAsync(ScaffoldState state, CancellationToken ct) =>
            Task.FromResult(state.Value >= 5 ? "high" : "low");
    }
}
