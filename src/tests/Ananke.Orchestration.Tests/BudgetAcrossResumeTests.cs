using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Simulation;
using Ananke.Orchestration.Budget;
using Ananke.Orchestration.Checkpointing;
using Ananke.Orchestration.Workflows;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>A budget over a run that pauses for a person and is resumed.</summary>
[TestFixture]
public class BudgetAcrossResumeTests
{
    private sealed record State
    {
        public string Text { get; init; } = "";
    }

    private sealed record Answer
    {
        public string? Text { get; init; }
    }

    [Test]
    public async Task ResumeAsync_SpendBeforeAndAfterThePause_CountsAgainstOneBudget()
    {
        // Each job's call costs 1.00 at $1 per million input tokens: 2.00 over a 1.50 ceiling.
        var workflow = Paused(new InMemoryCheckpointStore());

        var paused = await workflow.RunAsync(new State()).WaitAsync(TimeSpan.FromSeconds(10));
        paused.Status.ShouldBe(ExecutionStatus.Interrupted);

        var resumed = await workflow.ResumeAsync(paused.Id).WaitAsync(TimeSpan.FromSeconds(10));

        resumed.Status.ShouldBe(ExecutionStatus.BudgetExceeded);
        resumed.EstimatedCost.ShouldBe(2.00m);
    }

    [Test]
    public async Task ResumeAsync_AfterAProcessRestart_StillCountsTheSpendBeforeThePause()
    {
        // The paused run and the resumed one share nothing but the checkpoint directory.
        var root = Path.Combine(Path.GetTempPath(), "ananke-budget-resume", Guid.NewGuid().ToString("N"));

        try
        {
            var paused = await Paused(new FileCheckpointStore(root))
                .RunAsync(new State()).WaitAsync(TimeSpan.FromSeconds(10));
            paused.Status.ShouldBe(ExecutionStatus.Interrupted);

            var resumed = await Paused(new FileCheckpointStore(root))
                .ResumeAsync(paused.Id).WaitAsync(TimeSpan.FromSeconds(10));

            resumed.Status.ShouldBe(ExecutionStatus.BudgetExceeded);
            resumed.EstimatedCost.ShouldBe(2.00m);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static Workflow<State> Paused(ICheckpointStore store)
    {
        var model = SimulatedAgentModel.Json(new Answer { Text = "done" },
            new SimulatedModelOptions { InputTokens = 1_000_000 });

        return new Workflow<State>("paused")
            .Job("first", Ask("first", model))
            .Job("second", Ask("second", model))
            .Then("first", "second")
            .Then("second", Workflow.End)
            .InterruptBefore("second")
            .UseCheckpointing(store)
            .WithBudget(BudgetConfig.FromPerMillion(maxCost: 1.50m, inputPerMillion: 1m, outputPerMillion: 0m));
    }

    private static AgentJob<State, Answer> Ask(string name, SimulatedAgentModel model) =>
        AgentJobFactory.Create<State, Answer>(name, model)
            .WithPrompt(_ => "answer")
            .MapResult((s, r) => s with { Text = r.Text ?? "" })
            .Build();
}
