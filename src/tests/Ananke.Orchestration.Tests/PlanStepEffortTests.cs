using System.Text.Json;
using System.Text.Json.Nodes;
using Ananke.Design;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Tests.Fakes;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// A step that names how hard it reasons, from the plan file to the fields its requests carry.
/// </summary>
#pragma warning disable ANNKEEXP003 // a request's own fields are experimental
[TestFixture]
public sealed class PlanStepEffortTests
{
    private const string Criterion = "The change is drafted.";

    [Test]
    public async Task RunAsync_AStepNamingAnEffortInThePlanFile_MakesEachRequestWithTheFieldsItIsWrittenAs()
    {
        var manifest = PlanManifest.Parse(
        [
            "plan: tidy",
            "root:",
            "  goal: Tidy the module.",
            "  children:",
            "    - id: draft",
            "      goal: Draft the change.",
            "      effort: high",
            "      criteria:",
            $"        - {Criterion}"
        ]);

        // Carried: written out and read back, and through a change of plan that re-rules the step.
        PlanManifest.Parse(manifest.ToYaml().Split('\n')).Root.Children.ShouldHaveSingleItem().Effort.ShouldBe("high");
        var tree = manifest.ToTree().Rerule("draft", Contract("Draft the change again."), "the first draft missed a file");
        var node = tree.Current.Nodes["draft"];
        node.Effort.ShouldBe("high");

        var model = new ScriptedAgentModel(ScriptedAgentModel.Say(Report()));
        var runner = new PlanNodeAgentRunner(new PlanNodeAgentOptions
        {
            Model = model,
            EffortFields = effort => new Dictionary<string, JsonNode> { ["reasoning"] = new JsonObject { ["effort"] = effort } }
        });

        await runner.RunAsync(ContextFor(node)).WaitAsync(TimeSpan.FromSeconds(10));

        model.Requests.ShouldNotBeEmpty();
        foreach (var request in model.Requests)
            request.RequestFields.ShouldNotBeNull()["reasoning"]!["effort"]!.GetValue<string>().ShouldBe("high");
    }

    [Test]
    public async Task RunAsync_AStepNamingAnEffort_WithNoWayToWriteIt_IsRefused_AndNoCallIsMade()
    {
        var model = new ScriptedAgentModel(ScriptedAgentModel.Say(Report()));
        var runner = new PlanNodeAgentRunner(new PlanNodeAgentOptions { Model = model });
        var node = new PlanNode { Id = "draft", Contract = Contract("Draft the change."), Effort = "high" };

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => runner.RunAsync(ContextFor(node)).WaitAsync(TimeSpan.FromSeconds(10)));

        thrown.Message.ShouldContain("'high'");
        model.Requests.ShouldBeEmpty();
    }

    private static PlanNodeContext ContextFor(PlanNode node) => new()
    {
        PlanId = "tidy",
        PlanVersion = 1,
        Node = node,
        Contract = node.Contract,
        TreeView = $"[{node.Id}] {node.Contract.Goal}",
        Projection = new PlanTreeProjection.Projection { Text = $"[{node.Id}] {node.Contract.Goal}", EstimatedTokens = 8 }
    };

    private static AgentContract Contract(string goal) => new() { Goal = goal, AcceptanceCriteria = [Criterion] };

    private static string Report() => JsonSerializer.Serialize(new PlanNodeReport { Summary = "drafted it", Done = true });
}
#pragma warning restore ANNKEEXP003
