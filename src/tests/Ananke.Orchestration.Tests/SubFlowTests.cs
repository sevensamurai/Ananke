using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class SubFlowTests
{
    private record ChildState
    {
        public int Input { get; init; }
        public int Output { get; init; }
        public List<string> Steps { get; init; } = [];
    }

    [Test]
    public async Task SubFlow_ExecutesNestedWorkflow()
    {
        var inner = new Workflow<ChildState>("child")
            .Job("double", (s, _) => Task.FromResult(s with
            {
                Output = s.Input * 2,
                Steps = [.. s.Steps, "doubled"]
            }))
            .Then("double", Workflow.End);

        var execution = await new Workflow<CounterState>("parent")
            .Job("setup", (s, _) => Task.FromResult(s with { Value = 5 }))
            .SubFlow("child-flow", inner,
                parent => new ChildState { Input = parent.Value },
                (parent, child) => parent with
                {
                    Value = child.Output,
                    Trail = [.. parent.Trail, "child-done"]
                })
            .Chain("setup", "child-flow", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Value.ShouldBe(10);
        execution.Result.FinalState.Trail.ShouldContain("child-done");
    }

    [Test]
    public async Task SubFlow_ChildFailure_FaultsParent()
    {
        var inner = new Workflow<ChildState>("failing-child")
            .Job("explode", (_, _) => throw new InvalidOperationException("child failed"))
            .Then("explode", Workflow.End);

        var execution = await new Workflow<CounterState>("parent-with-fail")
            .SubFlow("child-flow", inner,
                _ => new ChildState(),
                (parent, _) => parent)
            .Then("child-flow", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Faulted);
        execution.Result!.Success.ShouldBeFalse();
    }

    [Test]
    public async Task SubFlow_MultipleJobs_ExecutesChain()
    {
        var inner = new Workflow<ChildState>("multi-child")
            .Job("step1", (s, _) => Task.FromResult(s with
            {
                Output = s.Input + 1,
                Steps = [.. s.Steps, "step1"]
            }))
            .Job("step2", (s, _) => Task.FromResult(s with
            {
                Output = s.Output * 3,
                Steps = [.. s.Steps, "step2"]
            }))
            .Chain("step1", "step2", Workflow.End);

        var execution = await new Workflow<CounterState>("parent-multi")
            .Job("init", (s, _) => Task.FromResult(s with { Value = 10 }))
            .SubFlow("child", inner,
                parent => new ChildState { Input = parent.Value },
                (parent, child) => parent with { Value = child.Output })
            .Chain("init", "child", Workflow.End)
            .RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        // (10 + 1) * 3 = 33
        execution.Result!.FinalState.Value.ShouldBe(33);
    }

    [Test]
    public void SubFlow_ExceedsMaxDepth_Throws()
    {
        // Both SubFlows use maxDepth=1; the outer runs at depth 0 (OK),
        // but the inner runs at depth 1 which equals the limit → throws.
        var leaf = new Workflow<ChildState>("leaf")
            .Job("work", (s, _) => Task.FromResult(s with { Output = 1 }))
            .Then("work", Workflow.End);

        var mid = new Workflow<ChildState>("mid")
            .SubFlow("nested", leaf,
                s => s,
                (parent, child) => parent with { Output = child.Output },
                maxDepth: 1)
            .Then("nested", Workflow.End);

        var top = new Workflow<CounterState>("top")
            .SubFlow("sub", mid,
                _ => new ChildState(),
                (parent, child) => parent with { Value = child.Output },
                maxDepth: 2) // top allows depth 0→1, but mid's nested checks depth 1 >= 1
            .Then("sub", Workflow.End);

        // The inner subflow exceeds its maxDepth limit
        var execution = top.RunAsync(new CounterState()).GetAwaiter().GetResult();
        execution.Status.ShouldBe(ExecutionStatus.Faulted);
        execution.Result!.Error.ShouldNotBeNull();
        execution.Result.Error.ShouldContain("depth limit");
    }
}
