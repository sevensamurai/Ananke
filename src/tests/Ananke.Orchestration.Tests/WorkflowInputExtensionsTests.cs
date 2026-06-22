using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Checkpointing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class WorkflowInputExtensionsTests
{
    private InMemoryCheckpointStore _store = null!;

    [SetUp]
    public void Setup()
    {
        _store = new InMemoryCheckpointStore();
    }

    private static Task<CounterState> Fold(CounterState state, string answer, CancellationToken ct) =>
        Task.FromResult(state with { Trail = [.. state.Trail, answer] });

    [Test]
    public async Task ResumeWithInputAsync_NullWorkflow_Throws()
    {
        Workflow<CounterState>? workflow = null;

        await Should.ThrowAsync<ArgumentNullException>(() =>
            workflow!.ResumeWithInputAsync("id", new CounterState(), "reply", Fold));
    }

    [Test]
    public async Task ResumeWithInputAsync_NullOrWhitespaceExecutionId_Throws()
    {
        var workflow = new Workflow<CounterState>("validation")
            .Job("a", (s, _) => Task.FromResult(s))
            .Then("a", Workflow.End)
            .UseCheckpointing(_store);

        await Should.ThrowAsync<ArgumentException>(() =>
            workflow.ResumeWithInputAsync("  ", new CounterState(), "reply", Fold));
    }

    [Test]
    public async Task ResumeWithInputAsync_NullFold_Throws()
    {
        var workflow = new Workflow<CounterState>("validation")
            .Job("a", (s, _) => Task.FromResult(s))
            .Then("a", Workflow.End)
            .UseCheckpointing(_store);

        await Should.ThrowAsync<ArgumentNullException>(() =>
            workflow.ResumeWithInputAsync("id", new CounterState(), "reply", null!));
    }

    [Test]
    public async Task ResumeWithInputAsync_FoldsReplyThenResumes_NotCoupledToInterviewPattern()
    {
        // A fake platform adapter: correlates an inbound message to a paused AwaitInput turn
        // and resumes it — no Interview pattern involved, just the raw ask/AwaitInput primitive.
        var workflow = new Workflow<CounterState>("adapter-resume")
            .Job("a", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("ask_question", (s, _) => Task.FromResult(s))
            .Chain("a", "ask_question", Workflow.End)
            .AwaitInput("ask_question")
            .UseCheckpointing(_store);

        var execution = await workflow.RunAsync(new CounterState());
        execution.Status.ShouldBe(ExecutionStatus.Interrupted);

        // "Inbound message" arrives later, correlated by the adapter to execution.Id.
        var resumed = await workflow.ResumeWithInputAsync(execution.Id, execution.State, "remote, PST", Fold);

        resumed.Status.ShouldBe(ExecutionStatus.Completed);
        resumed.Result!.FinalState.Trail.ShouldBe(["remote, PST"]);
        resumed.Result.FinalState.Value.ShouldBe(1);
    }
}
