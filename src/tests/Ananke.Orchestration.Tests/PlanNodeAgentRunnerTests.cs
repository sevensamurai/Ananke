using System.Text.Json;
using System.Text.Json.Nodes;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Simulation;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// The seam between a plan node and an agent job.
/// </summary>
/// <remarks>
/// <para>
/// Both sides of this join were already built and tested — a contract is pinned by construction
/// inside a job, and nothing crosses a node boundary except through the tree. What had never been
/// pinned is that either property survives the join, because until this runner existed there was no
/// join to test: every consumer wrote it by hand.
/// </para>
/// <para>
/// A node's own report no longer becomes a verdict, a dispute or a question (R26) — <c>ToOutcome</c>
/// and the guards that arbitrated a self-report are gone with it. What is left worth testing here is
/// narrower: that the seam still pins the contract correctly, gives each node a fresh job, and carries
/// a node's <c>Summary</c> to the event stream untouched.
/// </para>
/// </remarks>
[TestFixture]
public class PlanNodeAgentRunnerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 24, 9, 0, 0, TimeSpan.Zero);

    private const string Criterion = "the parser round-trips";

    // ── Pinning across the seam ──

    [Test]
    public async Task RunAsync_Always_PinsTheNodesContractIntoTheRequest()
    {
        var model = Model(Report());

        await Runner(model).RunAsync(Context());

        var systemPrompt = model.Requests[^1].SystemPrompt;
        systemPrompt.ShouldNotBeNull();
        systemPrompt.ShouldContain("Parse the input");
        systemPrompt.ShouldContain(Criterion);
    }

    [Test]
    public async Task RunAsync_WhenConfigureSetsItsOwnContract_TheNodesContractStillWins()
    {
        var model = Model(Report());
        var options = Options(model) with
        {
            Configure = b => b.WithContract(new AgentContract { Goal = "Something else entirely" })
        };

        await new PlanNodeAgentRunner(options).RunAsync(Context());

        // A caller that could overwrite the contract could unpin it, and R2 would hold only for
        // callers who remembered.
        model.Requests[^1].SystemPrompt!.ShouldContain("Parse the input");
        model.Requests[^1].SystemPrompt!.ShouldNotContain("Something else entirely");
    }

    [Test]
    public async Task RunAsync_WithAPersona_KeepsItSeparateFromTheContract()
    {
        var model = Model(Report());

        await new PlanNodeAgentRunner(Options(model) with { Persona = "You are terse." })
            .RunAsync(Context());

        var systemPrompt = model.Requests[^1].SystemPrompt!;
        systemPrompt.ShouldContain("You are terse.");
        systemPrompt.IndexOf("You are terse.", StringComparison.Ordinal)
            .ShouldBeLessThan(systemPrompt.IndexOf("# Contract", StringComparison.Ordinal));
    }

    [Test]
    public async Task RunAsync_TheTreeView_GoesOnTheUserTurnAndNotIntoThePinnedSection()
    {
        var model = Model(Report());
        var context = Context() with { TreeView = "[parse] Parse the input — no verdict yet" };

        await Runner(model).RunAsync(context);

        // P6, pinned by a test rather than by a comment: the projection is a reading of a plan
        // version, already budgeted and already trimmed. Pinning it would pin something that is
        // true only of the moment the node started.
        model.Requests[^1].SystemPrompt!.ShouldNotContain("no verdict yet");
        model.Requests[^1].Messages[0].Content!.ShouldContain("no verdict yet");
    }

    [Test]
    public async Task RunAsync_TwoNodesInARow_DoNotShareAContract()
    {
        var model = Model(Report(), Report());
        var runner = Runner(model);

        await runner.RunAsync(Context());
        await runner.RunAsync(Context() with
        {
            Contract = new AgentContract { Goal = "Render the output" },
            Node = new PlanNode { Id = "render", Contract = new AgentContract { Goal = "Render the output" } }
        });

        model.Requests[^1].SystemPrompt!.ShouldContain("Render the output");
        model.Requests[^1].SystemPrompt!.ShouldNotContain("Parse the input");
    }

    // ── What a node's report carries, and what it cannot ──

    [Test]
    public async Task RunAsync_ANodesOwnReport_NeverBecomesAVerdict()
    {
        // R26: a step says what it did; nothing derives from it. Before this runner stopped asking a
        // model to grade its own work, a report like this one was exactly how a node talked its way
        // to Satisfied.
        var outcome = await Runner(Model(Report())).RunAsync(Context());

        outcome.Verdicts.ShouldBeEmpty();
        outcome.Summary.ShouldBe("parsed it");
    }

    [Test]
    public async Task RunAsync_AStepThatCouldNotDoItsTask_CarriesThatAndItsOptions()
    {
        var reply = JsonSerializer.Serialize(new PlanNodeReport
        {
            Summary = "no stay in hakone is free for 2 nights",
            Done = false,
            Options = ["onsen-ryokan: 1 night, Wed 7 Apr"]
        });

        var outcome = await Runner(Model(reply)).RunAsync(Context());

        outcome.Done.ShouldBeFalse();
        outcome.Options.ShouldBe(["onsen-ryokan: 1 night, Wed 7 Apr"]);
        outcome.Candidate.ShouldBeNull();
    }

    [Test]
    public async Task RunAsync_AStepThatReportsFindings_CarriesThemOnItsOutcome()
    {
        var reply = JsonSerializer.Serialize(new PlanNodeReport
        {
            Summary = "split keeps the semicolon",
            Done = true,
            Findings =
            [
                new NodeFinding { Claim = "split returns each statement stripped", Source = "sqlparse/__init__.py:70" },
                new NodeFinding { Claim = "the splitter appends the ';' token", Source = "sqlparse/engine/statement_splitter.py:97" }
            ]
        });

        var outcome = await Runner(Model(reply)).RunAsync(Context());

        outcome.Findings.Select(f => (f.Claim, f.Source)).ShouldBe(
        [
            ("split returns each statement stripped", "sqlparse/__init__.py:70"),
            ("the splitter appends the ';' token", "sqlparse/engine/statement_splitter.py:97")
        ]);
    }

    [Test]
    public async Task RunAsync_AStepThatReportsNoFindings_CarriesNone()
    {
        var outcome = await Runner(Model(Report())).RunAsync(Context());

        outcome.Findings.ShouldBeEmpty();
    }

    [Test]
    public async Task RunAsync_AStepThatDidItsTask_ReportsDoneAndNoOptions()
    {
        var outcome = await Runner(Model(Report())).RunAsync(Context());

        outcome.Done.ShouldBeTrue();
        outcome.Options.ShouldBeEmpty();
    }

    [Test]
    public void BuildPrompt_AsksWhetherTheTaskCouldBeDone_AndForOptionsWhenItCouldNot()
    {
        var catalog = new OperationCatalog(
            [new OperationDefinition { Name = "parse", Arity = 0, Description = "parse the input" }]);

        var prompt = new PlanNodeAgentRunner(Options(Model(Report())) with { Operations = catalog })
            .BuildPrompt(Context());

        prompt.ShouldContain("If you could not do it");
        prompt.ShouldContain("\"Options\"");
    }

    [Test]
    public void BuildPrompt_AfterAFindingWithNoSource_ShowsTheFindingsThatWereRefused()
    {
        var rejection = new NodeRejection(null, "1 finding names no source.")
        {
            Findings = [new NodeFinding { Claim = "the input is CSV" }]
        };

        var prompt = new PlanNodeAgentRunner(Options(Model(Report())))
            .BuildPrompt(Context() with { Rejection = rejection });

        prompt.ShouldContain("the input is CSV");
        prompt.ShouldContain("1 finding names no source.");
        prompt.ShouldNotContain("You proposed");
    }

    // A proposing step's contract cannot be met while it proposes — nothing has applied the change
    // or checked it — so read the other way it reports not-done and PlanExecutor drops the candidate
    // it just made. Four live runs went by before that was found, and the only thing standing
    // between it and a return is this wording.

    [Test]
    public void BuildPrompt_WithOperations_TellsAProposingStepThatProposingIsDoingItsPart()
    {
        var catalog = new OperationCatalog(
            [new OperationDefinition { Name = "parse", Arity = 0, Description = "parse the input" }]);

        var prompt = new PlanNodeAgentRunner(Options(Model(Report())) with { Operations = catalog })
            .BuildPrompt(Context());

        prompt.ShouldContain("Proposing a change is doing your part");
        prompt.ShouldContain("When you propose a change, leave \"Options\" empty");
    }

    [Test]
    public void BuildPrompt_WithNoOperations_SaysNeitherOfThose()
    {
        var prompt = Runner(Model(Report())).BuildPrompt(Context());

        // "If nothing comes close, leave \"Options\" empty" is the shared paragraph's, and every
        // domain gets it. What must not reach a step with no catalog is the proposing half.
        prompt.ShouldNotContain("doing your part");
        prompt.ShouldNotContain("When you propose a change");
    }

    [Test]
    public void BuildPrompt_WithNoOperations_AsksWhatWasFoundAndNeverForAChange()
    {
        var prompt = Runner(Model(Report())).BuildPrompt(Context());

        prompt.ShouldContain("\"Done\"");
        prompt.ShouldContain("\"Options\"");
        prompt.ShouldNotContain("Propose");
        prompt.ShouldNotContain("propose");
    }

    [Test]
    public async Task ARunNodesSummary_ReachesTheEventStream()
    {
        var sink = new CollectingSink();
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(PlanTree.Create(
            "plan", Contract("Ship it"), [("parse", Contract("Parse the input", Criterion))]));

        using (WorkflowEventReporting.BeginScope(sink))
            await new PlanExecutor(store).ExecuteAsync("plan", Runner(Model(Report())).AsRunner());

        // The summary is the one part of a report the tree deliberately has no room for, so if it
        // is not reported it reaches nobody. Reported by the executor from the outcome, which is why
        // a runner that is a program or a person gets the same line as one that is an agent.
        //
        // A report is narration only now (R26), so "parse" never settles from its own say-so and the
        // root — which declares no criteria of its own — runs too and reports as well; this asserts
        // on "parse" specifically rather than assuming it is the only node that ran.
        var reported = sink.Events.OfType<PlanNodeReported>().Single(e => e.NodeId == "parse");
        reported.Summary.ShouldBe("parsed it");
        reported.PlanId.ShouldBe("plan");
    }

    [Test]
    public async Task ANodeThatIsNotAnAgent_ReportsItsSummaryTheSameWay()
    {
        // The gap this closes: reporting used to live inside the agent runner, so a plan whose steps
        // are programs, commands or people ran silently — every narration of it read as a plan that
        // had done nothing.
        var sink = new CollectingSink();
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(PlanTree.Create(
            "plan", Contract("Ship it"), [("parse", Contract("Parse the input", Criterion))]));

        using (WorkflowEventReporting.BeginScope(sink))
        {
            await new PlanExecutor(store).ExecuteAsync(
                "plan",
                (_, _) => Task.FromResult(new NodeOutcome { Summary = "ran a script" }));
        }

        // Two, and both are right. This runner summarises whatever it is handed and satisfies
        // nothing, so `parse` does not settle and the root runs too — and a root is a node with a
        // contract like any other. What the test is about is the runner's *shape*: a step that is a
        // program gets the line an agent would have got, which is what reporting from the executor
        // rather than from inside the agent runner buys.
        var reported = sink.Events.OfType<PlanNodeReported>().ToList();

        reported.Select(e => e.NodeId).ShouldBe(["parse", "plan"]);
        reported.ShouldAllBe(e => e.Summary == "ran a script");
    }

    [Test]
    public async Task ANodeThatOnlyFoundSomething_IsReportedWithWhatItFound()
    {
        // A step that researches may have nothing to narrate and still have found something. Reported
        // only on a summary, it would run silently.
        var sink = new CollectingSink();
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(PlanTree.Create(
            "plan", Contract("Ship it"), [("parse", Contract("Parse the input", Criterion))]));

        using (WorkflowEventReporting.BeginScope(sink))
        {
            await new PlanExecutor(store).ExecuteAsync(
                "plan",
                (_, _) => Task.FromResult(new NodeOutcome
                {
                    Findings = [new NodeFinding { Claim = "the input is CSV", Source = "input.csv:1" }]
                }));
        }

        var reported = sink.Events.OfType<PlanNodeReported>().First(e => e.NodeId == "parse");

        reported.Findings.Single().Source.ShouldBe("input.csv:1");
        reported.Summary.ShouldBeEmpty();
    }

    // ── Proposing by calling ──

    private static readonly OperationCatalog Edits = new(
        [new OperationDefinition { Name = "edit", Arity = 2, Description = "Edit a file: arg1 the path, arg2 the change." }]);

    private static string ReportNaming(Operation? operation) =>
        JsonSerializer.Serialize(new PlanNodeReport { Summary = "proposed it", Operation = operation, Done = true });

    [Test]
    public async Task RunAsync_ProposingByCalling_TheCallIsTheCandidate_WhenTheReportNamesNone()
    {
        var model = new CallsAnOperation("edit", """{"arg1":"a.py","arg2":"add one line"}""", ReportNaming(null));
        var runner = new PlanNodeAgentRunner(new PlanNodeAgentOptions
        {
            Model = model,
            Operations = Edits,
            OperationsAsTools = true
        });

        var outcome = await runner.RunAsync(Context());

        outcome.Candidate.ShouldNotBeNull().ToString().ShouldBe("edit(a.py, add one line)");
    }

    [Test]
    public async Task RunAsync_ProposingByCalling_TheCallIsTheCandidate_AndNoReportIsAskedFor()
    {
        // Asked for a report after its call, a step writes the operation out again, and the copy is not
        // the call: a diff typed a second time loses its indentation and its markers.
        var reported = new Operation { Name = "edit", Arguments = ["b.py", "the change as reported"] };
        var model = new CallsAnOperation("edit", """{"arg1":"a.py","arg2":"add one line"}""", ReportNaming(reported));
        var runner = new PlanNodeAgentRunner(new PlanNodeAgentOptions
        {
            Model = model,
            Operations = Edits,
            OperationsAsTools = true
        });

        var outcome = await runner.RunAsync(Context());

        outcome.Candidate.ShouldNotBeNull().ToString().ShouldBe("edit(a.py, add one line)");
        outcome.Done.ShouldBeTrue();
        model.ReportAsked.ShouldBeFalse();
        model.Requests.Count.ShouldBe(1);
    }

    [Test]
    public async Task RunAsync_ProposingByCalling_TheSummaryIsWhatTheStepWroteWithItsCall()
    {
        var model = new CallsAnOperation(
            "edit", """{"arg1":"a.py","arg2":"add one line"}""", ReportNaming(null), said: "Adding the line a.py lacks.");
        var runner = new PlanNodeAgentRunner(new PlanNodeAgentOptions
        {
            Model = model,
            Operations = Edits,
            OperationsAsTools = true
        });

        var outcome = await runner.RunAsync(Context());

        outcome.Summary.ShouldBe("Adding the line a.py lacks.");
    }

    [Test]
    public async Task RunAsync_WithoutProposingByCalling_OffersNoOperationAsATool()
    {
        var model = new CallsAnOperation("edit", "{}", ReportNaming(null));
        var runner = new PlanNodeAgentRunner(new PlanNodeAgentOptions { Model = model, Operations = Edits });

        var outcome = await runner.RunAsync(Context());

        model.Requests.ShouldAllBe(r => r.Tools == null || r.Tools.All(t => t.Name != "edit"));
        outcome.Candidate.ShouldBeNull();
    }

    [Test]
    public async Task RunAsync_ProposingByCalling_KeepsTheStepsOwnTools()
    {
        var model = new CallsAnOperation("edit", """{"arg1":"a.py","arg2":"x"}""", ReportNaming(null));
        var own = new ToolKit("own").AddTool("look", "Look at something.", () => ToolResult.Ok("seen"));
        var runner = new PlanNodeAgentRunner(new PlanNodeAgentOptions
        {
            Model = model,
            Operations = Edits,
            OperationsAsTools = true,
            Configure = job => job.WithTools(own)
        });

        await runner.RunAsync(Context());

        model.Requests.First().Tools.ShouldNotBeNull().Select(t => t.Name).ShouldBe(["look", "edit"], ignoreOrder: true);
        own.Tools.Keys.ShouldBe(["look"]);
    }

    [Test]
    public async Task Execute_ProposingByCalling_TheCallReachesTheApplierOnlyAfterTheGate()
    {
        var model = new CallsAnOperation("edit", """{"arg1":"a.py","arg2":"add one line"}""", ReportNaming(null));
        var runner = new PlanNodeAgentRunner(new PlanNodeAgentOptions
        {
            Model = model,
            Operations = Edits,
            OperationsAsTools = true
        });
        var store = new InMemoryPlanTreeStore();
        await store.SaveAsync(PlanTree.Create(
            "plan", Contract("Ship it"), [("parse", Contract("Parse the input", Criterion))]));
        var applied = new List<(string NodeId, Operation Operation, bool StepReturned)>();
        var returned = false;

        await new PlanExecutor(
                store,
                operations: Edits,
                applier: (operation, context, _) =>
                {
                    applied.Add((context.Node.Id, operation, returned));
                    return Task.FromResult(PlanApplication.Ok());
                })
            .ExecuteAsync("plan", async (context, ct) =>
            {
                returned = false;
                var outcome = await runner.RunAsync(context, ct);
                returned = true;
                return outcome;
            });

        // The runner satisfies nothing, so the root runs and proposes too; each is applied once, and
        // only after the step's runner returned — never from inside its tool rounds.
        var parse = applied.Where(a => a.NodeId == "parse").ShouldHaveSingleItem();
        parse.Operation.Arguments.ShouldBe(["a.py", "add one line"]);
        applied.ShouldAllBe(a => a.StepReturned);
    }

    [Test]
    public async Task RunAsync_WithNobodyListening_StillRuns()
    {
        // No scope, no sink. A node that could only run inside a stream would be unusable under
        // RunAsync, which is most of how a plan gets run.
        var outcome = await Runner(Model(Report())).RunAsync(Context());

        outcome.Summary.ShouldBe("parsed it");
    }

    // ── The Done contract, enforced by deserialization ──

    [Test]
    public void PlanNodeReport_WithoutDone_FailsToDeserialize()
    {
        // A prompt-only provider that omits a field entirely, rather than sending something
        // invalid for it, must fail loudly here — not be read as Done: true by a lenient default.
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<PlanNodeReport>(
                """{"summary":"could not reach the API"}""",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));
    }

    // ── A step that only reports what it found ──

    [Test]
    public async Task RunAsync_AFindingsAnswer_IsAskedForNoOperationAndNoOptions_AndCarriesWhatItFound()
    {
        var model = Model(JsonSerializer.Serialize(new PlanNodeFindings
        {
            Summary = "split keeps the semicolon",
            Findings = [new NodeFinding { Claim = "split returns each statement stripped", Source = "sqlparse/__init__.py:70" }]
        }));

        var outcome = await FindingsRunner(model).RunAsync(Context());

        var asked = model.Requests.ShouldHaveSingleItem();
        var format = asked.ResponseFormat.ShouldNotBeNull();
        format.SchemaName.ShouldBe(nameof(PlanNodeFindings));
        format.JsonSchema.ShouldContain("Findings");
        format.JsonSchema.ShouldNotContain("Operation");
        format.JsonSchema.ShouldNotContain("Options");
        format.JsonSchema.ShouldNotContain("Done");

        var prompt = string.Join("\n", asked.Messages.Select(m => m.Content));
        prompt.ShouldNotContain("Options");
        prompt.ShouldNotContain("propose", Case.Insensitive);

        outcome.Summary.ShouldBe("split keeps the semicolon");
        outcome.Findings.ShouldHaveSingleItem().Source.ShouldBe("sqlparse/__init__.py:70");
        outcome.Candidate.ShouldBeNull();
        outcome.Options.ShouldBeEmpty();
        outcome.Done.ShouldBeTrue();
    }

    [Test]
    public async Task RunAsync_AnyAnswer_WhoseStepCalledAnOperation_HasTheCallAsItsCandidate()
    {
        var model = new CallsAnOperation(
            "edit", """{"arg1":"parser.py","arg2":"add one line"}""",
            JsonSerializer.Serialize(new PlanNodeFindings { Summary = "edited it" }));

        var outcome = await new PlanNodeAgentRunner<PlanNodeFindings>(new PlanNodeAgentOptions<PlanNodeFindings>
        {
            Model = model,
            Operations = Edits,
            OperationsAsTools = true
        }).RunAsync(Context());

        outcome.Candidate.ShouldNotBeNull().ToString().ShouldBe("edit(parser.py, add one line)");
    }

    // ── A step that answers by calling ──

    private const string SplitLine = "return [str(stmt).strip() for stmt in stack.run(sql, encoding)]";

    private static readonly string FindingsCalled = JsonSerializer.Serialize(new
    {
        Summary = "split keeps the semicolon",
        Findings = new[]
        {
            new { Claim = "split returns each statement stripped", Source = "sqlparse/__init__.py:70", Quotes = new[] { SplitLine } }
        }
    });

    private static string FindingsAnswered(string summary) =>
        JsonSerializer.Serialize(new PlanNodeFindings { Summary = summary });

    private static PlanNodeAgentRunner<PlanNodeFindings> AnsweringByCall(CallsAnOperation model) =>
        new(new PlanNodeAgentOptions<PlanNodeFindings> { Model = model, AnswerAsTool = true });

    [Test]
    public async Task RunAsync_AnsweringByCalling_TheCallIsTheAnswer_AndNoAnswerIsAskedFor()
    {
        var model = new CallsAnOperation("report_findings", FindingsCalled, FindingsAnswered("asked again"));

        var outcome = await AnsweringByCall(model).RunAsync(Context());

        model.ReportAsked.ShouldBeFalse();
        model.Requests.Count.ShouldBe(1);
        outcome.Summary.ShouldBe("split keeps the semicolon");
        var finding = outcome.Findings.ShouldHaveSingleItem();
        finding.Claim.ShouldBe("split returns each statement stripped");
        finding.Source.ShouldBe("sqlparse/__init__.py:70");
        finding.Quotes.ShouldBe([SplitLine]);
        outcome.Candidate.ShouldBeNull();
    }

    [Test]
    public async Task RunAsync_AnsweringByCalling_OffersTheAnswersFieldsAsATool_AndTheTurnSaysToCallIt()
    {
        var model = new CallsAnOperation("report_findings", FindingsCalled, FindingsAnswered("asked again"));

        await AnsweringByCall(model).RunAsync(Context());

        var asked = model.Requests[0];
        var tool = asked.Tools.ShouldNotBeNull().ShouldHaveSingleItem();
        tool.Name.ShouldBe("report_findings");
        var schema = JsonNode.Parse(tool.ParametersJsonSchema)!;
        schema["properties"]!["Summary"]!["type"]!.GetValue<string>().ShouldBe("string");
        schema["properties"]!["Findings"]!["type"]!.GetValue<string>().ShouldBe("array");
        schema["properties"]!["Findings"]!["items"]!["properties"]!["Quotes"].ShouldNotBeNull();
        asked.Messages[0].Content!.ShouldContain("Give your answer by calling report_findings");
    }

    [Test]
    public async Task RunAsync_AnsweringByCalling_AStepThatCallsNothing_IsAskedForItsAnswerAsBefore()
    {
        // Calls a tool it is not offered, so it never calls one: it answers in words, then as JSON.
        var model = new CallsAnOperation("look", "{}", FindingsAnswered("found it in words"));

        var outcome = await AnsweringByCall(model).RunAsync(Context());

        model.ReportAsked.ShouldBeTrue();
        outcome.Summary.ShouldBe("found it in words");
    }

    [Test]
    public async Task RunAsync_AnsweringByCalling_ACallWhoseFindingsCannotBeRead_IsAnsweredWhy_AndDoesNotEndTheStep()
    {
        var model = new CallsAnOperation(
            "report_findings", """{"Summary":"s","Findings":"not a list"}""", FindingsAnswered("answered after"));

        var outcome = await AnsweringByCall(model).RunAsync(Context());

        model.ToolResults.ShouldHaveSingleItem().ShouldContain("Findings");
        model.ReportAsked.ShouldBeTrue();
        outcome.Summary.ShouldBe("answered after");
    }

    [Test]
    public async Task RunAsync_AnsweringByCalling_AFindingWithNoSource_ReachesTheOutcome_ForTheGateToRefuse()
    {
        var model = new CallsAnOperation(
            "report_findings", """{"Summary":"s","Findings":[{"Claim":"it splits"}]}""", FindingsAnswered("asked again"));

        var outcome = await AnsweringByCall(model).RunAsync(Context());

        model.ReportAsked.ShouldBeFalse();
        var finding = outcome.Findings.ShouldHaveSingleItem();
        finding.Claim.ShouldBe("it splits");
        finding.Source.ShouldBeEmpty();
    }

    [Test]
    public async Task RunAsync_AnsweringAndProposingByCalling_OffersBoth_AndAnOperationCalledIsTheCandidate()
    {
        var model = new CallsAnOperation("edit", """{"arg1":"a.py","arg2":"add one line"}""", FindingsAnswered("asked again"));

        var outcome = await new PlanNodeAgentRunner<PlanNodeFindings>(new PlanNodeAgentOptions<PlanNodeFindings>
        {
            Model = model,
            Operations = Edits,
            OperationsAsTools = true,
            AnswerAsTool = true
        }).RunAsync(Context());

        model.Requests[0].Tools.ShouldNotBeNull().Select(t => t.Name).ShouldBe(["edit", "report_findings"], ignoreOrder: true);
        outcome.Candidate.ShouldNotBeNull().ToString().ShouldBe("edit(a.py, add one line)");
    }

    [Test]
    public void New_AnsweringByCalling_AnAnswerWithNoTool_IsRefused()
    {
        var refused = Should.Throw<ArgumentException>(() => new PlanNodeAgentRunner(new PlanNodeAgentOptions
        {
            Model = Model(Report()),
            AnswerAsTool = true
        }));

        refused.Message.ShouldContain(nameof(PlanNodeReport));
    }

    /// <summary>One line an edit rests on: a record, so a list of them is a list and not text to split.</summary>
    private sealed record RestingLine
    {
        public string File { get; init; } = string.Empty;
        public string Text { get; init; } = string.Empty;
    }

    private static readonly OperationCatalog RestingEdits = new(
    [
        new OperationDefinition
        {
            Name = "edit",
            Arity = 2,
            Description = "Edit a file: arg1 the path, arg2 the lines the edit rests on.",
            Lists = new Dictionary<int, Type> { [1] = typeof(RestingLine) }
        }
    ]);

    [Test]
    public async Task RunAsync_AnOperationWhoseArgumentIsAList_IsOfferedAnArray_AndItsCandidateCarriesTheListAsJson()
    {
        var model = new CallsAnOperation(
            "edit", """{"arg1":"parser.py","arg2":[{"File":"parser.py","Text":"x = 1; y = 2"}]}""",
            JsonSerializer.Serialize(new PlanNodeFindings { Summary = "edited it" }));

        var outcome = await new PlanNodeAgentRunner<PlanNodeFindings>(new PlanNodeAgentOptions<PlanNodeFindings>
        {
            Model = model,
            Operations = RestingEdits,
            OperationsAsTools = true
        }).RunAsync(Context());

        var schema = JsonNode.Parse(model.Requests[0].Tools.ShouldNotBeNull().Single(tool => tool.Name == "edit").ParametersJsonSchema)!;
        schema["properties"]!["arg1"]!["type"]!.GetValue<string>().ShouldBe("string");
        schema["properties"]!["arg2"]!["type"]!.GetValue<string>().ShouldBe("array");
        schema["properties"]!["arg2"]!["items"]!["properties"]!["Text"].ShouldNotBeNull();

        var lines = JsonSerializer.Deserialize<List<RestingLine>>(outcome.Candidate.ShouldNotBeNull().Argument(1)!).ShouldNotBeNull();
        lines.ShouldHaveSingleItem().Text.ShouldBe("x = 1; y = 2");

        RestingEdits.Legend().ShouldContain("edit(arg1, arg2 (a list))");
    }

    // ── Fixtures ──

    private static PlanNodeAgentRunner<PlanNodeFindings> FindingsRunner(SimulatedAgentModel model) =>
        new(new PlanNodeAgentOptions<PlanNodeFindings> { Model = model, TimeProvider = new FixedTime(T0) });

    private static PlanNodeAgentRunner Runner(SimulatedAgentModel model) => new(Options(model));

    private static PlanNodeAgentOptions Options(SimulatedAgentModel model) => new()
    {
        Model = model,
        TimeProvider = new FixedTime(T0)
    };

    private static PlanNodeContext Context()
    {
        var contract = Contract("Parse the input", Criterion);
        return new PlanNodeContext
        {
            PlanId = "plan",
            PlanVersion = 1,
            Node = new PlanNode { Id = "parse", Contract = contract },
            Contract = contract,
            TreeView = "[parse] Parse the input",
            Projection = new PlanTreeProjection.Projection
            {
                Text = "[parse] Parse the input",
                EstimatedTokens = 8
            }
        };
    }

    private static AgentContract Contract(string goal, params string[] criteria) =>
        new() { Goal = goal, AcceptanceCriteria = criteria };

    private static string Report() => JsonSerializer.Serialize(new PlanNodeReport
    {
        Summary = "parsed it",
        Done = true
    });

    private static SimulatedAgentModel Model(params string[] responses) =>
        SimulatedAgentModel.Sequence(responses);

    /// <summary>
    /// Calls one operation during its tool rounds, then answers the report it is asked for with
    /// <paramref name="report"/> — the way a step that ran out of rounds and wrote its proposal in prose
    /// can come back with a report that names nothing.
    /// </summary>
    private sealed class CallsAnOperation(string operation, string arguments, string report, string? said = null) : IAgentModel
    {
        private readonly List<AgentRequest> _requests = [];

        public IReadOnlyList<AgentRequest> Requests => _requests;

        /// <summary>Whether the report has been asked for yet: the tool rounds are over once it has.</summary>
        public bool ReportAsked { get; private set; }

        /// <summary>
        /// What each tool call was answered, read when the answer arrives: the engine goes on adding to
        /// the conversation a request was sent with, so it cannot be read off the request afterwards.
        /// </summary>
        public List<string> ToolResults { get; } = [];

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
        {
            _requests.Add(request);
            ReportAsked = request.ResponseFormat is not null;

            if (request.ResponseFormat is null && request.Messages.LastOrDefault() is { Role: AgentRole.Tool } result)
                ToolResults.Add(result.Content ?? string.Empty);

            if (request.ResponseFormat is not null)
            {
                return Task.FromResult(new AgentResponse { Text = report });
            }

            if (request.Messages.LastOrDefault()?.Role == AgentRole.Tool
                || request.Tools?.Any(t => t.Name == operation) != true)
            {
                return Task.FromResult(new AgentResponse { Text = "I have proposed it." });
            }

            return Task.FromResult(new AgentResponse
            {
                Text = said,
                ToolCalls = [new AgentToolCall("call-1", operation, arguments)],
                StopReason = AgentStopReason.ToolCalls
            });
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CollectingSink : IWorkflowEventSink
    {
        public List<WorkflowEvent> Events { get; } = [];

        public ValueTask ReportAsync(WorkflowEvent evt, CancellationToken ct = default)
        {
            Events.Add(evt);
            return ValueTask.CompletedTask;
        }
    }
}
