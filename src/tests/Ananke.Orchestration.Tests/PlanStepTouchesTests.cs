using Ananke.Design;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// What a step may change, named in the plan: carried from the plan file to the node, kept by a change
/// of plan, set by a Planner, and handed to whatever applies what the step proposed.
/// </summary>
[TestFixture]
public sealed class PlanStepTouchesTests
{
    private const string Criterion = "The change is drafted.";

    private static readonly string[] Manifest =
    [
        "plan: tidy",
        "root:",
        "  goal: Tidy the module.",
        "  children:",
        "    - id: draft",
        "      goal: Draft the change.",
        "      touches:",
        "        - src/tidy.py",
        "        - docs/tidy.md",
        "      criteria:",
        $"        - {Criterion}"
    ];

    [Test]
    public void ToTree_WhatAStepTouches_IsOnItsNode_WrittenOutAndReadBack_AndKeptByAChangeOfPlan()
    {
        var manifest = PlanManifest.Parse(Manifest);

        manifest.Root.Children.ShouldHaveSingleItem().Touches.ShouldBe(["src/tidy.py", "docs/tidy.md"]);
        PlanManifest.Parse(manifest.ToYaml().Split('\n')).Root.Children.ShouldHaveSingleItem()
            .Touches.ShouldBe(["src/tidy.py", "docs/tidy.md"]);

        var tree = manifest.ToTree();
        tree.Node("draft").Touches.ShouldBe(["src/tidy.py", "docs/tidy.md"]);
        tree.Node("tidy").Touches.ShouldBeEmpty();

        tree.Rerule("draft", Contract("Draft the change again."), "the first draft missed a file")
            .Node("draft").Touches.ShouldBe(["src/tidy.py", "docs/tidy.md"]);
    }

    [Test]
    public async Task ReruleAsync_AStepThePlannerNamesWhatItTouches_TouchesThat_AndOneNamingNothingKeepsItsOwn()
    {
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(PlanManifest.Parse(Manifest).ToTree());

        var reruled = await new PlanExecutor(store).ReruleAsync(
            "tidy",
            "tidy",
            Contract("Tidy the module."),
            "the module needs a test",
            [
                new AuthoredStep { Id = "draft", Contract = Contract("Draft the change.") },
                new AuthoredStep { Id = "test", Contract = Contract("Test the change."), Touches = ["tests/test_tidy.py"] }
            ]);

        reruled.Node("draft").Touches.ShouldBe(["src/tidy.py", "docs/tidy.md"]);
        reruled.Node("test").Touches.ShouldBe(["tests/test_tidy.py"]);
        (await store.LoadAsync("tidy")).ShouldNotBeNull().Node("test").Touches.ShouldBe(["tests/test_tidy.py"]);
    }

    [Test]
    public async Task ExecuteAsync_TheApplier_IsHandedWhatTheStepTouches()
    {
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(PlanManifest.Parse(Manifest).ToTree());
        var catalog = new OperationCatalog([new OperationDefinition { Name = "edit", Arity = 1, Description = "Edit: arg1 the change." }]);
        var handed = new List<IReadOnlyList<string>>();

        await new PlanExecutor(
                store,
                operations: catalog,
                applier: (_, context, _) =>
                {
                    if (context.Node.Id == "draft")
                        handed.Add(context.Node.Touches);

                    return Task.FromResult(PlanApplication.Ok());
                })
            .ExecuteAsync("tidy", (context, _) => Task.FromResult(new NodeOutcome
            {
                Candidate = new Operation { Name = "edit", Arguments = ["one more line"] }
            }));

        handed.ShouldHaveSingleItem().ShouldBe(["src/tidy.py", "docs/tidy.md"]);
    }

    private static AgentContract Contract(string goal) => new() { Goal = goal, AcceptanceCriteria = [Criterion] };
}
