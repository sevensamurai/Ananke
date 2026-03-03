using Ananke.Orchestration.Jobs;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class WorkflowRunnerAdvancedTests
{
    [Test]
    public async Task RunAsync_WithIJobImplementation_Executes()
    {
        var job = new DoubleJob();

        var execution = await new Workflow<CounterState>("ijob-test")
            .Job("double", job)
            .Then("double", Workflow.End)
            .RunAsync(new CounterState { Value = 5 });

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Value.ShouldBe(10);
    }

    [Test]
    public async Task RunAsync_DecideAsync_RoutesCorrectly()
    {
        var execution = await new Workflow<CounterState>("decide-async")
            .Job("start", (s, _) => Task.FromResult(s with { Value = 7 }))
            .Job("high", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "high"] }))
            .Job("low", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "low"] }))
            .Then("start", Workflow.DecideAsync<CounterState>(async s =>
            {
                await Task.Yield();
                return s.Value >= 5 ? "high" : "low";
            }))
            .Then("high", Workflow.End)
            .Then("low", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Trail.ShouldBe(new[] { "high" });
    }

    [Test]
    public async Task RunAsync_WorkflowName_PreservedInExecution()
    {
        var execution = await new Workflow<CounterState>("my-workflow-name")
            .Job("noop", (s, _) => Task.FromResult(s))
            .Then("noop", Workflow.End)
            .RunAsync(new CounterState());

        execution.WorkflowName.ShouldBe("my-workflow-name");
    }

    [Test]
    public async Task RunAsync_MultipleDecisions_RouteChain()
    {
        var execution = await new Workflow<CounterState>("multi-decide")
            .Job("init", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("step-a", (s, _) => Task.FromResult(s with
            {
                Value = s.Value + 10,
                Trail = [.. s.Trail, "a"]
            }))
            .Job("step-b", (s, _) => Task.FromResult(s with
            {
                Trail = [.. s.Trail, "b"]
            }))
            .Job("final-high", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "high"] }))
            .Job("final-low", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "low"] }))
            .Then("init", Workflow.Decide<CounterState>(s => s.Value > 0 ? "step-a" : "step-b"))
            .Then("step-a", Workflow.Decide<CounterState>(s => s.Value > 5 ? "final-high" : "final-low"))
            .Then("step-b", "final-low")
            .Then("final-high", Workflow.End)
            .Then("final-low", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        // init sets Value=1, step-a adds 10 → Value=11, then routes to final-high
        execution.Result!.FinalState.Trail.ShouldBe(new[] { "a", "high" });
    }

    [Test]
    public async Task RunAsync_EmptyNameThrows()
    {
        Should.Throw<ArgumentException>(() => new Workflow<CounterState>(""));
    }

    [Test]
    public async Task RunAsync_CancellationMidJob_Cancels()
    {
        using var cts = new CancellationTokenSource();

        var execution = await new Workflow<CounterState>("cancel-mid")
            .Job("a", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("slow", async (s, ct) =>
            {
                await cts.CancelAsync();
                ct.ThrowIfCancellationRequested();
                return s;
            })
            .Chain("a", "slow", Workflow.End)
            .RunAsync(new CounterState(), cts.Token);

        execution.Status.ShouldBe(ExecutionStatus.Cancelled);
    }

    [Test]
    public async Task Build_WorkflowDefinitionIsImmutable()
    {
        var workflow = new Workflow<CounterState>("immutable-test")
            .Job("a", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("b", (s, _) => Task.FromResult(s with { Value = 2 }))
            .Chain("a", "b", Workflow.End);

        var def = workflow.Build();

        def.Name.ShouldBe("immutable-test");
        def.Jobs.Count.ShouldBe(2);
        def.EntryJob.ShouldBe("a");
        def.Connections.Count.ShouldBe(2);
    }
}

file class DoubleJob : IJob<CounterState>
{
    public string Name => "double";

    public Task<CounterState> ExecuteAsync(CounterState state, CancellationToken ct = default) =>
        Task.FromResult(state with { Value = state.Value * 2 });
}
