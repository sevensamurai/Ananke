using Ananke.Abstractions.Agents;
using Ananke.Design;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Tests.Fakes;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// A step that proposes by calling its operation: its call is the candidate and ends its turn, what it
/// wrote with the call is the summary, and no call follows it. A step that calls nothing answers in words.
/// </summary>
[TestFixture]
public sealed class PlanNodeProposalTests
{
    private static readonly OperationCatalog Edits = new(
        [new OperationDefinition { Name = "edit", Arity = 2, Description = "Edit a file: arg1 the path, arg2 the change." }]);

    [Test]
    public async Task RunAsync_AStepThatCalls_HasTheCallAsItsCandidate_AndWhatItWroteWithTheCallAsItsSummary()
    {
        var model = new ScriptedAgentModel(
            Calling("Adding the line to a.py.", "edit", """{"arg1":"a.py","arg2":"add one line"}"""),
            ScriptedAgentModel.Say("I proposed an edit to a.py."));

        var outcome = await Runner(model).RunAsync(Context()).WaitAsync(TimeSpan.FromSeconds(10));

        outcome.Candidate.ShouldNotBeNull().ToString().ShouldBe("edit(a.py, add one line)");
        outcome.Summary.ShouldBe("Adding the line to a.py.");
    }

    [Test]
    public async Task RunAsync_AStepThatCalls_IsAskedForNoResponseFormat_AndNoCallFollowsItsCall()
    {
        var model = new ScriptedAgentModel(
            ScriptedAgentModel.Call("c1", "edit", """{"arg1":"a.py","arg2":"add one line"}"""),
            ScriptedAgentModel.Say("I proposed an edit to a.py."));

        await Runner(model).RunAsync(Context()).WaitAsync(TimeSpan.FromSeconds(10));

        model.Requests.ShouldHaveSingleItem().ResponseFormat.ShouldBeNull();
    }

    [Test]
    public async Task RunAsync_AStepThatCallsNothing_ProposesNothing_AndSaysWhy()
    {
        var model = new ScriptedAgentModel(ScriptedAgentModel.Say("No operation fits: there is no file to edit."));

        var outcome = await Runner(model).RunAsync(Context()).WaitAsync(TimeSpan.FromSeconds(10));

        outcome.Candidate.ShouldBeNull();
        outcome.Summary.ShouldBe("No operation fits: there is no file to edit.");
    }

    [Test]
    public void Constructor_WithoutOperationsAsTools_Throws()
    {
        // The proposal is the call: with nothing to call, every step would read as proposing nothing.
        var options = new PlanNodeAgentOptions<PlanNodeProposal>
        {
            Model = new ScriptedAgentModel(ScriptedAgentModel.Say("unused")),
            Operations = Edits
        };

        Should.Throw<ArgumentException>(() => new PlanNodeAgentRunner<PlanNodeProposal>(options))
            .Message.ShouldContain(nameof(PlanNodeAgentOptions.OperationsAsTools));
    }

    [Test]
    public void Constructor_WithoutOperations_Throws()
    {
        var options = new PlanNodeAgentOptions<PlanNodeProposal>
        {
            Model = new ScriptedAgentModel(ScriptedAgentModel.Say("unused")),
            OperationsAsTools = true
        };

        Should.Throw<ArgumentException>(() => new PlanNodeAgentRunner<PlanNodeProposal>(options));
    }

    [Test]
    public void Constructor_AnAnswerThatDoesNotProposeByCalling_NeedsNoOperations()
    {
        var options = new PlanNodeAgentOptions<PlanNodeFindings> { Model = new ScriptedAgentModel(ScriptedAgentModel.Say("unused")) };

        Should.NotThrow(() => new PlanNodeAgentRunner<PlanNodeFindings>(options));
    }

    private static AgentResponse Calling(string text, string tool, string args) =>
        new() { Text = text, ToolCalls = [new AgentToolCall("c1", tool, args)] };

    private static PlanNodeAgentRunner<PlanNodeProposal> Runner(ScriptedAgentModel model) =>
        new(new PlanNodeAgentOptions<PlanNodeProposal> { Model = model, Operations = Edits, OperationsAsTools = true });

    private static PlanNodeContext Context()
    {
        var contract = new AgentContract { Goal = "Add one line to a.py", AcceptanceCriteria = ["The line is there."] };

        return new PlanNodeContext
        {
            PlanId = "plan",
            PlanVersion = 1,
            Node = new PlanNode { Id = "add", Contract = contract },
            Contract = contract,
            TreeView = "[add] Add one line to a.py",
            Projection = new PlanTreeProjection.Projection { Text = "[add] Add one line to a.py", EstimatedTokens = 8 }
        };
    }
}
