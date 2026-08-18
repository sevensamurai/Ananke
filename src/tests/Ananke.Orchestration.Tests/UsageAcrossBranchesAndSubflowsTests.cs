using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Gaps 1 and 2 of ADR-arch-028: token usage produced inside a fork branch, or inside a
/// sub-workflow, must reach the execution that owns the budget.
/// <para>
/// Before Part B neither did. <c>TokenUsageCapture</c> held a mutable accumulator through an
/// ambient reference the runner reassigned per job: a fork branch never assigned one, so
/// <c>Accumulate</c> hit its null guard and <b>discarded the tokens</b>; a sub-workflow's runner
/// assigned its own, so child spend never reached the parent.
/// </para>
/// </summary>
[TestFixture]
public class UsageAcrossBranchesAndSubflowsTests
{
    [Test]
    public async Task Fork_TokensSpentInsideBranches_ReachCumulativeUsage()
    {
        var modelA = new FixedUsageModel(inputTokens: 100, outputTokens: 40);
        var modelB = new FixedUsageModel(inputTokens: 200, outputTokens: 60);

        var execution = await new Workflow<UsageState>("fork-usage")
            .Job("start", (s, _) => Task.FromResult(s))
            .Job("branch-a", AgentJob("branch-a", modelA))
            .Job("branch-b", AgentJob("branch-b", modelB))
            .Job("merge", (s, _) => Task.FromResult(s))
            .Then("start", Workflow.Fork("branch-a", "branch-b"))
            .Join(["branch-a", "branch-b"], "merge", states => states[0])
            .Then("merge", Workflow.End)
            .RunAsync(new UsageState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.CumulativeUsage.InputTokens.ShouldBe(300,
            "both branches' tokens must be counted — they used to be discarded outright");
        execution.CumulativeUsage.OutputTokens.ShouldBe(100);
    }

    [Test]
    public async Task SubFlow_TokensSpentInTheChild_ReachTheParentsTotal()
    {
        var childModel = new FixedUsageModel(inputTokens: 70, outputTokens: 30);
        var parentModel = new FixedUsageModel(inputTokens: 10, outputTokens: 5);

        var inner = new Workflow<UsageState>("child")
            .Job("child-work", AgentJob("child-work", childModel))
            .Then("child-work", Workflow.End);

        var execution = await new Workflow<UsageState>("parent-usage")
            .Job("parent-work", AgentJob("parent-work", parentModel))
            .SubFlow("child-flow", inner, parent => parent, (parent, _) => parent)
            .Chain("parent-work", "child-flow", Workflow.End)
            .RunAsync(new UsageState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        execution.CumulativeUsage.InputTokens.ShouldBe(80,
            "a sub-workflow's spend must reach the parent — a parent budget that cannot see " +
            "its children does not bound anything");
        execution.CumulativeUsage.OutputTokens.ShouldBe(35);
    }

    [Test]
    public async Task SubFlow_ChildReportsOnlyItsOwnSpend_NotTheParents()
    {
        // The child inherits the parent's recorder, so without a per-execution baseline it
        // would report the parent's tokens as its own.
        UsageState? observed = null;
        var childModel = new FixedUsageModel(inputTokens: 70, outputTokens: 30);
        var parentModel = new FixedUsageModel(inputTokens: 10, outputTokens: 5);

        var inner = new Workflow<UsageState>("child")
            .Job("child-work", AgentJob("child-work", childModel))
            .Then("child-work", Workflow.End);

        var execution = await new Workflow<UsageState>("parent-baseline")
            .Job("parent-work", AgentJob("parent-work", parentModel))
            .SubFlow("child-flow", inner,
                parent => parent,
                (parent, child) => { observed = child; return parent; })
            .Chain("parent-work", "child-flow", Workflow.End)
            .RunAsync(new UsageState());

        execution.Status.ShouldBe(ExecutionStatus.Completed);
        observed.ShouldNotBeNull();
        execution.CumulativeUsage.InputTokens.ShouldBe(80);
    }

    // -- Helpers -----------------------------------------------------

    private static Jobs.IJob<UsageState> AgentJob(string name, IAgentModel model) =>
        AgentJobFactory.Create<UsageState, AgentOutput>(name, model)
            .WithPrompt(_ => "test")
            .MapResult((s, _) => s)
            .Build();

    public record UsageState;

    private record AgentOutput;

    private sealed class FixedUsageModel(int inputTokens, int outputTokens) : IAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AgentResponse
            {
                Text = "{}",
                Usage = new TokenUsage { InputTokens = inputTokens, OutputTokens = outputTokens }
            });
    }
}
