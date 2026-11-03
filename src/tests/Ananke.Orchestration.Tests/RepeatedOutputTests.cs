using System.Text;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Tests.Fakes;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// A reply whose end repeats one passage over and over is stopped and ends the run, and a long reply
/// that never repeats itself is left alone.
/// </summary>
[TestFixture]
public class RepeatedOutputTests
{
    // How the live reply began, before it fell into repeating its last sentence until the provider's
    // limit of 131,072 output tokens.
    private const string Opening =
        "{\n  \"Summary\": \"Added Step 1 of the plan: give sqlparse.split a strip_semicolon keyword (off by "
        + "default) that removes each statement's trailing semicolon, with a documented docstring, leaving "
        + "the default output unchanged and the rest of the module untouched. Remaining work (CHANGELOG "
        + "entry and tests in tests/test_split.py) will follow in later steps. ";

    private const string Repeated =
        "The step is written so it leaves the codebase working on its own: existing callers and tests keep "
        + "passing because the default behavior is byte-identical to today's, making this a safe standalone "
        + "first edit before the later documentation and test additions. ";

    private static string Looping(int repeats) =>
        Opening + string.Concat(Enumerable.Repeat(Repeated, repeats)) + Repeated[..40];

    [Test]
    public void ThrowIfRepeating_AReplyThatEndsRepeatingOnePassage_Throws()
    {
        var thrown = Should.Throw<RepeatedOutputException>(() => RepeatedOutput.ThrowIfRepeating(Looping(20)));

        thrown.Message.ShouldContain("The step is written so it leaves the codebase working");
    }

    [Test]
    public void ThrowIfRepeating_ALongReplyThatNeverRepeatsAPassage_DoesNot()
    {
        var text = new StringBuilder("| Step | Goal |\n|").Append('-', 80).Append("|\n");

        for (var i = 1; i <= 400; i++)
            text.Append("| step-").Append(i).Append(" | In module_").Append(i % 7)
                .Append(".py, change function_").Append(i).Append(" so it returns ").Append(i * i).Append(". |\n");

        Should.NotThrow(() => RepeatedOutput.ThrowIfRepeating(text.ToString()));

        var watch = new RepeatedOutput();
        Should.NotThrow(() =>
        {
            foreach (var piece in text.ToString().Chunk(5))
                watch.Add(new string(piece));
        });
    }

    [Test]
    public void Add_AStreamThatRepeatsOnePassage_IsStoppedWithinAFewThousandCharacters()
    {
        var watch = new RepeatedOutput();
        var looping = Looping(2_000);
        var fed = 0;

        Should.Throw<RepeatedOutputException>(() =>
        {
            for (; fed < looping.Length; fed += 7)
                watch.Add(looping.Substring(fed, Math.Min(7, looping.Length - fed)));
        });

        fed.ShouldBeLessThan(3_000);
    }

    [Test]
    public async Task AgentJob_AReplyThatRepeatsItself_ThrowsRepeatedOutput_AndIsAskedOnce()
    {
        // Cut off at the limit while repeating: the cause is the repetition, and a larger limit would
        // only have bought more of it.
        var model = new ScriptedAgentModel(new AgentResponse { Text = Looping(200), StopReason = AgentStopReason.MaxTokens });

        var job = AgentJobFactory.Create<string, Answer>("answer", model)
            .WithPrompt(s => s)
            .MapResult((s, r) => r.Summary)
            .Build();

        await Should.ThrowAsync<RepeatedOutputException>(
            () => job.ExecuteAsync("question").WaitAsync(TimeSpan.FromSeconds(10)));

        model.Requests.Count.ShouldBe(1);
    }

    [Test]
    public async Task AgentJob_AReplyThatEndedOnItsOwn_IsNotCheckedForRepeats()
    {
        // Fixture rows, asked for: a reply the model ended itself did not run away, whatever its last
        // lines look like.
        var rows = string.Concat(Enumerable.Repeat("0,0,0,0,0,0,0,0,0,0\n", 60));
        var model = new ScriptedAgentModel(new AgentResponse { Text = "a,b,c,d,e,f,g,h,i,j\n" + rows, StopReason = AgentStopReason.EndTurn });

        var job = AgentJobFactory.Create<string>("rows", model)
            .WithPrompt(s => s)
            .MapResult((_, reply) => reply)
            .Build();

        (await job.ExecuteAsync("sixty rows of zeros").WaitAsync(TimeSpan.FromSeconds(10))).ShouldEndWith(rows);
    }

    [Test]
    public async Task AgentJob_AReplyFromAnAdapterThatSaysNotWhyItStopped_IsStillChecked()
    {
        var model = new ScriptedAgentModel(new AgentResponse { Text = Looping(200) });

        var job = AgentJobFactory.Create<string>("answer", model)
            .WithPrompt(s => s)
            .MapResult((_, reply) => reply)
            .Build();

        await Should.ThrowAsync<RepeatedOutputException>(
            () => job.ExecuteAsync("question").WaitAsync(TimeSpan.FromSeconds(10)));
    }

    private sealed record Answer
    {
        public string Summary { get; init; } = "";
    }
}
