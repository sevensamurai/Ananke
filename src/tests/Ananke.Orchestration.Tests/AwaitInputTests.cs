using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Checkpointing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class AwaitInputTests
{
    private InMemoryCheckpointStore _store = null!;

    [SetUp]
    public void Setup()
    {
        _store = new InMemoryCheckpointStore();
    }

    [Test]
    public async Task AwaitInput_PausesBeforeJob_LikeInterruptBefore()
    {
        var executed = new List<string>();

        var workflow = new Workflow<CounterState>("await-input")
            .Job("a", (s, _) =>
            {
                executed.Add("a");
                return Task.FromResult(s with { Value = 1 });
            })
            .Job("ask_question", (s, _) =>
            {
                executed.Add("ask_question");
                return Task.FromResult(s with { Value = 2 });
            })
            .Chain("a", "ask_question", Workflow.End)
            .AwaitInput("ask_question")
            .UseCheckpointing(_store);

        var first = await workflow.RunAsync(new CounterState());

        first.Status.ShouldBe(ExecutionStatus.Interrupted);
        executed.ShouldBe(new[] { "a" });
        first.State.Value.ShouldBe(1);
    }

    [Test]
    public void AwaitInput_AddsJobToInputJobs()
    {
        var workflow = new Workflow<CounterState>("input-jobs")
            .Job("a", (s, _) => Task.FromResult(s))
            .Job("ask_question", (s, _) => Task.FromResult(s))
            .Chain("a", "ask_question", Workflow.End)
            .AwaitInput("ask_question")
            .UseCheckpointing(_store);

        var definition = workflow.Build();

        definition.InputJobs.ShouldContain("ask_question");
    }

    [Test]
    public void AwaitInput_DoesNotMarkPlainInterrupts_AsInputJobs()
    {
        var workflow = new Workflow<CounterState>("mixed-pauses")
            .Job("a", (s, _) => Task.FromResult(s))
            .Job("approve", (s, _) => Task.FromResult(s))
            .Job("ask_question", (s, _) => Task.FromResult(s))
            .Chain("a", "approve", "ask_question", Workflow.End)
            .InterruptBefore("approve")
            .AwaitInput("ask_question")
            .UseCheckpointing(_store);

        var definition = workflow.Build();

        definition.InputJobs.ShouldContain("ask_question");
        definition.InputJobs.ShouldNotContain("approve");
    }

    [Test]
    public async Task AwaitInput_ResumeWithTransform_InjectsReply()
    {
        var workflow = new Workflow<CounterState>("interview-turn")
            .Job("a", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("ask_question", (s, _) => Task.FromResult(s))
            .Chain("a", "ask_question", Workflow.End)
            .AwaitInput("ask_question")
            .UseCheckpointing(_store);

        var first = await workflow.RunAsync(new CounterState());
        first.Status.ShouldBe(ExecutionStatus.Interrupted);

        // Host folds the user's free-text reply into state on resume.
        var resumed = await workflow.ResumeAsync(first.Id,
            state => state with { Trail = [.. state.Trail, "remote, PST"] });

        resumed.Status.ShouldBe(ExecutionStatus.Completed);
        resumed.Result!.FinalState.Trail.ShouldBe(["remote, PST"]);
    }
}
