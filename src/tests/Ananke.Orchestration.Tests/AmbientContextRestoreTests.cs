using Ananke.Orchestration.Workflows;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Tracing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Tests for H-7: ambient AsyncLocal context (<see cref="WorkflowTraceContext"/> and
/// <see cref="TokenUsageCapture"/>) must be captured before each job and restored in a
/// <c>finally</c> block so stale values cannot leak into continuations outside the job's
/// execution scope.
/// </summary>
[TestFixture]
public class AmbientContextRestoreTests
{
    // -- WorkflowTraceContext -----------------------------------------

    [Test]
    public async Task TraceContext_IsSetDuringJobExecution()
    {
        TraceInfo? capturedTrace = null;

        await new Workflow<CounterState>("trace-during-job")
            .Job("work", (s, _) =>
            {
                // Inside the job body the context must be set
                capturedTrace = WorkflowTraceContext.Value;
                return Task.FromResult(s with { Value = 1 });
            })
            .Then("work", Workflow.End)
            .RunAsync(new CounterState());

        capturedTrace.ShouldNotBeNull();
        capturedTrace!.WorkflowName.ShouldBe("trace-during-job");
        capturedTrace.CurrentJob.ShouldBe("work");
    }

    [Test]
    public async Task TraceContext_OnEnterAndOnExit_SeeJobScopedContext()
    {
        // OnEnter and OnExit run inside the job's try block and must see the trace
        // pointing at the current job — proving the ambient value is set before they run.
        TraceInfo? traceOnEnter = null;
        TraceInfo? traceOnExit  = null;

        await new Workflow<CounterState>("trace-lifecycle")
            .Job("work", (s, _) => Task.FromResult(s with { Value = 1 }))
            .OnEnter("work", s => { traceOnEnter = WorkflowTraceContext.Value; return Task.CompletedTask; })
            .OnExit ("work", s => { traceOnExit  = WorkflowTraceContext.Value; return Task.CompletedTask; })
            .Then("work", Workflow.End)
            .RunAsync(new CounterState());

        traceOnEnter.ShouldNotBeNull();
        traceOnEnter!.CurrentJob.ShouldBe("work");
        traceOnExit.ShouldNotBeNull();
        traceOnExit!.CurrentJob.ShouldBe("work");
    }

    [Test]
    public async Task TraceContext_EachJob_SeesOwnContext()
    {
        var traces = new List<(string job, string? captured)>();

        await new Workflow<CounterState>("trace-per-job")
            .Job("step-a", (s, _) =>
            {
                traces.Add(("step-a", WorkflowTraceContext.Value?.CurrentJob));
                return Task.FromResult(s with { Value = 1 });
            })
            .Job("step-b", (s, _) =>
            {
                traces.Add(("step-b", WorkflowTraceContext.Value?.CurrentJob));
                return Task.FromResult(s with { Value = 2 });
            })
            .Chain("step-a", "step-b", Workflow.End)
            .RunAsync(new CounterState());

        traces.Count.ShouldBe(2);
        traces[0].captured.ShouldBe("step-a");
        traces[1].captured.ShouldBe("step-b");
    }

    [Test]
    public async Task TraceContext_SecondJobAfterFaultAndResume_SeesOwnContext()
    {
        // job-a always succeeds (checkpoint is saved). job-b fails on the first run,
        // then succeeds on resume. job-b must see its own job name in the trace on
        // both calls, proving the finally restore + re-set sequence is correct.
        var callCount = 0;
        var capturedJobs = new List<string?>();
        var store = new Checkpointing.InMemoryCheckpointStore();

        var workflow = new Workflow<CounterState>("trace-restore-fault")
            .Job("job-a", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("job-b", (s, _) =>
            {
                capturedJobs.Add(WorkflowTraceContext.Value?.CurrentJob);
                if (++callCount == 1)
                    throw new InvalidOperationException("transient");
                return Task.FromResult(s with { Value = 2 });
            })
            .Chain("job-a", "job-b", Workflow.End)
            .UseCheckpointing(store);

        var first = await workflow.RunAsync(new CounterState());
        first.Status.ShouldBe(ExecutionStatus.Faulted);
        store.Count.ShouldBe(1); // checkpoint on job-a (the last successful job)

        var resumed = await workflow.ResumeAsync(first.Id);
        resumed.Status.ShouldBe(ExecutionStatus.Completed);

        // job-b ran twice (fault + resume), both times it must have seen its own name
        capturedJobs.Count.ShouldBe(2);
        capturedJobs[0].ShouldBe("job-b");
        capturedJobs[1].ShouldBe("job-b");
    }

    // -- Token usage attribution --------------------------------------

    [Test]
    public async Task TokenUsage_AttributedPerJob_CumulativeSumIsCorrect()
    {
        // Two jobs each use distinct token amounts.
        // CumulativeUsage must equal the exact sum — proving tokens aren't
        // double-counted or leaked between accumulators.
        var modelA = new FixedUsageModel(inputTokens: 100, outputTokens: 40);
        var modelB = new FixedUsageModel(inputTokens: 200, outputTokens: 80);

        var result = await new Workflow<AgentState>("token-per-job")
            .Job("job-a", CreateAgentJob("job-a", modelA))
            .Job("job-b", CreateAgentJob("job-b", modelB))
            .Chain("job-a", "job-b", Workflow.End)
            .RunAsync(new AgentState());

        result.Status.ShouldBe(ExecutionStatus.Completed);
        result.CumulativeUsage.InputTokens.ShouldBe(300);   // 100 + 200
        result.CumulativeUsage.OutputTokens.ShouldBe(120);  // 40  + 80
    }

    [Test]
    public async Task TokenUsage_FaultingJob_PriorTokensStillAccumulated()
    {
        // job-a succeeds and reports 50 input tokens.
        // job-b faults. CumulativeUsage must still include job-a's tokens.
        var modelA = new FixedUsageModel(inputTokens: 50, outputTokens: 20);

        var result = await new Workflow<AgentState>("token-fault")
            .Job("job-a", CreateAgentJob("job-a", modelA))
            .Job("job-b", (AgentState _, CancellationToken _) =>
                throw new InvalidOperationException("boom"))
            .Chain("job-a", "job-b", Workflow.End)
            .RunAsync(new AgentState());

        result.Status.ShouldBe(ExecutionStatus.Faulted);
        result.CumulativeUsage.InputTokens.ShouldBe(50);
        result.CumulativeUsage.OutputTokens.ShouldBe(20);
    }

    [Test]
    public async Task TokenUsage_NoTokensFromFaultingJob_NotCounted()
    {
        // A plain (non-agent) job that faults contributes 0 tokens.
        // The accumulator created for it must not carry over into the next iteration.
        var modelA = new FixedUsageModel(inputTokens: 10, outputTokens: 5);
        var callCount = 0;

        var result = await new Workflow<AgentState>("token-no-leak")
            .Job("plain-fault", (AgentState s, CancellationToken _) =>
            {
                if (++callCount == 1) throw new InvalidOperationException("transient");
                return Task.FromResult(s);
            })
            .Job("agent-job", CreateAgentJob("agent-job", modelA))
            .Chain("plain-fault", "agent-job", Workflow.End)
            .UseCheckpointing(new Checkpointing.InMemoryCheckpointStore())
            .RunAsync(new AgentState());

        // First run fails at plain-fault before agent-job runs
        result.Status.ShouldBe(ExecutionStatus.Faulted);
        result.CumulativeUsage.TotalTokens.ShouldBe(0);
    }

    // -- Helpers -----------------------------------------------------

    private static IJob<AgentState> CreateAgentJob(string name, IAgentModel model) =>
        AgentJobFactory.Create<AgentState, AgentOutput>(name, model)
            .WithPrompt(_ => "test")
            .MapResult((s, _) => s)
            .Build();

    private record AgentState;

    private record AgentOutput;

    private sealed class FixedUsageModel(int inputTokens, int outputTokens) : IAgentModel
    {
        public Task<Ananke.Abstractions.Agents.AgentResponse> GenerateAsync(
            AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new Ananke.Abstractions.Agents.AgentResponse
            {
                Text = "{}",
                Usage = new TokenUsage { InputTokens = inputTokens, OutputTokens = outputTokens }
            });
    }
}
