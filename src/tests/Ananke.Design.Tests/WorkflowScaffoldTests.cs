using Ananke.Orchestration;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Routing;
using Shouldly;

namespace Ananke.Design.Tests;

public record ScaffoldState
{
    public int Value { get; init; }
    public List<string> Trail { get; init; } = [];
}

public record SubFlowChildState
{
    public bool Done { get; init; }
}

[TestFixture]
public class WorkflowScaffoldTests
{
    // ── Parse + JobNames discovery ───────────────────────────────────

    [Test]
    public void Parse_SimpleChain_DiscoversJobNames()
    {
        var scaffold = WorkflowScaffold.Parse<ScaffoldState>("test", """
            a -> b
            b -> c
            c -> End
            """);

        scaffold.JobNames.ShouldBe(["a", "b", "c"], ignoreOrder: true);
    }

    [Test]
    public void Parse_EndIsNotAJobName()
    {
        var scaffold = WorkflowScaffold.Parse<ScaffoldState>("test", "a -> End");

        scaffold.JobNames.ShouldContain("a");
        scaffold.JobNames.ShouldNotContain("End");
    }

    [Test]
    public void Parse_ForkDiscoversAllTargets()
    {
        var scaffold = WorkflowScaffold.Parse<ScaffoldState>("test", """
            start -> fork(branch_a, branch_b)
            join(branch_a, branch_b) -> merge
            merge -> End
            """);

        scaffold.JobNames.ShouldBe(["start", "branch_a", "branch_b", "merge"], ignoreOrder: true);
    }

    [Test]
    public void Parse_RouterDiscoversAllOptions()
    {
        var scaffold = WorkflowScaffold.Parse<ScaffoldState>("test", """
            classify -> router(escalate, auto_resolve)
            escalate -> End
            auto_resolve -> End
            """);

        scaffold.JobNames.ShouldContain("classify");
        scaffold.JobNames.ShouldContain("escalate");
        scaffold.JobNames.ShouldContain("auto_resolve");
    }

    [Test]
    public void Parse_EmptyDsl_Throws()
    {
        Should.Throw<InvalidOperationException>(() =>
            WorkflowScaffold.Parse<ScaffoldState>("test", "# only comments"));
    }

    [Test]
    public void Parse_NullName_Throws()
    {
        Should.Throw<ArgumentException>(() =>
            WorkflowScaffold.Parse<ScaffoldState>(null!, "a -> b"));
    }

    [Test]
    public void Parse_NullDsl_Throws()
    {
        Should.Throw<ArgumentException>(() =>
            WorkflowScaffold.Parse<ScaffoldState>("test", (string)null!));
    }

    // ── UnboundJobs tracking ─────────────────────────────────────────

    [Test]
    public void UnboundJobs_InitiallyContainsAllJobs()
    {
        var scaffold = WorkflowScaffold.Parse<ScaffoldState>("test", """
            a -> b
            b -> End
            """);

        scaffold.UnboundJobs.ShouldBe(["a", "b"], ignoreOrder: true);
    }

    [Test]
    public void UnboundJobs_ShrinkAfterBind()
    {
        var scaffold = WorkflowScaffold.Parse<ScaffoldState>("test", """
            a -> b
            b -> End
            """);

        scaffold.Bind("a", (s, _) => Task.FromResult(s));

        scaffold.UnboundJobs.ShouldBe(["b"]);
    }

    // ── UnboundMerges tracking ───────────────────────────────────────

    [Test]
    public void UnboundMerges_TracksJoinTargets()
    {
        var scaffold = WorkflowScaffold.Parse<ScaffoldState>("test", """
            start -> fork(a, b)
            join(a, b) -> merge
            merge -> End
            """);

        scaffold.UnboundMerges.ShouldContain("merge");
    }

    [Test]
    public void UnboundMerges_ShrinkAfterBindMerge()
    {
        var scaffold = WorkflowScaffold.Parse<ScaffoldState>("test", """
            start -> fork(a, b)
            join(a, b) -> merge
            merge -> End
            """);

        scaffold.BindMerge("merge", states => states[0]);

        scaffold.UnboundMerges.ShouldBeEmpty();
    }

    // ── UnboundRouters tracking ──────────────────────────────────────

    [Test]
    public void UnboundRouters_TracksRouterJobs()
    {
        var scaffold = WorkflowScaffold.Parse<ScaffoldState>("test", """
            classify -> router(a, b)
            a -> End
            b -> End
            """);

        scaffold.UnboundRouters.ShouldContain("classify");
    }

    // ── Bind validation ──────────────────────────────────────────────

    [Test]
    public void Bind_UnknownJobName_Throws()
    {
        var scaffold = WorkflowScaffold.Parse<ScaffoldState>("test", "a -> End");

        Should.Throw<InvalidOperationException>(() =>
            scaffold.Bind("nonexistent", (s, _) => Task.FromResult(s)));
    }

    [Test]
    public void BindMerge_NonJoinTarget_Throws()
    {
        var scaffold = WorkflowScaffold.Parse<ScaffoldState>("test", "a -> End");

        Should.Throw<InvalidOperationException>(() =>
            scaffold.BindMerge("a", states => states[0]));
    }

    [Test]
    public void BindRouter_NonRouterJob_Throws()
    {
        var scaffold = WorkflowScaffold.Parse<ScaffoldState>("test", """
            a -> b
            b -> End
            """);

        Should.Throw<InvalidOperationException>(() =>
            scaffold.BindRouter("a", new TestRouter()));
    }

    // ── Build validation ─────────────────────────────────────────────

    [Test]
    public void Build_WithUnboundJobs_Throws()
    {
        var scaffold = WorkflowScaffold.Parse<ScaffoldState>("test", """
            a -> b
            b -> End
            """);

        scaffold.Bind("a", (s, _) => Task.FromResult(s));
        // "b" is not bound

        var ex = Should.Throw<InvalidOperationException>(() => scaffold.Build());
        ex.Message.ShouldContain("b");
    }

    [Test]
    public void Build_WithUnboundMerge_Throws()
    {
        var scaffold = WorkflowScaffold.Parse<ScaffoldState>("test", """
            start -> fork(a, b)
            join(a, b) -> merge
            merge -> End
            """);

        scaffold.Bind("start", (s, _) => Task.FromResult(s));
        scaffold.Bind("a", (s, _) => Task.FromResult(s));
        scaffold.Bind("b", (s, _) => Task.FromResult(s));
        scaffold.Bind("merge", (s, _) => Task.FromResult(s));
        // merge function not bound

        var ex = Should.Throw<InvalidOperationException>(() => scaffold.Build());
        ex.Message.ShouldContain("merge");
    }

    [Test]
    public void Build_WithUnboundRouter_Throws()
    {
        var scaffold = WorkflowScaffold.Parse<ScaffoldState>("test", """
            classify -> router(a, b)
            a -> End
            b -> End
            """);

        scaffold.Bind("classify", (s, _) => Task.FromResult(s));
        scaffold.Bind("a", (s, _) => Task.FromResult(s));
        scaffold.Bind("b", (s, _) => Task.FromResult(s));
        // router not bound

        var ex = Should.Throw<InvalidOperationException>(() => scaffold.Build());
        ex.Message.ShouldContain("classify");
    }

    // ── Build + Run: direct chain ────────────────────────────────────

    [Test]
    public async Task Build_DirectChain_RunsSuccessfully()
    {
        var workflow = WorkflowScaffold.Parse<ScaffoldState>("test", """
            step1 -> step2
            step2 -> End
            """)
            .Bind("step1", (s, _) => Task.FromResult(s with { Value = 1, Trail = [.. s.Trail, "step1"] }))
            .Bind("step2", (s, _) => Task.FromResult(s with { Value = s.Value + 10, Trail = [.. s.Trail, "step2"] }))
            .Build();

        var execution = await workflow.RunAsync(new ScaffoldState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Value.ShouldBe(11);
        execution.Result.FinalState.Trail.ShouldBe(["step1", "step2"]);
    }

    // ── Build + Run: fork/join ───────────────────────────────────────

    [Test]
    public async Task Build_ForkJoin_RunsInParallel()
    {
        var workflow = WorkflowScaffold.Parse<ScaffoldState>("test", """
            plan -> fork(fetch_a, fetch_b)
            join(fetch_a, fetch_b) -> combine
            combine -> End
            """)
            .Bind("plan", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Bind("fetch_a", (s, _) => Task.FromResult(s with { Value = s.Value + 10 }))
            .Bind("fetch_b", (s, _) => Task.FromResult(s with { Value = s.Value + 100 }))
            .Bind("combine", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "combined"] }))
            .BindMerge("combine", states => new ScaffoldState { Value = states.Sum(s => s.Value) })
            .Build();

        var execution = await workflow.RunAsync(new ScaffoldState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        // plan: 1. fork: (1+10)+(1+100) = 112. combine adds Trail.
        execution.Result!.FinalState.Value.ShouldBe(112);
        execution.Result.FinalState.Trail.ShouldContain("combined");
    }

    // ── Build + Run: fork with mode ──────────────────────────────────

    [Test]
    public async Task Build_ForkBestEffort_ContinuesOnFailure()
    {
        var workflow = WorkflowScaffold.Parse<ScaffoldState>("test", """
            start -> fork(ok, bad, mode: best-effort)
            join(ok, bad) -> after
            after -> End
            """)
            .Bind("start", (s, _) => Task.FromResult(s))
            .Bind("ok", (s, _) => Task.FromResult(s with { Value = 42 }))
            .Bind("bad", (_, _) => throw new InvalidOperationException("fail"))
            .Bind("after", (s, _) => Task.FromResult(s))
            .BindMerge("after", states => states[0])
            .Build();

        var execution = await workflow.RunAsync(new ScaffoldState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Value.ShouldBe(42);
    }

    // ── Build + Run: router ──────────────────────────────────────────

    [Test]
    public async Task Build_Router_RoutesCorrectly()
    {
        var workflow = WorkflowScaffold.Parse<ScaffoldState>("test", """
            classify -> router(high, low)
            high -> End
            low -> End
            """)
            .Bind("classify", (s, _) => Task.FromResult(s with { Value = 10 }))
            .Bind("high", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "high"] }))
            .Bind("low", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "low"] }))
            .BindRouter("classify", new TestRouter())
            .Build();

        var execution = await workflow.RunAsync(new ScaffoldState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Trail.ShouldContain("high");
    }

    // ── Bind with IJob<TState> ───────────────────────────────────────

    [Test]
    public async Task Bind_IJob_WorksLikeLambda()
    {
        var workflow = WorkflowScaffold.Parse<ScaffoldState>("test", "a -> End")
            .Bind("a", new IncrementJob())
            .Build();

        var execution = await workflow.RunAsync(new ScaffoldState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Value.ShouldBe(1);
    }

    // ── Parse from IEnumerable<string> ───────────────────────────────

    [Test]
    public void Parse_LinesOverload_Works()
    {
        var scaffold = WorkflowScaffold.Parse<ScaffoldState>("test",
            new[] { "a -> b", "b -> End" });

        scaffold.JobNames.ShouldBe(["a", "b"], ignoreOrder: true);
    }

    // ── SubFlow directive ────────────────────────────────────────────

    [Test]
    public void Parse_SubFlowDirective_DoesNotAddExtraJobNames()
    {
        var scaffold = WorkflowScaffold.Parse<ScaffoldState>("test", """
            a -> b
            b -> End
            subflow(a)
            """);

        scaffold.JobNames.ShouldBe(["a", "b"], ignoreOrder: true);
    }

    [Test]
    public void Parse_SubFlowReferencingUnknownJob_Throws()
    {
        Should.Throw<InvalidOperationException>(() =>
            WorkflowScaffold.Parse<ScaffoldState>("test", """
                a -> End
                subflow(nonexistent)
                """));
    }

    [Test]
    public void UnboundSubFlows_TracksSubFlowNames()
    {
        var scaffold = WorkflowScaffold.Parse<ScaffoldState>("test", """
            a -> b
            b -> End
            subflow(a)
            """);

        scaffold.UnboundSubFlows.ShouldContain("a");
    }

    [Test]
    public void UnboundSubFlows_ShrinkAfterBindSubFlow()
    {
        var inner = new Workflow<SubFlowChildState>("inner")
            .Job("step", (s, _) => Task.FromResult(s with { Done = true }))
            .Then("step", Workflow.End);

        var scaffold = WorkflowScaffold.Parse<ScaffoldState>("test", """
            a -> b
            b -> End
            subflow(a)
            """);

        scaffold.BindSubFlow("a", inner,
            parent => new SubFlowChildState(),
            (parent, child) => parent with { Value = child.Done ? 1 : 0 });

        scaffold.UnboundSubFlows.ShouldBeEmpty();
    }

    [Test]
    public void UnboundJobs_ExcludesSubFlowBindings()
    {
        var inner = new Workflow<SubFlowChildState>("inner")
            .Job("step", (s, _) => Task.FromResult(s with { Done = true }))
            .Then("step", Workflow.End);

        var scaffold = WorkflowScaffold.Parse<ScaffoldState>("test", """
            a -> b
            b -> End
            subflow(a)
            """);

        scaffold.BindSubFlow("a", inner,
            parent => new SubFlowChildState(),
            (parent, child) => parent with { Value = child.Done ? 1 : 0 });

        scaffold.UnboundJobs.ShouldNotContain("a");
        scaffold.UnboundJobs.ShouldContain("b");
    }

    [Test]
    public void BindSubFlow_NonSubFlowName_Throws()
    {
        var inner = new Workflow<SubFlowChildState>("inner")
            .Job("step", (s, _) => Task.FromResult(s with { Done = true }))
            .Then("step", Workflow.End);

        var scaffold = WorkflowScaffold.Parse<ScaffoldState>("test", """
            a -> b
            b -> End
            """);

        Should.Throw<InvalidOperationException>(() =>
            scaffold.BindSubFlow("a", inner,
                parent => new SubFlowChildState(),
                (parent, child) => parent));
    }

    [Test]
    public void Build_WithUnboundSubFlow_Throws()
    {
        var scaffold = WorkflowScaffold.Parse<ScaffoldState>("test", """
            a -> b
            b -> End
            subflow(a)
            """);

        scaffold.Bind("b", (s, _) => Task.FromResult(s));
        // subflow "a" not bound

        var ex = Should.Throw<InvalidOperationException>(() => scaffold.Build());
        ex.Message.ShouldContain("a");
        ex.Message.ShouldContain("subflow");
    }

    // ── Build + Run: SubFlow ─────────────────────────────────────────

    [Test]
    public async Task Build_SubFlow_RunsNestedWorkflow()
    {
        var inner = new Workflow<SubFlowChildState>("inner")
            .Job("step", (s, _) => Task.FromResult(s with { Done = true }))
            .Then("step", Workflow.End);

        var workflow = WorkflowScaffold.Parse<ScaffoldState>("test", """
            a -> b
            b -> End
            subflow(a)
            """)
            .BindSubFlow("a", inner,
                parent => new SubFlowChildState(),
                (parent, child) => parent with { Value = child.Done ? 99 : 0 })
            .Bind("b", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "b"] }))
            .Build();

        var execution = await workflow.RunAsync(new ScaffoldState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Value.ShouldBe(99);
        execution.Result.FinalState.Trail.ShouldContain("b");
    }

    // ── Interrupt directive ──────────────────────────────────────────

    [Test]
    public void Parse_InterruptReferencingUnknownJob_Throws()
    {
        Should.Throw<InvalidOperationException>(() =>
            WorkflowScaffold.Parse<ScaffoldState>("test", """
                a -> End
                interrupt(nonexistent)
                """));
    }

    // ── Build + Run: Interrupt ───────────────────────────────────────

    [Test]
    public async Task Build_Interrupt_PausesBeforeJob()
    {
        var workflow = WorkflowScaffold.Parse<ScaffoldState>("test", """
            a -> b
            b -> End
            interrupt(b)
            """)
            .Bind("a", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Bind("b", (s, _) => Task.FromResult(s with { Value = 2 }))
            .Build()
            .UseCheckpointing(new InMemoryCheckpointStore());

        var execution = await workflow.RunAsync(new ScaffoldState());

        execution.Status.ShouldBe(ExecutionStatus.Interrupted);
        execution.State.Value.ShouldBe(1);
    }

    // ── Test helpers ─────────────────────────────────────────────────

    private sealed class TestRouter : IRouter<ScaffoldState>
    {
        public Task<string> RouteAsync(ScaffoldState state, CancellationToken ct) =>
            Task.FromResult(state.Value >= 5 ? "high" : "low");
    }

    private sealed class IncrementJob : IJob<ScaffoldState>
    {
        public string Name => "increment";

        public Task<ScaffoldState> ExecuteAsync(ScaffoldState state, CancellationToken ct) =>
            Task.FromResult(state with { Value = state.Value + 1 });
    }
}
