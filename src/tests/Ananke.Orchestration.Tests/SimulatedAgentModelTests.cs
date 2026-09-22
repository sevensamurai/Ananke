using System.Text.Json;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Routing;
using Ananke.Orchestration.Agents.Simulation;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// The shipped stand-in for a provider.
/// </summary>
/// <remarks>
/// It exists because twenty-six hand-written versions of it did. The properties worth pinning are
/// the ones those copies each re-derived and sometimes got wrong: successive answers to the same
/// question, matching on what the model was actually <em>sent</em> rather than on a label passed
/// beside the request, and refusing to answer a request the script does not describe.
/// </remarks>
[TestFixture]
public class SimulatedAgentModelTests
{
    [Test]
    public async Task Fixed_AnswersTheSameThingToEveryRequest()
    {
        var model = SimulatedAgentModel.Fixed("the same");

        (await model.GenerateAsync(Ask("first"))).Text.ShouldBe("the same");
        (await model.GenerateAsync(Ask("second"))).Text.ShouldBe("the same");
    }

    [Test]
    public async Task Json_AnswersTheSerializedObject()
    {
        var model = SimulatedAgentModel.Json(new { verdict = "met" });

        var response = await model.GenerateAsync(Ask("anything"));

        JsonSerializer.Deserialize<Dictionary<string, string>>(response.Text!)!["verdict"]
            .ShouldBe("met");
    }

    [Test]
    public async Task Sequence_AnswersInOrder_ThenRepeatsTheLast()
    {
        var model = SimulatedAgentModel.Sequence(["first", "second"]);

        (await model.GenerateAsync(Ask("go"))).Text.ShouldBe("first");
        (await model.GenerateAsync(Ask("go"))).Text.ShouldBe("second");
        (await model.GenerateAsync(Ask("go"))).Text.ShouldBe("second", "the last answer stands");
    }

    [Test]
    public void Sequence_WithNoReplies_IsRejected() =>
        Should.Throw<ArgumentException>(() => SimulatedAgentModel.Sequence([]));

    // ── Matching ──

    [Test]
    public async Task FromScript_AnswersTheFirstEntryWhoseWhenAppearsInTheRequest()
    {
        var model = SimulatedAgentModel.FromScript(new SimulatedScript
        {
            Responses =
            [
                new SimulatedResponse { When = "parse", Replies = ["parsing"] },
                new SimulatedResponse { When = "render", Replies = ["rendering"] }
            ]
        });

        (await model.GenerateAsync(Ask("please render the report"))).Text.ShouldBe("rendering");
        (await model.GenerateAsync(Ask("please parse the input"))).Text.ShouldBe("parsing");
    }

    [Test]
    public async Task FromScript_MatchesTheSystemPromptToo()
    {
        // The contract reaches a job as a system prompt, so a script that could only see the user
        // message could not be keyed on the work item at all.
        var model = SimulatedAgentModel.FromScript(new SimulatedScript
        {
            Responses = [new SimulatedResponse { When = "Ship offline export", Replies = ["shipping"] }]
        });

        var response = await model.GenerateAsync(new AgentRequest
        {
            SystemPrompt = "Goal: Ship offline export of reports",
            Messages = [AgentMessage.User("get on with it")]
        });

        response.Text.ShouldBe("shipping");
    }

    [Test]
    public async Task FromScript_WhenInSystemPrompt_IgnoresTheSameTextInAMessage()
    {
        // The case this exists for: a job pins the contract into the system prompt and renders a
        // view of the surrounding work into the user turn — which names other work items' goals.
        // Without a scope, this entry would answer for whichever request mentioned "Parse the input"
        // anywhere, including the one whose own goal is something else entirely.
        var model = SimulatedAgentModel.FromScript(new SimulatedScript
        {
            Responses =
            [
                new SimulatedResponse
                {
                    When = "Parse the input",
                    WhenIn = SimulatedRequestPart.SystemPrompt,
                    Replies = ["parsing"]
                },
                new SimulatedResponse { Replies = ["something else"] }
            ]
        });

        var asParser = await model.GenerateAsync(new AgentRequest
        {
            SystemPrompt = "Goal: Parse the input",
            Messages = [AgentMessage.User("the plan so far")]
        });

        var asRenderer = await model.GenerateAsync(new AgentRequest
        {
            SystemPrompt = "Goal: Render the output",
            Messages = [AgentMessage.User("the plan so far: [parse] Parse the input")]
        });

        asParser.Text.ShouldBe("parsing");
        asRenderer.Text.ShouldBe("something else");
    }

    [Test]
    public async Task FromScript_WhenInMessages_IgnoresTheSameTextInTheSystemPrompt()
    {
        var model = SimulatedAgentModel.FromScript(new SimulatedScript
        {
            Responses =
            [
                new SimulatedResponse
                {
                    When = "the golden file",
                    WhenIn = SimulatedRequestPart.Messages,
                    Replies = ["comparing"]
                },
                new SimulatedResponse { Replies = ["something else"] }
            ]
        });

        var response = await model.GenerateAsync(new AgentRequest
        {
            SystemPrompt = "Criterion: output matches the golden file",
            Messages = [AgentMessage.User("get on with it")]
        });

        response.Text.ShouldBe("something else");
    }

    [Test]
    public async Task FromScript_AnEntryWithNoWhen_AnswersAnything()
    {
        var model = SimulatedAgentModel.FromScript(new SimulatedScript
        {
            Responses =
            [
                new SimulatedResponse { When = "parse", Replies = ["parsing"] },
                new SimulatedResponse { Replies = ["anything else"] }
            ]
        });

        (await model.GenerateAsync(Ask("parse this"))).Text.ShouldBe("parsing");
        (await model.GenerateAsync(Ask("something unrelated"))).Text.ShouldBe("anything else");
    }

    [Test]
    public async Task FromScript_SuccessiveCallsMatchingOneEntry_GetSuccessiveReplies()
    {
        // The shape of work that takes more than one go: it fails, it is fixed, it passes.
        var model = SimulatedAgentModel.FromScript(new SimulatedScript
        {
            Responses =
            [
                new SimulatedResponse { When = "endpoint", Replies = ["returns the file inline", "streams it"] }
            ]
        });

        (await model.GenerateAsync(Ask("build the endpoint"))).Text.ShouldBe("returns the file inline");
        (await model.GenerateAsync(Ask("build the endpoint"))).Text.ShouldBe("streams it");
    }

    [Test]
    public async Task FromScript_CallsMatchingDifferentEntries_DoNotShareAPlaceInTheScript()
    {
        var model = SimulatedAgentModel.FromScript(new SimulatedScript
        {
            Responses =
            [
                new SimulatedResponse { When = "parse", Replies = ["parse 1", "parse 2"] },
                new SimulatedResponse { When = "render", Replies = ["render 1", "render 2"] }
            ]
        });

        (await model.GenerateAsync(Ask("parse"))).Text.ShouldBe("parse 1");
        (await model.GenerateAsync(Ask("render"))).Text.ShouldBe("render 1");
        (await model.GenerateAsync(Ask("parse"))).Text.ShouldBe("parse 2");
    }

    [Test]
    public async Task FromScript_ARequestNothingDescribes_IsRefusedAndQuoted()
    {
        // Answering anyway would turn an unscripted request into a passing run that proves nothing —
        // which is exactly how a contract that stopped reaching the model would go unnoticed.
        var model = SimulatedAgentModel.FromScript(new SimulatedScript
        {
            Responses = [new SimulatedResponse { When = "parse", Replies = ["parsing"] }]
        });

        var error = await Should.ThrowAsync<InvalidOperationException>(
            () => model.GenerateAsync(Ask("render the report")));

        error.Message.ShouldContain("render the report");
    }

    [Test]
    public void FromScript_AnEntryWithNoReplies_IsRejectedWhenTheScriptIsRead()
    {
        var error = Should.Throw<ArgumentException>(() => SimulatedAgentModel.FromScript(
            new SimulatedScript { Responses = [new SimulatedResponse { When = "parse" }] }));

        error.Message.ShouldContain("parse");
    }

    // ── From a file ──

    [Test]
    public async Task FromFile_ReadsTheScript_AndHandsAJsonObjectReplyOverVerbatim()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        await File.WriteAllTextAsync(path, """
            {
              "responses": [
                {
                  "when": "the parser round-trips",
                  "replies": [
                    { "summary": "it round-trips", "met": true },
                    "a plain string reply"
                  ]
                }
              ]
            }
            """);

        try
        {
            var model = SimulatedAgentModel.FromFile(path);

            // Written as an object in the file so a reviewer can read it, handed over as the JSON
            // the job will deserialize.
            var first = await model.GenerateAsync(Ask("check that the parser round-trips"));
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(first.Text!)!["summary"]
                .GetString().ShouldBe("it round-trips");

            var second = await model.GenerateAsync(Ask("check that the parser round-trips"));
            second.Text.ShouldBe("a plain string reply");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── What it reports about itself ──

    [Test]
    public async Task Requests_RecordsEverythingItWasSent()
    {
        var model = SimulatedAgentModel.Fixed("fine");

        await model.GenerateAsync(new AgentRequest
        {
            SystemPrompt = "the contract",
            Messages = [AgentMessage.User("the work")]
        });

        model.Calls.ShouldBe(1);
        model.Requests[0].SystemPrompt.ShouldBe("the contract");
        model.Requests[0].Messages[0].Content.ShouldBe("the work");
    }

    [Test]
    public void ResolveContextWindow_WithNoneDeclared_IsUnknown() =>
        SimulatedAgentModel.Fixed("fine").ResolveContextWindow(Ask("go"))
            .ShouldBe(ModelContextWindow.Unknown);

    [Test]
    public void ResolveContextWindow_WithOneDeclared_ReportsIt()
    {
        // Without this the context instruments have no window to measure fill against, and every
        // figure they produce degrades to "no call knew its window".
        var model = SimulatedAgentModel.Fixed(
            "fine", new SimulatedModelOptions { ModelName = "scripted", ContextWindowTokens = 8_000 });

        var window = model.ResolveContextWindow(Ask("go"));

        window.ModelName.ShouldBe("scripted");
        window.ContextTokens.ShouldBe(8_000);
        window.IsKnown.ShouldBeTrue();
    }

    [Test]
    public async Task GenerateStreamAsync_StreamsTheAnswer_AndEndsWithTheWholeOfIt()
    {
        var model = SimulatedAgentModel.Fixed("two words");

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in model.GenerateStreamAsync(Ask("go")))
            chunks.Add(chunk);

        chunks[^1].CompletedResponse!.Text.ShouldBe("two words");
        model.Calls.ShouldBe(1, "streaming a reply is one call, not two");
    }

    private static AgentRequest Ask(string text) =>
        new() { Messages = [AgentMessage.User(text)] };
}
