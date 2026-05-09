using Ananke.Orchestration.Workflows;
using Ananke.TestHelpers;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class OnFaultTests
{
    [Test]
    public async Task OnFault_PerJob_InvokedWhenJobThrows()
    {
        Exception? captured = null;

        var exec = await new Workflow<CounterState>("fault-test")
            .Job("fail", (CounterState _, CancellationToken _) =>
                throw new InvalidOperationException("boom"))
            .OnFault("fail", (_, ex) => { captured = ex; return Task.CompletedTask; })
            .Then("fail", Workflow.End)
            .RunAsync(new CounterState());

        exec.Status.ShouldBe(ExecutionStatus.Faulted);
        captured.ShouldNotBeNull();
        captured.ShouldBeOfType<InvalidOperationException>();
        captured!.Message.ShouldBe("boom");
    }

    [Test]
    public async Task OnFault_PerJob_ReceivesCurrentState()
    {
        CounterState? capturedState = null;

        var exec = await new Workflow<CounterState>("fault-state")
            .Job("setup", (s, _) => Task.FromResult(s with { Value = 42 }))
            .Job("fail", (CounterState _, CancellationToken _) =>
                throw new InvalidOperationException("oops"))
            .OnFault("fail", (state, _) => { capturedState = state; return Task.CompletedTask; })
            .Then("setup", "fail")
            .Then("fail", Workflow.End)
            .RunAsync(new CounterState());

        exec.Status.ShouldBe(ExecutionStatus.Faulted);
        capturedState.ShouldNotBeNull();
        capturedState!.Value.ShouldBe(42);
    }

    [Test]
    public async Task OnFault_WithJobRef_Works()
    {
        var faultHandled = false;

        var exec = await new Workflow<CounterState>("fault-ref")
            .Job("fail", (CounterState _, CancellationToken _) =>
                throw new InvalidOperationException("ref boom"), out var fail)
            .OnFault(fail, (_, _) => { faultHandled = true; return Task.CompletedTask; })
            .Then(fail, Workflow.EndRef)
            .RunAsync(new CounterState());

        exec.Status.ShouldBe(ExecutionStatus.Faulted);
        faultHandled.ShouldBeTrue();
    }

    [Test]
    public async Task OnFault_HandlerException_DoesNotReplaceOriginal()
    {
        // If the OnFault handler itself throws, the original exception should
        // still determine the workflow result.
        var exec = await new Workflow<CounterState>("fault-throws")
            .Job("fail", (CounterState _, CancellationToken _) =>
                throw new InvalidOperationException("original"))
            .OnFault("fail", (_, _) => throw new ApplicationException("handler error"))
            .Then("fail", Workflow.End)
            .RunAsync(new CounterState());

        exec.Status.ShouldBe(ExecutionStatus.Faulted);
        exec.Result!.Exception.ShouldBeOfType<InvalidOperationException>();
        exec.Result.Exception!.Message.ShouldBe("original");
    }

    [Test]
    public async Task OnError_WorkflowLevel_InvokedOnAnyJobFault()
    {
        string? faultedJob = null;
        Exception? captured = null;

        var exec = await new Workflow<CounterState>("error-hook")
            .Job("ok", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("fail", (CounterState _, CancellationToken _) =>
                throw new InvalidOperationException("kaboom"))
            .OnError((_, jobName, ex) =>
            {
                faultedJob = jobName;
                captured = ex;
                return Task.CompletedTask;
            })
            .Then("ok", "fail")
            .Then("fail", Workflow.End)
            .RunAsync(new CounterState());

        exec.Status.ShouldBe(ExecutionStatus.Faulted);
        faultedJob.ShouldBe("fail");
        captured.ShouldNotBeNull();
        captured!.Message.ShouldBe("kaboom");
    }

    [Test]
    public async Task OnFault_And_OnError_BothInvoked()
    {
        var perJobCalled = false;
        var globalCalled = false;

        var exec = await new Workflow<CounterState>("both-hooks")
            .Job("fail", (CounterState _, CancellationToken _) =>
                throw new InvalidOperationException("dual"))
            .OnFault("fail", (_, _) => { perJobCalled = true; return Task.CompletedTask; })
            .OnError((_, _, _) => { globalCalled = true; return Task.CompletedTask; })
            .Then("fail", Workflow.End)
            .RunAsync(new CounterState());

        exec.Status.ShouldBe(ExecutionStatus.Faulted);
        perJobCalled.ShouldBeTrue();
        globalCalled.ShouldBeTrue();
    }

    [Test]
    public async Task OnFault_NotCalled_WhenJobSucceeds()
    {
        var faultCalled = false;

        var exec = await new Workflow<CounterState>("no-fault")
            .Job("ok", (s, _) => Task.FromResult(s with { Value = 1 }))
            .OnFault("ok", (_, _) => { faultCalled = true; return Task.CompletedTask; })
            .Then("ok", Workflow.End)
            .RunAsync(new CounterState());

        exec.Status.ShouldBe(ExecutionStatus.Completed);
        faultCalled.ShouldBeFalse();
    }

    [Test]
    public async Task OnFault_OnTimeout_InvokedWithTimeoutException()
    {
        Exception? captured = null;

        var exec = await new Workflow<CounterState>("fault-timeout")
            .Job("slow", async (s, ct) =>
            {
                await WorkflowLoops.Park(ct);
                return s;
            })
            .OnFault("slow", (_, ex) => { captured = ex; return Task.CompletedTask; })
            .Timeout("slow", TimeSpan.FromMilliseconds(50))
            .Then("slow", Workflow.End)
            .RunAsync(new CounterState());

        exec.Status.ShouldBe(ExecutionStatus.Faulted);
        captured.ShouldNotBeNull();
        captured.ShouldBeOfType<TimeoutException>();
    }

    [Test]
    public void OnFault_UndefinedJob_ThrowsOnBuild()
    {
        #pragma warning disable ANANKE001 // intentional: testing runtime validation of undefined OnFault target
                var workflow = new Workflow<CounterState>("fault-undefined")
                    .Job("a", (s, _) => Task.FromResult(s))
                    .OnFault("missing", (_, _) => Task.CompletedTask)
                    .Then("a", Workflow.End);
        #pragma warning restore ANANKE001

                Should.Throw<InvalidOperationException>(() => workflow.Build());
    }

    [Test]
    public async Task StreamAsync_OnFault_InvokedBeforeFaultedEvent()
    {
        var faultHandled = false;

        var events = new List<string>();

        await foreach (var evt in new Workflow<CounterState>("fault-stream")
            .Job("fail", (CounterState _, CancellationToken _) =>
                throw new InvalidOperationException("stream boom"))
            .OnFault("fail", (_, _) => { faultHandled = true; return Task.CompletedTask; })
            .Then("fail", Workflow.End)
            .StreamAsync(new CounterState()))
        {
            events.Add(evt.GetType().Name);
        }

        faultHandled.ShouldBeTrue();
        events.ShouldContain("WorkflowFaulted`1");
    }
}
