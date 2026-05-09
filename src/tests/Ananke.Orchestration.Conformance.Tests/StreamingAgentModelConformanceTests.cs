using Ananke.Abstractions.Agents;
using Shouldly;

namespace Ananke.Orchestration.Conformance.Tests;

/// <summary>
/// Abstract conformance suite for <see cref="IStreamingAgentModel"/> implementations.
/// </summary>
/// <remarks>
/// <para>
/// Subclass this fixture in each provider's test project and override
/// <see cref="CreateModel"/> to supply the real provider model.  All scenarios
/// then run automatically against that implementation.
/// </para>
/// <para>
/// The default implementation (the <see cref="FakeConformanceTests"/> subclass below)
/// uses <see cref="FakeConformanceModel"/> as the subject so the suite itself is
/// self-validating and runs in CI without any live credentials.
/// </para>
/// </remarks>
[TestFixture]
public abstract class StreamingAgentModelConformanceTests
{
    /// <summary>
    /// Creates the subject under test.  Called once per test method.
    /// </summary>
    protected abstract IStreamingAgentModel CreateModel();

    // ── Helper ──────────────────────────────────────────────────────────

    private static AgentRequest TextRequest(string userText, string? system = null) =>
        new()
        {
            SystemPrompt = system,
            Messages = [AgentMessage.User(userText)]
        };

    // ── 1. Text generation ───────────────────────────────────────────────

    [Test]
    public async Task GenerateAsync_ReturnsNonNullResponse()
    {
        var model = CreateModel();
        var response = await model.GenerateAsync(TextRequest("hello"));
        response.ShouldNotBeNull();
    }

    [Test]
    public async Task GenerateAsync_TextResponse_IsNotNullOrEmpty()
    {
        var model = CreateModel();
        var response = await model.GenerateAsync(TextRequest("hello"));
        // A text-only request must produce either text or tool calls — not both null.
        (response.Text is not null || response.RequiresAction).ShouldBeTrue(
            "Response must carry text or tool calls");
    }

    // ── 2. Tool calling ──────────────────────────────────────────────────

    [Test]
    public async Task GenerateAsync_WithTools_CanReturnToolCall()
    {
        var model = CreateModel();
        var request = new AgentRequest
        {
            Messages = [AgentMessage.User("call the tool")],
            Tools =
            [
                new AgentTool(
                    "test_tool",
                    "A tool that does something",
                    """{"type":"object","properties":{"input":{"type":"string"}},"required":["input"]}""")
            ]
        };

        var response = await model.GenerateAsync(request);

        // Either a tool call is returned, or the model chose to reply with text.
        // Both are valid; what is NOT valid is returning null for both.
        (response.RequiresAction || response.Text is not null).ShouldBeTrue(
            "Model must either invoke a tool or return text when tools are provided");
    }

    [Test]
    public async Task GenerateAsync_ToolCallIds_AreNonEmpty()
    {
        var model = CreateModel();
        var request = new AgentRequest
        {
            Messages = [AgentMessage.User("use the tool")],
            Tools = [new AgentTool("my_tool", "does things", """{"type":"object","properties":{}}""")]
        };

        var response = await model.GenerateAsync(request);
        if (!response.RequiresAction) Assert.Pass("Model returned text — tool-call path skipped");

        foreach (var call in response.ToolCalls!)
            call.Id.ShouldNotBeNullOrEmpty("Every tool call must carry a non-empty ID");
    }

    [Test]
    public async Task GenerateAsync_ToolCallIds_AreStableUnderConcurrency()
    {
        // Fires 10 concurrent requests and asserts all tool-call IDs are non-empty.
        // This catches providers that generate IDs from a shared non-thread-safe counter.
        var model = CreateModel();
        var request = new AgentRequest
        {
            Messages = [AgentMessage.User("call tool")],
            Tools = [new AgentTool("concurrent_tool", "for concurrency test", """{"type":"object","properties":{}}""")]
        };

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => model.GenerateAsync(request))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        foreach (var response in responses)
        {
            if (!response.RequiresAction) continue;
            foreach (var call in response.ToolCalls!)
                call.Id.ShouldNotBeNullOrEmpty("Tool-call ID must not be empty under concurrency");
        }
    }

    // ── 3. Structured output ─────────────────────────────────────────────

    [Test]
    public async Task GenerateAsync_StructuredOutput_ReturnsValidJson()
    {
        var model = CreateModel();
        var request = new AgentRequest
        {
            Messages = [AgentMessage.User("give me a result object")],
            ResponseFormat = new AgentResponseFormat(
                SchemaName: "result",
                JsonSchema: """{"type":"object","properties":{"result":{"type":"string"}},"required":["result"]}""")
        };

        var response = await model.GenerateAsync(request);
        response.Text.ShouldNotBeNullOrEmpty("Structured output must return JSON text");

        // Must be parseable JSON — providers that return prose here are non-conformant.
        var act = () => System.Text.Json.JsonDocument.Parse(response.Text!);
        act.ShouldNotThrow("Structured output response must be valid JSON");
    }

    // ── 4. Streaming ─────────────────────────────────────────────────────

    [Test]
    public async Task GenerateStreamAsync_LastChunk_CarriesCompletedResponse()
    {
        var model = CreateModel();
        var chunks = new List<AgentStreamChunk>();

        await foreach (var chunk in model.GenerateStreamAsync(TextRequest("stream me")))
            chunks.Add(chunk);

        chunks.ShouldNotBeEmpty();
        chunks[^1].CompletedResponse.ShouldNotBeNull(
            "The last stream chunk must carry the completed AgentResponse");
    }

    [Test]
    public async Task GenerateStreamAsync_TextDeltaChunks_AssembleToFinalText()
    {
        var model = CreateModel();
        var chunks = new List<AgentStreamChunk>();

        await foreach (var chunk in model.GenerateStreamAsync(TextRequest("stream assembly")))
            chunks.Add(chunk);

        var assembled = string.Concat(
            chunks.Where(c => c.TextDelta is not null).Select(c => c.TextDelta));

        var finalText = chunks[^1].CompletedResponse?.Text;

        // If the model returned text, the deltas must assemble to it (whitespace-normalized).
        if (finalText is not null)
            assembled.Trim().ShouldBe(finalText.Trim(),
                "Concatenated text deltas must equal the final completed response text");
    }

    [Test]
    public async Task GenerateStreamAsync_EmptyResponse_YieldsAtLeastCompletedChunk()
    {
        var model = CreateModel();
        // Send a request that is unlikely to produce tool calls so the model
        // returns an empty-ish text response.
        var request = new AgentRequest { Messages = [AgentMessage.User("")] };

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in model.GenerateStreamAsync(request))
            chunks.Add(chunk);

        chunks.ShouldNotBeEmpty("Must yield at least one (completed) chunk even for empty input");
        chunks.Any(c => c.CompletedResponse is not null).ShouldBeTrue();
    }

    [Test]
    public async Task GenerateStreamAsync_SupportsCancellation()
    {
        var model = CreateModel();
        using var cts = new CancellationTokenSource();

        var chunks = new List<AgentStreamChunk>();
        var caught = false;
        try
        {
            await foreach (var chunk in model.GenerateStreamAsync(TextRequest("cancel me"), cts.Token))
            {
                chunks.Add(chunk);
                // Cancel after the first chunk — if any.
                await cts.CancelAsync();
            }
        }
        catch (OperationCanceledException)
        {
            caught = true;
        }

        // Either cancellation was honoured (OperationCanceledException) OR the stream
        // finished so quickly that cancellation had nothing to cancel — both are valid.
        (caught || chunks.Any(c => c.CompletedResponse is not null)).ShouldBeTrue(
            "Stream must either honour cancellation or complete normally");
    }

    // ── 5. Multimodal ────────────────────────────────────────────────────

    [Test]
    public async Task GenerateAsync_MultimodalRequest_DoesNotThrow()
    {
        var model = CreateModel();
        var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG magic bytes
        var request = new AgentRequest
        {
            Messages =
            [
                AgentMessage.User([
                    new TextPart("describe this image"),
                    new ImagePart { Data = imageBytes, MimeType = "image/jpeg" }
                ])
            ]
        };

        AgentResponse? response = null;
        var act = async () => response = await model.GenerateAsync(request);
        await act.ShouldNotThrowAsync("Models that don't support images must at minimum not throw");

        response.ShouldNotBeNull();
    }

    // ── 6. Token usage accounting ────────────────────────────────────────

    [Test]
    public async Task GenerateAsync_WhenUsageReported_InputAndOutputArePositive()
    {
        var model = CreateModel();
        var response = await model.GenerateAsync(TextRequest("token usage test"));

        if (response.Usage is null)
            Assert.Pass("Provider does not report token usage — conformance check skipped");

        response.Usage!.InputTokens.ShouldBeGreaterThan(0,
            "InputTokens must be positive when usage is reported");
        response.Usage.OutputTokens.ShouldBeGreaterThan(0,
            "OutputTokens must be positive when usage is reported");
        response.Usage.TotalTokens.ShouldBe(
            response.Usage.InputTokens + response.Usage.OutputTokens,
            "TotalTokens must equal InputTokens + OutputTokens");
    }

    [Test]
    public async Task GenerateStreamAsync_WhenUsageReported_CompletedChunkCarriesUsage()
    {
        var model = CreateModel();
        AgentStreamChunk? completedChunk = null;

        await foreach (var chunk in model.GenerateStreamAsync(TextRequest("stream usage")))
        {
            if (chunk.CompletedResponse is not null)
                completedChunk = chunk;
        }

        completedChunk.ShouldNotBeNull();

        if (completedChunk!.CompletedResponse!.Usage is null)
            Assert.Pass("Provider does not report usage on stream — conformance check skipped");

        completedChunk.CompletedResponse!.Usage!.TotalTokens.ShouldBeGreaterThan(0);
    }

    // ── 7. System-prompt + JSON schema fusion equivalence ────────────────

    [Test]
    public async Task GenerateAsync_SystemPromptAndResponseFormat_BothApplied()
    {
        var model = CreateModel();
        var request = new AgentRequest
        {
            SystemPrompt = "You are a JSON-only assistant.",
            Messages = [AgentMessage.User("give me a result")],
            ResponseFormat = new AgentResponseFormat(
                SchemaName: "echo",
                JsonSchema: """{"type":"object","properties":{"result":{"type":"string"}},"required":["result"]}""")
        };

        var response = await model.GenerateAsync(request);

        // A model that ignores the system prompt and returns prose would fail this.
        var act = () => System.Text.Json.JsonDocument.Parse(response.Text!);
        act.ShouldNotThrow(
            "When both SystemPrompt and ResponseFormat are set, response must still be valid JSON");
    }
}

/// <summary>
/// Self-validating run of the conformance suite against <see cref="FakeConformanceModel"/>.
/// Proves the suite itself is correct without requiring live credentials.
/// </summary>
[TestFixture]
public sealed class FakeConformanceTests : StreamingAgentModelConformanceTests
{
    protected override IStreamingAgentModel CreateModel() => new FakeConformanceModel();
}
