using Shouldly;

namespace Ananke.StateMachine.Tests;

[TestFixture]
public class InsightSignalTests
{
    private static StateMachine<SimplePhase, SimpleAction> CreateMachine() =>
        StateMachine.Create<SimplePhase, SimpleAction>(
            SimplePhase.Searching, b => b
                .From(SimplePhase.Searching).On(SimpleAction.StartPaperwork).To(SimplePhase.Paperwork)
                .From(SimplePhase.Paperwork).On(SimpleAction.Complete).To(SimplePhase.Done));

    [Test]
    public async Task SignalInsight_InvokesHandler_WithInsightAndCurrentState()
    {
        var machine = CreateMachine();
        string? received = null;
        SimplePhase? receivedState = null;

        machine.OnInsight<string>((insight, state) =>
        {
            received = insight;
            receivedState = state;
            return Task.CompletedTask;
        });

        await machine.SignalInsightAsync("aha!");

        received.ShouldBe("aha!");
        receivedState.ShouldBe(SimplePhase.Searching);
    }

    [Test]
    public async Task SignalInsight_MultipleHandlers_AllInvoked()
    {
        var machine = CreateMachine();
        var invocations = new List<int>();

        machine
            .OnInsight<string>((_, _) => { invocations.Add(1); return Task.CompletedTask; })
            .OnInsight<string>((_, _) => { invocations.Add(2); return Task.CompletedTask; });

        await machine.SignalInsightAsync("test");

        invocations.ShouldBe([1, 2]);
    }

    [Test]
    public async Task SignalInsight_TypedHandler_IgnoresMismatchedType()
    {
        var machine = CreateMachine();
        var stringCalled = false;
        var intCalled = false;

        machine
            .OnInsight<string>((_, _) => { stringCalled = true; return Task.CompletedTask; })
            .OnInsight<int>((_, _) => { intCalled = true; return Task.CompletedTask; });

        await machine.SignalInsightAsync("only-string");

        stringCalled.ShouldBeTrue();
        intCalled.ShouldBeFalse();
    }

    [Test]
    public async Task SignalInsight_DoesNotChangeState()
    {
        var machine = CreateMachine();
        machine.OnInsight<string>((_, _) => Task.CompletedTask);

        await machine.SignalInsightAsync("test");

        machine.CurrentState.ShouldBe(SimplePhase.Searching);
    }

    [Test]
    public async Task SignalInsight_SerializedWithFireAsync()
    {
        var machine = CreateMachine();
        var sequence = new List<string>();
        var insightStarted = new TaskCompletionSource();
        var insightCanFinish = new TaskCompletionSource();

        machine.OnInsight<string>(async (_, _) =>
        {
            sequence.Add("insight-start");
            insightStarted.SetResult();
            await insightCanFinish.Task;
            sequence.Add("insight-end");
        });

        // Start insight - it will block inside the gate
        var insightTask = machine.SignalInsightAsync("slow");
        await insightStarted.Task;

        // Fire a transition while insight holds the gate
        var fireTask = machine.FireAsync(SimpleAction.StartPaperwork);

        // Transition should not have started yet
        sequence.ShouldNotContain("fire");

        // Let the insight finish
        insightCanFinish.SetResult();
        await insightTask;

        var result = await fireTask;
        result.Success.ShouldBeTrue();

        // Insight completed before transition could acquire the gate
        sequence[0].ShouldBe("insight-start");
        sequence[1].ShouldBe("insight-end");
    }

    [Test]
    public async Task SignalInsight_HandlerException_DoesNotBlockOtherHandlers()
    {
        var machine = CreateMachine();
        var secondCalled = false;

        machine
            .OnInsight<string>((_, _) => throw new InvalidOperationException("boom"))
            .OnInsight<string>((_, _) => { secondCalled = true; return Task.CompletedTask; });

        await machine.SignalInsightAsync("test");

        secondCalled.ShouldBeTrue();
    }

    [Test]
    public async Task SignalInsight_HandlerException_DoesNotPoisonGate()
    {
        var machine = CreateMachine();
        machine.OnInsight<string>((_, _) => throw new InvalidOperationException("boom"));

        await machine.SignalInsightAsync("test");

        // Gate should still work — FireAsync must succeed
        var result = await machine.FireAsync(SimpleAction.StartPaperwork);
        result.Success.ShouldBeTrue();
        machine.CurrentState.ShouldBe(SimplePhase.Paperwork);
    }

    [Test]
    public async Task SignalInsight_NoHandlers_Succeeds()
    {
        var machine = CreateMachine();

        // Should not throw
        await machine.SignalInsightAsync("ignored");

        machine.CurrentState.ShouldBe(SimplePhase.Searching);
    }
}
