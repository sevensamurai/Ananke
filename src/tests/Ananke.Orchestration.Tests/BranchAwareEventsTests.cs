using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// work inside a fork branch was invisible on the event stream —
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

    private static async Task<List<WorkflowEvent>> CollectAsync(
        Workflow<CounterState> workflow)
    {
        var events = new List<WorkflowEvent>();
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

    /// <summary>
    /// Every branch job's events arrive, however wide and deep the fork. The tests above
    /// use single-job branches, which never exercise concurrent writers on the stream
    /// channel; this one does. It does not pin the channel's SingleWriter declaration —
    /// BoundedChannel locks on write either way, so that stays a contract question the
    /// runtime happens not to punish today.
    /// </summary>
    [Test]
    [Repeat(20)]
    public async Task StreamAsync_WithConcurrentForkBranches_LosesNoEvents()
    {
        const int branches = 6;
        const int depth = 8;

        var workflow = new Workflow<CounterState>("wide-fork")
            .Job("start", (s, _) => Task.FromResult(s));

        for (var b = 0; b < branches; b++)
        {
            for (var j = 0; j < depth; j++)
            {
                workflow = workflow.Job($"b{b}-j{j}", async (s, ct) =>
                {
                    // Yield so the branches genuinely interleave rather than each one
                    // running to completion before the next is scheduled.
                    await Task.Yield();
                    return s;
                });
            }
        }

        // The jobs above are registered in a loop, so the analyzer cannot see them and
        // reports every name this section wires as undefined. Build() still validates them
        // at runtime — a genuinely missing job would fail the test, not slip through.
#pragma warning disable ANANKE001
        workflow = workflow
            .Job("merge", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork([.. Enumerable.Range(0, branches).Select(b => $"b{b}-j0")]))
            .Join([.. Enumerable.Range(0, branches).Select(b => $"b{b}-j{depth - 1}")], "merge", states => states[0])
            .Then("merge", Workflow.End);

        for (var b = 0; b < branches; b++)
            for (var j = 0; j < depth - 1; j++)
                workflow = workflow.Then($"b{b}-j{j}", $"b{b}-j{j + 1}");
#pragma warning restore ANANKE001

        var events = await CollectAsync(workflow);

        var started = events.OfType<JobStarted<CounterState>>()
            .Where(e => e.Branch is not null)
            .Select(e => e.JobName)
            .ToList();
        var completed = events.OfType<JobCompleted<CounterState>>()
            .Where(e => e.Branch is not null)
            .Select(e => e.JobName)
            .ToList();

        var expected = Enumerable.Range(0, branches)
            .SelectMany(b => Enumerable.Range(0, depth).Select(j => $"b{b}-j{j}"))
            .ToList();

        started.Order().ShouldBe(expected.Order());
        completed.Order().ShouldBe(expected.Order());
    }
}
