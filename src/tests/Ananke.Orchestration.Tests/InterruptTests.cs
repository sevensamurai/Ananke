using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Checkpointing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class InterruptTests
{
    private InMemoryCheckpointStore _store = null!;

    [SetUp]
    public void Setup()
    {
        _store = new InMemoryCheckpointStore();
    }

    [Test]
    public async Task InterruptBefore_PausesBeforeJob()
    {
        var executed = new List<string>();

        var workflow = new Workflow<CounterState>("interrupt-before")
            .Job("a", (s, _) =>
            {
                executed.Add("a");
                return Task.FromResult(s with { Value = 1 });
            })
            .Job("b", (s, _) =>
            {
                executed.Add("b");
                return Task.FromResult(s with { Value = 2 });
            })
            .Job("c", (s, _) =>
            {
                executed.Add("c");
                return Task.FromResult(s with { Value = 3 });
            })
            .Chain("a", "b", "c", Workflow.End)
            .InterruptBefore("b")
            .UseCheckpointing(_store);

        var first = await workflow.RunAsync(new CounterState());

        first.Status.ShouldBe(ExecutionStatus.Interrupted);
        executed.ShouldBe(new[] { "a" }); // "a" ran, "b" did not
        first.State.Value.ShouldBe(1);
        _store.Count.ShouldBe(1);
    }

    [Test]
    public async Task InterruptBefore_ResumeContinuesFromInterruptedJob()
    {
        var executed = new List<string>();

        var workflow = new Workflow<CounterState>("resume-before")
            .Job("a", (s, _) =>
            {
                executed.Add("a");
                return Task.FromResult(s with { Value = 1 });
            })
            .Job("b", (s, _) =>
            {
                executed.Add("b");
                return Task.FromResult(s with { Value = 2 });
            })
            .Chain("a", "b", Workflow.End)
            .InterruptBefore("b")
            .UseCheckpointing(_store);

        var first = await workflow.RunAsync(new CounterState());
        first.Status.ShouldBe(ExecutionStatus.Interrupted);

        executed.Clear();
        var resumed = await workflow.ResumeAsync(first.Id);

        resumed.Status.ShouldBe(ExecutionStatus.Completed);
        executed.ShouldBe(new[] { "b" }); // Only "b" runs on resume
        resumed.Result!.FinalState.Value.ShouldBe(2);
    }

    [Test]
    public async Task InterruptAfter_PausesAfterJob()
    {
        var executed = new List<string>();

        var workflow = new Workflow<CounterState>("interrupt-after")
            .Job("a", (s, _) =>
            {
                executed.Add("a");
                return Task.FromResult(s with { Value = 1 });
            })
            .Job("b", (s, _) =>
            {
                executed.Add("b");
                return Task.FromResult(s with { Value = 2 });
            })
            .Chain("a", "b", Workflow.End)
            .InterruptAfter("a")
            .UseCheckpointing(_store);

        var first = await workflow.RunAsync(new CounterState());

        first.Status.ShouldBe(ExecutionStatus.Interrupted);
        executed.ShouldBe(new[] { "a" }); // "a" ran and completed
        first.State.Value.ShouldBe(1);
        _store.Count.ShouldBe(1);
    }

    [Test]
    public async Task InterruptAfter_ResumeContinuesFromNextJob()
    {
        var executed = new List<string>();

        var workflow = new Workflow<CounterState>("resume-after")
            .Job("a", (s, _) =>
            {
                executed.Add("a");
                return Task.FromResult(s with { Value = 1 });
            })
            .Job("b", (s, _) =>
            {
                executed.Add("b");
                return Task.FromResult(s with { Value = 2 });
            })
            .Chain("a", "b", Workflow.End)
            .InterruptAfter("a")
            .UseCheckpointing(_store);

        var first = await workflow.RunAsync(new CounterState());
        first.Status.ShouldBe(ExecutionStatus.Interrupted);

        executed.Clear();
        var resumed = await workflow.ResumeAsync(first.Id);

        resumed.Status.ShouldBe(ExecutionStatus.Completed);
        executed.ShouldBe(new[] { "b" }); // "b" runs on resume
        resumed.Result!.FinalState.Value.ShouldBe(2);
    }

    [Test]
    public void InterruptBefore_OnEntryJob_Throws()
    {
        var workflow = new Workflow<CounterState>("bad-interrupt")
            .Job("entry", (s, _) => Task.FromResult(s))
            .Then("entry", Workflow.End)
            .InterruptBefore("entry")
            .UseCheckpointing(_store);

        Should.Throw<InvalidOperationException>(() => workflow.Build());
    }

    [Test]
    public async Task InterruptBefore_WithoutCheckpointing_Throws()
    {
        var workflow = new Workflow<CounterState>("no-store")
            .Job("a", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("b", (s, _) => Task.FromResult(s with { Value = 2 }))
            .Chain("a", "b", Workflow.End)
            .InterruptBefore("b");
        // No .UseCheckpointing()

        var execution = await workflow.RunAsync(new CounterState());

        execution.Status.ShouldBe(ExecutionStatus.Faulted);
        execution.Result!.Error.ShouldNotBeNull();
        execution.Result.Error.ShouldContain("checkpoint store");
    }

    [Test]
    public async Task ResumeAsync_WithStateTransform_AppliesTransform()
    {
        var workflow = new Workflow<CounterState>("transform-resume")
            .Job("a", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("b", (s, _) => Task.FromResult(s with { Value = s.Value + 100 }))
            .Chain("a", "b", Workflow.End)
            .InterruptAfter("a")
            .UseCheckpointing(_store);

        var first = await workflow.RunAsync(new CounterState());
        first.Status.ShouldBe(ExecutionStatus.Interrupted);

        // Human-in-the-loop: modify state before resume
        var resumed = await workflow.ResumeAsync(first.Id,
            state => state with { Value = 50 });

        resumed.Status.ShouldBe(ExecutionStatus.Completed);
        // b adds 100 to the transformed value of 50
        resumed.Result!.FinalState.Value.ShouldBe(150);
    }
}
