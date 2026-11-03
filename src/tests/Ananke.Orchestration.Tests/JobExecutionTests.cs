using Ananke.Orchestration.Execution;
using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Workflows;
using Ananke.TestHelpers;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class JobExecutionTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task History_LinearRun_IsStampedByTheRunnersClock()
    {
        var runner = new WorkflowRunner(timeProvider: new FakeTimeProvider(Now));

        var execution = await new Workflow<CounterState>("clocked")
            .Job("a", (s, _) => Task.FromResult(s with { Value = 1 }))
            .Job("b", (s, _) => Task.FromResult(s with { Value = 2 }))
            .Chain("a", "b", Workflow.End)
            .UseRunner(runner)
            .RunAsync(new CounterState())
            .WaitAsync(TimeSpan.FromSeconds(10));

        execution.History.Count.ShouldBe(2);
        ShouldBeStampedAtNow(execution.History);
    }

    [Test]
    public async Task History_ForkBranches_AreStampedByTheRunnersClock()
    {
        var runner = new WorkflowRunner(timeProvider: new FakeTimeProvider(Now));

        var execution = await new Workflow<CounterState>("clocked-fork")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("left", (s, _) => Task.FromResult(s with { Value = s.Value + 1 }))
            .Job("right", (s, _) => Task.FromResult(s with { Value = s.Value + 10 }))
            .Job("merge", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork("left", "right"))
            .Join(["left", "right"], "merge",
                states => new CounterState { Value = states.Sum(s => s.Value) })
            .Then("merge", Workflow.End)
            .UseRunner(runner)
            .RunAsync(new CounterState())
            .WaitAsync(TimeSpan.FromSeconds(10));

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.History.Select(h => h.JobName).ShouldContain("left");
        execution.History.Select(h => h.JobName).ShouldContain("right");
        ShouldBeStampedAtNow(execution.History);
    }

    private static void ShouldBeStampedAtNow(IReadOnlyList<JobExecution> history)
    {
        foreach (var job in history)
        {
            job.CompletedAt.ShouldBe(Now, job.JobName);
            job.StartedAt.ShouldBe(job.CompletedAt - job.Duration, job.JobName);
        }
    }
}
