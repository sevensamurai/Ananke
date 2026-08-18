using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Gap 4 of ADR-arch-028: work inside a fork branch was invisible on the event stream —
/// the branch path emitted log lines only, because ExecuteForkJoinAsync held the writer
/// and never passed it down. Consumers saw ForkStarted, then nothing until JoinCompleted.
/// </summary>
[TestFixture]
public class BranchAwareEventsTests
{
    private static Workflow<CounterState> ForkWorkflow() =>
        new Workflow<CounterState>("branch-events")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("branch-a", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "a"] }))
            .Job("branch-b", (s, _) => Task.FromResult(s with { Trail = [.. s.Trail, "b"] }))
            .Job("merge", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork("branch-a", "branch-b"))
            .Join(["branch-a", "branch-b"], "merge", states => states[0])
            .Then("merge", Workflow.End);

    private static async Task<List<WorkflowEvent<CounterState>>> CollectAsync(
        Workflow<CounterState> workflow)
    {
        var events = new List<WorkflowEvent<CounterState>>();
        await foreach (var evt in workflow.StreamAsync(new CounterState()))
            events.Add(evt);
        return events;
    }

    [Test]
    public async Task BranchJobs_EmitStartedAndCompleted()
    {
        var events = await CollectAsync(ForkWorkflow());

        var branchStarts = events.OfType<JobStarted<CounterState>>()
            .Where(e => e.Branch is not null)
            .Select(e => e.JobName)
            .ToList();

        branchStarts.ShouldContain("branch-a");
        branchStarts.ShouldContain("branch-b");

        var branchCompletions = events.OfType<JobCompleted<CounterState>>()
            .Where(e => e.Branch is not null)
            .Select(e => e.JobName)
            .ToList();

        branchCompletions.ShouldContain("branch-a");
        branchCompletions.ShouldContain("branch-b");
    }

    [Test]
    public async Task BranchEvents_CarryTheBranchStartJobAsDiscriminator()
    {
        var events = await CollectAsync(ForkWorkflow());

        foreach (var evt in events.OfType<JobStarted<CounterState>>().Where(e => e.Branch is not null))
            evt.Branch.ShouldBe(evt.JobName,
                "each branch here is a single job, so the discriminator is that job's own name");
    }

    [Test]
    public async Task MainPathEvents_HaveNoBranch()
    {
        var events = await CollectAsync(ForkWorkflow());

        var mainPathJobs = events.OfType<JobStarted<CounterState>>()
            .Where(e => e.Branch is null)
            .Select(e => e.JobName)
            .ToList();

        mainPathJobs.ShouldContain("start");
        mainPathJobs.ShouldContain("merge");
        mainPathJobs.ShouldNotContain("branch-a");
        mainPathJobs.ShouldNotContain("branch-b");
    }

    [Test]
    public async Task BranchJobs_EmitNoStateUpdated()
    {
        // A branch's state is its own until the join merges it. Emitting StateUpdated per
        // branch job would tell a consumer the workflow state changed when it has not.
        var events = await CollectAsync(ForkWorkflow());

        events.OfType<StateUpdated<CounterState>>()
            .Where(e => e.Branch is not null)
            .ShouldBeEmpty();
    }
}
