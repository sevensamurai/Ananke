using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Patterns;
using Ananke.Orchestration.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

public record ReviewState
{
    public string Draft { get; init; } = "";
    public double Score { get; init; }
    public int Revisions { get; init; }
    public List<string> Trail { get; init; } = [];
}

[TestFixture]
public class AgenticPatternTests
{
    // ── ReviewCritique: validation ───────────────────────────────

    [Test]
    public void ReviewCritique_MissingGenerator_Throws()
    {
        var builder = AgenticPattern.ReviewCritique<ReviewState>("test")
            .WithCritic("critic", (s, _) => Task.FromResult(s))
            .Until(s => s.Score >= 0.9);

        var ex = Should.Throw<InvalidOperationException>(() => builder.Build());
        ex.Message.ShouldContain("generator");
        ex.Message.ShouldContain("WithGenerator");
    }

    [Test]
    public void ReviewCritique_MissingCritic_Throws()
    {
        var builder = AgenticPattern.ReviewCritique<ReviewState>("test")
            .WithGenerator("gen", (s, _) => Task.FromResult(s))
            .Until(s => s.Score >= 0.9);

        var ex = Should.Throw<InvalidOperationException>(() => builder.Build());
        ex.Message.ShouldContain("critic");
        ex.Message.ShouldContain("WithCritic");
    }

    [Test]
    public void ReviewCritique_MissingUntil_Throws()
    {
        var builder = AgenticPattern.ReviewCritique<ReviewState>("test")
            .WithGenerator("gen", (s, _) => Task.FromResult(s))
            .WithCritic("critic", (s, _) => Task.FromResult(s));

        var ex = Should.Throw<InvalidOperationException>(() => builder.Build());
        ex.Message.ShouldContain("Until");
    }

    [Test]
    public void ReviewCritique_MaxIterationsLessThan1_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            AgenticPattern.ReviewCritique<ReviewState>("test").MaxIterations(0));
    }

    // ── ReviewCritique: execution ───────────────────────────────

    [Test]
    public async Task ReviewCritique_ExitsWhenConditionMet()
    {
        LoopExitReason? exitReason = null;

        var workflow = AgenticPattern.ReviewCritique<ReviewState>("draft-review")
            .WithGenerator("generate", (s, _) => Task.FromResult(
                s with { Draft = $"revision-{s.Revisions + 1}", Revisions = s.Revisions + 1 }))
            .WithCritic("critique", (s, _) => Task.FromResult(
                s with { Score = s.Revisions >= 2 ? 0.95 : 0.3 }))
            .Until(s => s.Score >= 0.9)
            .MaxIterations(10)
            .OnLoopExit((_, reason) => exitReason = reason)
            .Build();

        var result = await workflow.RunAsync(new ReviewState());

        result.State.Score.ShouldBeGreaterThanOrEqualTo(0.9);
        result.State.Revisions.ShouldBe(2);
        exitReason.ShouldBe(LoopExitReason.ConditionMet);
    }

    [Test]
    public async Task ReviewCritique_ExitsAtMaxIterations()
    {
        LoopExitReason? exitReason = null;

        var workflow = AgenticPattern.ReviewCritique<ReviewState>("stuck-review")
            .WithGenerator("generate", (s, _) => Task.FromResult(
                s with { Revisions = s.Revisions + 1 }))
            .WithCritic("critique", (s, _) => Task.FromResult(
                s with { Score = 0.1 }))    // never good enough
            .Until(s => s.Score >= 0.9)
            .MaxIterations(3)
            .OnLoopExit((_, reason) => exitReason = reason)
            .Build();

        var result = await workflow.RunAsync(new ReviewState());

        result.State.Revisions.ShouldBe(3);
        exitReason.ShouldBe(LoopExitReason.MaxIterationsReached);
    }

    [Test]
    public async Task ReviewCritique_DefaultMaxIterationsIs5()
    {
        var iterations = 0;

        var workflow = AgenticPattern.ReviewCritique<ReviewState>("default-max")
            .WithGenerator("generate", (s, _) =>
            {
                iterations++;
                return Task.FromResult(s);
            })
            .WithCritic("critique", (s, _) => Task.FromResult(s with { Score = 0.0 }))
            .Until(s => s.Score >= 0.9)    // never met
            .Build();

        await workflow.RunAsync(new ReviewState());

        iterations.ShouldBe(5);
    }

    [Test]
    public async Task ReviewCritique_ProducesValidWorkflow()
    {
        // Verify the built workflow has expected job topology
        var workflow = AgenticPattern.ReviewCritique<ReviewState>("topology")
            .WithGenerator("gen", (s, _) => Task.FromResult(s with { Score = 1.0 }))
            .WithCritic("eval", (s, _) => Task.FromResult(s))
            .Until(s => s.Score >= 0.9)
            .Build();

        var def = workflow.Build();

        def.Name.ShouldBe("topology");
        def.Jobs.ShouldContainKey("gen");
        def.Jobs.ShouldContainKey("eval");
        def.EntryJob.ShouldBe("gen");
    }

    [Test]
    public async Task ReviewCritique_WorksWithSubFlow()
    {
        var innerWorkflow = AgenticPattern.ReviewCritique<ReviewState>("inner")
            .WithGenerator("gen", (s, _) => Task.FromResult(
                s with { Revisions = s.Revisions + 1, Score = 1.0 }))
            .WithCritic("eval", (s, _) => Task.FromResult(s))
            .Until(s => s.Score >= 0.9)
            .Build();

        var outerWorkflow = new Workflow<ReviewState>("outer")
            .SubFlow("review", innerWorkflow,
                mapIn: s => s,
                mapOut: (_, child) => child)
            .Then("review", Workflow.End);

        var result = await outerWorkflow.RunAsync(new ReviewState());

        result.State.Score.ShouldBe(1.0);
        result.State.Revisions.ShouldBe(1);
    }

    // ── IterativeRefinement: validation ──────────────────────────

    [Test]
    public void IterativeRefinement_MissingAgent_Throws()
    {
        var builder = AgenticPattern.IterativeRefinement<ReviewState>("test")
            .Until(s => s.Score >= 0.9);

        var ex = Should.Throw<InvalidOperationException>(() => builder.Build());
        ex.Message.ShouldContain("agent");
        ex.Message.ShouldContain("WithAgent");
    }

    [Test]
    public void IterativeRefinement_MissingUntil_Throws()
    {
        var builder = AgenticPattern.IterativeRefinement<ReviewState>("test")
            .WithAgent("refine", (s, _) => Task.FromResult(s));

        var ex = Should.Throw<InvalidOperationException>(() => builder.Build());
        ex.Message.ShouldContain("Until");
    }

    [Test]
    public void IterativeRefinement_MaxIterationsLessThan1_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            AgenticPattern.IterativeRefinement<ReviewState>("test").MaxIterations(0));
    }

    // ── IterativeRefinement: execution ───────────────────────────

    [Test]
    public async Task IterativeRefinement_ExitsWhenConditionMet()
    {
        LoopExitReason? exitReason = null;

        var workflow = AgenticPattern.IterativeRefinement<ReviewState>("refine")
            .WithAgent("polish", (s, _) => Task.FromResult(
                s with { Revisions = s.Revisions + 1, Score = s.Revisions + 1 >= 3 ? 0.95 : 0.4 }))
            .Until(s => s.Score >= 0.9)
            .MaxIterations(10)
            .OnLoopExit((_, reason) => exitReason = reason)
            .Build();

        var result = await workflow.RunAsync(new ReviewState());

        result.State.Score.ShouldBeGreaterThanOrEqualTo(0.9);
        result.State.Revisions.ShouldBe(3);
        exitReason.ShouldBe(LoopExitReason.ConditionMet);
    }

    [Test]
    public async Task IterativeRefinement_ExitsAtMaxIterations()
    {
        LoopExitReason? exitReason = null;

        var workflow = AgenticPattern.IterativeRefinement<ReviewState>("stuck")
            .WithAgent("refine", (s, _) => Task.FromResult(
                s with { Revisions = s.Revisions + 1, Score = 0.1 }))
            .Until(s => s.Score >= 0.9)
            .MaxIterations(4)
            .OnLoopExit((_, reason) => exitReason = reason)
            .Build();

        var result = await workflow.RunAsync(new ReviewState());

        result.State.Revisions.ShouldBe(4);
        exitReason.ShouldBe(LoopExitReason.MaxIterationsReached);
    }

    [Test]
    public async Task IterativeRefinement_DefaultMaxIterationsIs10()
    {
        var iterations = 0;

        var workflow = AgenticPattern.IterativeRefinement<ReviewState>("default-max")
            .WithAgent("refine", (s, _) =>
            {
                iterations++;
                return Task.FromResult(s with { Score = 0.0 });
            })
            .Until(s => s.Score >= 0.9)    // never met
            .Build();

        await workflow.RunAsync(new ReviewState());

        iterations.ShouldBe(10);
    }

    [Test]
    public async Task IterativeRefinement_SingleIterationWhenAlreadyMet()
    {
        LoopExitReason? exitReason = null;

        var workflow = AgenticPattern.IterativeRefinement<ReviewState>("already-good")
            .WithAgent("refine", (s, _) => Task.FromResult(s with { Score = 1.0 }))
            .Until(s => s.Score >= 0.9)
            .OnLoopExit((_, reason) => exitReason = reason)
            .Build();

        var result = await workflow.RunAsync(new ReviewState());

        result.State.Score.ShouldBe(1.0);
        exitReason.ShouldBe(LoopExitReason.ConditionMet);
    }

    // ── Entry point validation ──────────────────────────────────

    [Test]
    public void AgenticPattern_NullOrWhitespaceName_Throws()
    {
        Should.Throw<ArgumentException>(() =>
            AgenticPattern.ReviewCritique<ReviewState>(null!));

        Should.Throw<ArgumentException>(() =>
            AgenticPattern.ReviewCritique<ReviewState>("  "));

        Should.Throw<ArgumentException>(() =>
            AgenticPattern.IterativeRefinement<ReviewState>(null!));

        Should.Throw<ArgumentException>(() =>
            AgenticPattern.IterativeRefinement<ReviewState>(""));
    }
}
