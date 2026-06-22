using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Memory;
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

public record InterviewState
{
    public string ConversationId { get; init; } = "conversation-1";
    public List<string> Agenda { get; init; } = [];
    public List<string> Transcript { get; init; } = [];
    public bool Complete { get; init; }
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

    // ── Interview: validation ─────────────────────────────────────

    [Test]
    public void Interview_MissingQuestion_Throws()
    {
        var builder = AgenticPattern.Interview<InterviewState>("test")
            .WithWelcome((s, _) => Task.FromResult(s))
            .WithNavigation((_, s) => s)
            .Until(s => s.Complete);

        var ex = Should.Throw<InvalidOperationException>(() => builder.Build());
        ex.Message.ShouldContain("WithQuestion");
    }

    [Test]
    public void Interview_MissingNavigation_Throws()
    {
        var builder = AgenticPattern.Interview<InterviewState>("test")
            .WithWelcome((s, _) => Task.FromResult(s))
            .WithQuestion(s => s.Agenda[0])
            .Until(s => s.Complete);

        var ex = Should.Throw<InvalidOperationException>(() => builder.Build());
        ex.Message.ShouldContain("WithNavigation");
    }

    [Test]
    public void Interview_MissingUntil_Throws()
    {
        var builder = AgenticPattern.Interview<InterviewState>("test")
            .WithWelcome((s, _) => Task.FromResult(s))
            .WithQuestion(s => s.Agenda[0])
            .WithNavigation((_, s) => s);

        var ex = Should.Throw<InvalidOperationException>(() => builder.Build());
        ex.Message.ShouldContain("Until");
    }

    [Test]
    public void Interview_MissingWelcomeAndIcebreaker_Throws()
    {
        var builder = AgenticPattern.Interview<InterviewState>("test")
            .WithQuestion(s => s.Agenda[0])
            .WithNavigation((_, s) => s)
            .Until(s => s.Complete);

        var ex = Should.Throw<InvalidOperationException>(() => builder.Build());
        ex.Message.ShouldContain("WithWelcome");
        ex.Message.ShouldContain("WithIcebreaker");
    }

    [Test]
    public void Interview_MaxTurnsLessThan1_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            AgenticPattern.Interview<InterviewState>("test").MaxTurns(0));
    }

    [Test]
    public void Interview_WithTurnTimeoutNonPositive_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            AgenticPattern.Interview<InterviewState>("test").WithTurnTimeout(TimeSpan.Zero));
    }

    // ── Interview: topology ───────────────────────────────────────

    [Test]
    public void Interview_ProducesValidWorkflow_WithInputJobMarked()
    {
        var interview = AgenticPattern.Interview<InterviewState>("topology")
            .WithWelcome((s, _) => Task.FromResult(s))
            .WithQuestion(s => s.Agenda[0])
            .WithNavigation((_, s) => s)
            .Until(s => s.Complete)
            .Build();

        var def = interview.Workflow.Build();

        def.Name.ShouldBe("topology");
        def.EntryJob.ShouldBe("welcome");
        def.Jobs.ShouldContainKey("ask_question");
        def.InputJobs.ShouldContain("ask_question");
    }

    [Test]
    public async Task Interview_WelcomeAndIcebreaker_RunOnceBeforeFirstTurn()
    {
        var order = new List<string>();

        var interview = AgenticPattern.Interview<InterviewState>("greeting")
            .WithWelcome((s, _) => { order.Add("welcome"); return Task.FromResult(s); })
            .WithIcebreaker((s, _) => { order.Add("icebreaker"); return Task.FromResult(s); })
            .WithQuestion(s => s.Agenda[0])
            .WithNavigation((_, s) => s)
            .Until(s => s.Complete)
            .Build();

        var workflow = interview.Workflow.UseCheckpointing(new InMemoryCheckpointStore());
        var execution = await workflow.RunAsync(new InterviewState { Agenda = ["q1"] });

        execution.Status.ShouldBe(ExecutionStatus.Interrupted);
        order.ShouldBe(["welcome", "icebreaker"]);
    }

    // ── Interview: execution ───────────────────────────────────────

    private static InterviewState Navigate(string answer, InterviewState s)
    {
        var head = s.Agenda[0];
        var rest = s.Agenda.Skip(1).ToList();

        if (answer.StartsWith("expand:", StringComparison.Ordinal))
        {
            return s with
            {
                Agenda = [answer["expand:".Length..], .. rest],
                Transcript = [.. s.Transcript, $"{head}={answer}"]
            };
        }

        if (answer == "skip")
            return s with { Agenda = rest, Complete = rest.Count == 0 };

        if (answer.StartsWith("update:", StringComparison.Ordinal))
        {
            return s with
            {
                Agenda = [answer["update:".Length..], .. rest],
                Transcript = [.. s.Transcript, $"{head}={answer}"]
            };
        }

        return s with
        {
            Agenda = rest,
            Transcript = [.. s.Transcript, $"{head}={answer}"],
            Complete = rest.Count == 0
        };
    }

    [Test]
    public async Task Interview_ExpandSkipAndUpdate_AllAlterAgenda_AndTerminatesOnUntil()
    {
        var store = new InMemoryCheckpointStore();

        var interview = AgenticPattern.Interview<InterviewState>("profile")
            .WithWelcome((s, _) => Task.FromResult(s))
            .WithQuestion(s => s.Agenda[0])
            .WithNavigation(Navigate)
            .Until(s => s.Complete)
            .Build();

        var workflow = interview.Workflow.UseCheckpointing(store);

        var initial = new InterviewState
        {
            Agenda = ["fav-food", "fav-season", "fav-hobby"],
            Transcript = ["fav-color=blue"]
        };

        var execution = await workflow.RunAsync(initial);
        execution.Status.ShouldBe(ExecutionStatus.Interrupted);
        (await interview.GetQuestion(execution.State, default)).ShouldBe("fav-food");

        async Task<WorkflowExecution<InterviewState>> Reply(string executionId, InterviewState state, string answer)
        {
            var next = await interview.FoldAnswer(state, answer, default);
            return await workflow.ResumeAsync(executionId, _ => next);
        }

        execution = await Reply(execution.Id, execution.State, "expand:cuisine");
        execution.Status.ShouldBe(ExecutionStatus.Interrupted);
        (await interview.GetQuestion(execution.State, default)).ShouldBe("cuisine"); // expand: follow-up jumps the queue

        execution = await Reply(execution.Id, execution.State, "italian");
        execution.Status.ShouldBe(ExecutionStatus.Interrupted);
        (await interview.GetQuestion(execution.State, default)).ShouldBe("fav-season");

        execution = await Reply(execution.Id, execution.State, "skip");
        execution.Status.ShouldBe(ExecutionStatus.Interrupted);
        (await interview.GetQuestion(execution.State, default)).ShouldBe("fav-hobby"); // fav-season dropped, no transcript entry

        execution = await Reply(execution.Id, execution.State, "update:fav-color");
        execution.Status.ShouldBe(ExecutionStatus.Interrupted);
        (await interview.GetQuestion(execution.State, default)).ShouldBe("fav-color"); // re-enqueued for revisit

        execution = await Reply(execution.Id, execution.State, "red");
        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Complete.ShouldBeTrue();
        execution.Result.FinalState.Transcript.ShouldBe([
            "fav-color=blue",
            "fav-food=expand:cuisine",
            "cuisine=italian",
            "fav-hobby=update:fav-color",
            "fav-color=red"
        ]);
    }

    [Test]
    public async Task Interview_MaxTurnsCap_ExitsWhenUntilNeverTrue()
    {
        var store = new InMemoryCheckpointStore();

        var interview = AgenticPattern.Interview<InterviewState>("stuck")
            .WithWelcome((s, _) => Task.FromResult(s))
            .WithQuestion(_ => "same question")
            .WithNavigation((answer, s) => s with { Transcript = [.. s.Transcript, answer] })
            .Until(_ => false)
            .MaxTurns(3)
            .Build();

        var workflow = interview.Workflow.UseCheckpointing(store);

        var execution = await workflow.RunAsync(new InterviewState());
        execution.Status.ShouldBe(ExecutionStatus.Interrupted);

        for (var i = 0; i < 3; i++)
        {
            var next = await interview.FoldAnswer(execution.State, "ok", default);
            execution = await workflow.ResumeAsync(execution.Id, _ => next);
        }

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Transcript.ShouldBe(["ok", "ok", "ok"]);
    }

    // ── Interview: memory write-through + turn timeout ────────────

    [Test]
    public async Task Interview_WithMemory_WritesQuestionAndAnswerToConversationMemory()
    {
        var memory = new InMemoryConversationMemory();
        var store = new InMemoryCheckpointStore();

        var interview = AgenticPattern.Interview<InterviewState>("memory-backed")
            .WithWelcome((s, _) => Task.FromResult(s))
            .WithQuestion(s => s.Agenda[0])
            .WithNavigation((answer, s) => s with
            {
                Agenda = [.. s.Agenda.Skip(1)],
                Complete = s.Agenda.Count <= 1
            })
            .Until(s => s.Complete)
            .WithMemory(memory, s => s.ConversationId)
            .Build();

        var workflow = interview.Workflow.UseCheckpointing(store);
        var execution = await workflow.RunAsync(new InterviewState { Agenda = ["fav-color"] });

        var question = await interview.GetQuestion(execution.State, default);
        question.ShouldBe("fav-color");

        var next = await interview.FoldAnswer(execution.State, "blue", default);
        execution = await workflow.ResumeAsync(execution.Id, _ => next);
        execution.Status.ShouldBe(ExecutionStatus.Completed);

        var history = await memory.GetHistoryAsync("conversation-1");
        history.Count.ShouldBe(2);
        history[0].Role.ShouldBe(AgentRole.Assistant);
        history[0].Content.ShouldBe("fav-color");
        history[1].Role.ShouldBe(AgentRole.User);
        history[1].Content.ShouldBe("blue");
    }

    [Test]
    public async Task Interview_TurnTimeout_ExposesPauseMessage_AndStaysResumable()
    {
        var store = new InMemoryCheckpointStore();

        var interview = AgenticPattern.Interview<InterviewState>("pausable")
            .WithWelcome((s, _) => Task.FromResult(s))
            .WithQuestion(s => s.Agenda[0])
            .WithNavigation((answer, s) => s with
            {
                Agenda = [.. s.Agenda.Skip(1)],
                Transcript = [.. s.Transcript, answer],
                Complete = s.Agenda.Count <= 1
            })
            .Until(s => s.Complete)
            .WithTurnTimeout(TimeSpan.FromMinutes(30))
            .Build();

        interview.TurnTimeout.ShouldBe(TimeSpan.FromMinutes(30));

        var workflow = interview.Workflow.UseCheckpointing(store);
        var execution = await workflow.RunAsync(new InterviewState { Agenda = ["fav-color"] });
        execution.Status.ShouldBe(ExecutionStatus.Interrupted);

        // Host's own pending-input wait exceeds TurnTimeout: it shows the pause message but
        // does not abort or otherwise touch the execution — it stays checkpointed as-is.
        var shownToUser = interview.PauseMessage;
        shownToUser.ShouldBe(Interview<InterviewState>.DefaultPauseMessage);
        execution.Status.ShouldBe(ExecutionStatus.Interrupted);

        // The user eventually replies; the paused turn resumes exactly like an on-time one.
        var next = await interview.FoldAnswer(execution.State, "blue", default);
        execution = await workflow.ResumeAsync(execution.Id, _ => next);

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Transcript.ShouldBe(["blue"]);
    }

    [Test]
    public async Task Interview_FakeAdapter_ResumesViaResumeWithInputAsync_AgendaAdvances()
    {
        // A fake platform adapter: it owns nothing but a paused execution id and the next
        // inbound message — exactly the shape a Slack/Discord adapter would have (ADR §4, B4).
        var store = new InMemoryCheckpointStore();

        var interview = AgenticPattern.Interview<InterviewState>("adapter-driven")
            .WithWelcome((s, _) => Task.FromResult(s))
            .WithQuestion(s => s.Agenda[0])
            .WithNavigation(Navigate)
            .Until(s => s.Complete)
            .Build();

        var workflow = interview.Workflow.UseCheckpointing(store);

        var execution = await workflow.RunAsync(new InterviewState { Agenda = ["fav-food", "fav-hobby"] });
        execution.Status.ShouldBe(ExecutionStatus.Interrupted);
        (await interview.GetQuestion(execution.State, default)).ShouldBe("fav-food");

        // "Inbound message" — the adapter correlates it to execution.Id by conversation/thread id
        // (out of scope here) and resumes via the channel-agnostic helper, no manual fold+resume.
        execution = await workflow.ResumeWithInputAsync(
            execution.Id, execution.State, "pizza", interview.FoldAnswer);

        execution.Status.ShouldBe(ExecutionStatus.Interrupted);
        execution.State.Transcript.ShouldBe(["fav-food=pizza"]);
        (await interview.GetQuestion(execution.State, default)).ShouldBe("fav-hobby");

        execution = await workflow.ResumeWithInputAsync(
            execution.Id, execution.State, "chess", interview.FoldAnswer);

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.Result!.FinalState.Transcript.ShouldBe(["fav-food=pizza", "fav-hobby=chess"]);
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

        Should.Throw<ArgumentException>(() =>
            AgenticPattern.Interview<InterviewState>(null!));

        Should.Throw<ArgumentException>(() =>
            AgenticPattern.Interview<InterviewState>(""));
    }
}
