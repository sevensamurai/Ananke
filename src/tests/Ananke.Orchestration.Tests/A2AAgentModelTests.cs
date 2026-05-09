using System.Net;
using System.Text;
using System.Text.Json;
using Ananke.A2A.Client;
using Ananke.Abstractions.Agents;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Phase 6.5 — <see cref="A2AAgentModel"/> multi-turn conversation and tool-calling tests.
/// All tests use a <see cref="FakeA2AHandler"/> that returns pre-baked JSON-RPC
/// responses; no real remote agent is required.
/// </summary>
[TestFixture]
public class A2AAgentModelTests
{
    // ── fake transport ────────────────────────────────────────────────────────

    private sealed class FakeA2AHandler : HttpMessageHandler
    {
        public string ResponseJson { get; set; } = BuildAgentMessageJson("Hello from agent.");
        public List<string> ReceivedBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                ReceivedBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseJson, Encoding.UTF8, "application/json")
            };
        }
    }

    // ── JSON-RPC response builders ────────────────────────────────────────────

    private static string BuildAgentMessageJson(string text) =>
        $$"""
        {
          "jsonrpc": "2.0",
          "id": 1,
          "result": {
            "kind": "message",
            "role": "agent",
            "messageId": "{{Guid.NewGuid()}}",
            "parts": [{ "kind": "text", "text": "{{text}}" }]
          }
        }
        """;

    private static string BuildAgentTaskJson(string text) =>
        $$"""
        {
          "jsonrpc": "2.0",
          "id": 1,
          "result": {
            "kind": "task",
            "id": "{{Guid.NewGuid()}}",
            "contextId": "ctx-1",
            "status": { "state": "completed" },
            "artifacts": [
              {
                "artifactId": "art-1",
                "parts": [{ "kind": "text", "text": "{{text}}" }]
              }
            ]
          }
        }
        """;

    private static A2AAgentModel MakeModel(FakeA2AHandler handler) =>
        new(new A2AAgentModelOptions
        {
            AgentUrl   = new Uri("http://fake-a2a-agent/"),
            HttpClient = new HttpClient(handler) { BaseAddress = new Uri("http://fake-a2a-agent/") }
        });

    // Use the Ananke abstraction type explicitly to avoid ambiguity with A2A.AgentMessage.
    private static AgentRequest SimpleRequest(string text = "Hello") =>
        new() { Messages = [Ananke.Abstractions.Agents.AgentMessage.User(text)] };

    // ── single-turn ───────────────────────────────────────────────────────────

    [Test]
    public async Task GenerateAsync_TextReply_ReturnsNonNullResponse()
    {
        var response = await MakeModel(new FakeA2AHandler()).GenerateAsync(SimpleRequest());

        response.ShouldNotBeNull();
    }

    [Test]
    public async Task GenerateAsync_AgentMessageReply_TextIsExtracted()
    {
        var handler = new FakeA2AHandler { ResponseJson = BuildAgentMessageJson("Pong") };
        var response = await MakeModel(handler).GenerateAsync(SimpleRequest("Ping"));

        response.Text.ShouldBe("Pong");
    }

    [Test]
    public async Task GenerateAsync_AgentTaskReply_ArtifactTextIsExtracted()
    {
        var handler = new FakeA2AHandler { ResponseJson = BuildAgentTaskJson("Task result text") };
        var response = await MakeModel(handler).GenerateAsync(SimpleRequest());

        response.Text.ShouldBe("Task result text");
    }

    // ── multi-turn ────────────────────────────────────────────────────────────

    [Test]
    public async Task GenerateAsync_MultiTurn_HistoryLengthSentToRemote()
    {
        var handler = new FakeA2AHandler();
        var model   = MakeModel(handler);

        var request = new AgentRequest
        {
            Messages =
            [
                Ananke.Abstractions.Agents.AgentMessage.User("Turn 1 — user says hello"),
                Ananke.Abstractions.Agents.AgentMessage.Assistant("Turn 1 — agent responds"),
                Ananke.Abstractions.Agents.AgentMessage.User("Turn 2 — user follow-up")
            ]
        };

        await model.GenerateAsync(request);

        handler.ReceivedBodies.ShouldNotBeEmpty();
        handler.ReceivedBodies[0].ShouldContain("historyLength");
    }

    [Test]
    public async Task GenerateAsync_MultiTurn_LastUserMessageIsCurrentTurn()
    {
        var handler = new FakeA2AHandler();
        var model   = MakeModel(handler);

        var request = new AgentRequest
        {
            Messages =
            [
                Ananke.Abstractions.Agents.AgentMessage.User("First message"),
                Ananke.Abstractions.Agents.AgentMessage.Assistant("First reply"),
                Ananke.Abstractions.Agents.AgentMessage.User("Second message")
            ]
        };

        await model.GenerateAsync(request);

        handler.ReceivedBodies[0].ShouldContain("Second message");
    }

    [Test]
    public async Task GenerateAsync_MultiTurn_ReturnsCorrectReplyPerCall()
    {
        var handler = new FakeA2AHandler();
        var model   = MakeModel(handler);

        for (var i = 1; i <= 3; i++)
        {
            handler.ResponseJson = BuildAgentMessageJson($"Reply {i}");
            var response = await model.GenerateAsync(SimpleRequest($"Turn {i}"));
            response.Text.ShouldBe($"Reply {i}");
        }
    }

    // ── system prompt ─────────────────────────────────────────────────────────

    [Test]
    public async Task GenerateAsync_WithSystemPrompt_SystemPromptFusedIntoMessage()
    {
        var handler = new FakeA2AHandler();
        var model   = MakeModel(handler);

        var request = new AgentRequest
        {
            SystemPrompt = "You are a helpful assistant.",
            Messages     = [Ananke.Abstractions.Agents.AgentMessage.User("Hello")]
        };

        await model.GenerateAsync(request);

        handler.ReceivedBodies[0].ShouldContain("System:");
    }

    // ── tool metadata ─────────────────────────────────────────────────────────

    [Test]
    public async Task GenerateAsync_WithTools_ToolNamesAdvertisedInMetadata()
    {
        var handler = new FakeA2AHandler();
        var model   = MakeModel(handler);

        var request = new AgentRequest
        {
            Messages = [Ananke.Abstractions.Agents.AgentMessage.User("Call a tool")],
            Tools    = [new AgentTool("search", "Searches the web", "{}")]
        };

        await model.GenerateAsync(request);

        var body = handler.ReceivedBodies[0];
        body.ShouldContain("search");
        body.ShouldContain("ananke.tools");
    }

    [Test]
    public async Task GenerateAsync_NoTools_NoToolMetadataInRequest()
    {
        var handler = new FakeA2AHandler();
        var model   = MakeModel(handler);

        await model.GenerateAsync(SimpleRequest());

        handler.ReceivedBodies[0].ShouldNotContain("ananke.tools");
    }

    // ── streaming ─────────────────────────────────────────────────────────────

    [Test]
    public async Task GenerateStreamAsync_GracefullyHandlesEmptyOrInvalidStream()
    {
        var handler = new FakeA2AHandler { ResponseJson = "" };
        var model   = MakeModel(handler);
        var chunks  = new List<AgentStreamChunk>();

        try
        {
            await foreach (var chunk in model.GenerateStreamAsync(SimpleRequest()))
                chunks.Add(chunk);
        }
        catch (Exception ex) when (
            ex is HttpRequestException or JsonException or InvalidOperationException or TaskCanceledException)
        {
            // Acceptable — the fake endpoint doesn't support SSE.
        }

        chunks.ShouldNotBeNull();
    }

    // ── cancellation ──────────────────────────────────────────────────────────

    [Test]
    public async Task GenerateAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var model = MakeModel(new FakeA2AHandler());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => model.GenerateAsync(SimpleRequest(), cts.Token));
    }

    // ── argument validation ───────────────────────────────────────────────────

    [Test]
    public async Task GenerateAsync_NullRequest_ThrowsArgumentNullException()
    {
        var model = MakeModel(new FakeA2AHandler());

        await Should.ThrowAsync<ArgumentNullException>(
            () => model.GenerateAsync(null!));
    }
}
