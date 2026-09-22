using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.OpenAI;
using Ananke.Orchestration.Planning;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// What a real reviewer does with a real record — the half no fake can answer.
/// </summary>
/// <remarks>
/// <para>
/// Two failures look identical from outside: a reviewer that read the record and honestly could not
/// tell, and a reviewer whose answer never parsed. Both leave every criterion abstained. So one live
/// case each: a criterion the record settles, which must be <b>ruled</b>, and one it does not, which
/// must be <b>abstained</b>.
/// </para>
/// <para>
/// Explicit and live — it spends two model calls. Run with <c>--filter TestCategory=Live</c>.
/// </para>
/// </remarks>
[TestFixture]
[Category("Live")]
[Explicit("Calls a real model; needs OPENAI_API_KEY.")]
public class AgentVerifierLiveTests
{
    private const string InTheRecord =
        "the changelog step has been ruled on and its criterion held";
    private const string NotInTheRecord = "notes.md is at most eight lines long";

    private const string Record = """
        release-pack — Pending
          Produce a release pack for version 1.4.
          list-changes — Satisfied
            Read the changelog and save changes.csv.
            · met      changes.csv exists and lists every change.  (the files in the run folder)
          write-notes — Pending
            Save notes.md, a summary of release 1.4 for a reader, in at most 8 lines.
        """;

    [Test]
    public async Task Reviewer_ACriterionTheRecordSettles_IsRuledWithItsGrounds()
    {
        // The criteria a record can settle are the ones about *what has been decided* — which is
        // what a parent's contract asserts, since its children are ruled on before it runs. Not what
        // a step was asked to do: that is an instruction, and a reviewer treating it as evidence
        // would be recording "it was told to" as "it did".
        var ruling = await Rule(InTheRecord);

        var verdict = ruling.Verdicts.ShouldHaveSingleItem();
        verdict.Passed.ShouldBeTrue();
        verdict.Basis.ShouldNotBeNullOrWhiteSpace();
        ruling.Abstained.ShouldBeEmpty();
    }

    [Test]
    public async Task Reviewer_ARollUpTheRecordContradicts_IsRuledNotMet()
    {
        // The case a model is most likely to gloss: a criterion about *every* step, where one of
        // them has no verdict at all. Seen live — a reviewer answered "met" and its own grounds
        // named only the two steps that had verdicts, skipping the third in silence. A verdict is
        // recorded with its basis precisely so that kind of answer is readable afterwards; the
        // wording it needs is one that names what it is counting.
        var ruling = await Rule("every step of this plan has a recorded verdict");

        var verdict = ruling.Verdicts.ShouldHaveSingleItem();
        verdict.Passed.ShouldBeFalse("write-notes has no verdict in the record");
        verdict.Basis.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task Reviewer_ACriterionAboutAnArtifactItCannotSee_Abstains()
    {
        // The boundary that makes this safe to put in the control path. The record says a node was
        // asked for eight lines; it does not say what the file contains, and a reviewer that ruled
        // on that would be laundering the instruction into evidence that it was followed.
        var ruling = await Rule(NotInTheRecord);

        ruling.Abstained.ShouldBe([NotInTheRecord]);
        ruling.Verdicts.ShouldBeEmpty();
    }

    private static Task<Verification> Rule(string criterion)
    {
        var model = OpenAIChatAgentModel.Create(
            Keys.Require("OPENAI_API_KEY"),
            Environment.GetEnvironmentVariable("ANANKE_TEST_MODEL") ?? Models.OpenAI.Starred);

        var node = new PlanNode
        {
            Id = "release-pack",
            Contract = new AgentContract
            {
                Goal = "Produce a release pack for version 1.4.",
                AcceptanceCriteria = [criterion]
            }
        };

        return new AgentVerifier(model).VerifyAsync(new VerificationRequest
        {
            Node = node,
            TreeView = Record
        });
    }
}
