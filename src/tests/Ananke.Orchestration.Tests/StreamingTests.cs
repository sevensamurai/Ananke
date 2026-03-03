using Ananke.Orchestration.Streaming;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class StreamingTests
{
    [Test]
    public async Task StreamAsync_EmitsJobStartedAndCompleted()
    {
        var events = new List<WorkflowEvent<CounterState>>();

        await foreach (var evt in new Workflow<CounterState>("stream-basic")
            .Job("a", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Then("a", Workflow.End)
            .StreamAsync(new CounterState()))
        {
            events.Add(evt);
        }

        events.OfType<JobStarted<CounterState>>().Count().ShouldBe(1);
        events.OfType<JobStarted<CounterState>>().First().JobName.ShouldBe("a");

        events.OfType<JobCompleted<CounterState>>().Count().ShouldBe(1);
        events.OfType<JobCompleted<CounterState>>().First().JobName.ShouldBe("a");

        events.OfType<WorkflowCompleted<CounterState>>().Count().ShouldBe(1);
        events.OfType<WorkflowCompleted<CounterState>>().First().Result.Success.ShouldBeTrue();
    }

    [Test]
    public async Task StreamAsync_LinearChain_EmitsEventsInOrder()
    {
        var jobNames = new List<string>();

        await foreach (var evt in new Workflow<CounterState>("stream-chain")
            .Job("a", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "a"] }))
            .Job("b", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "b"] }))
            .Chain("a", "b", Workflow.End)
            .StreamAsync(new CounterState()))
        {
            if (evt is JobStarted<CounterState> js)
                jobNames.Add($"start:{js.JobName}");
            else if (evt is JobCompleted<CounterState> jc)
                jobNames.Add($"done:{jc.JobName}");
        }

        jobNames.ShouldBe(new[] { "start:a", "done:a", "start:b", "done:b" });
    }

    [Test]
    public async Task StreamAsync_EmitsStateUpdated()
    {
        var stateEvents = new List<StateUpdated<CounterState>>();

        await foreach (var evt in new Workflow<CounterState>("stream-state")
            .Job("a", (s, _) => Task.FromResult(s with { Value = 42 }))
            .Then("a", Workflow.End)
            .StreamAsync(new CounterState()))
        {
            if (evt is StateUpdated<CounterState> su)
                stateEvents.Add(su);
        }

        stateEvents.Count.ShouldBe(1);
        stateEvents[0].State.Value.ShouldBe(42);
    }

    [Test]
    public async Task StreamAsync_FailingJob_EmitsWorkflowFaulted()
    {
        var events = new List<WorkflowEvent<CounterState>>();

        await foreach (var evt in new Workflow<CounterState>("stream-fault")
            .Job("boom", (_, _) => throw new InvalidOperationException("kaboom"))
            .Then("boom", Workflow.End)
            .StreamAsync(new CounterState()))
        {
            events.Add(evt);
        }

        events.OfType<WorkflowFaulted<CounterState>>().Count().ShouldBe(1);
        events.OfType<WorkflowFaulted<CounterState>>().First().Exception.Message.ShouldBe("kaboom");
        events.OfType<WorkflowCompleted<CounterState>>().Count().ShouldBe(0);
    }

    [Test]
    public async Task StreamAsync_AllEventsHaveWorkflowNameAndExecutionId()
    {
        await foreach (var evt in new Workflow<CounterState>("named-wf")
            .Job("x", (s, _) => Task.FromResult(s))
            .Then("x", Workflow.End)
            .StreamAsync(new CounterState()))
        {
            evt.WorkflowName.ShouldBe("named-wf");
            evt.ExecutionId.ShouldNotBeNullOrWhiteSpace();
        }
    }
}
