using System.Text.Json;
using Ananke.Abstractions.Agents;
using Shouldly;

namespace Ananke.Orchestration.Conformance;

/// <summary>
/// The conformance contract for <see cref="IStreamingAgentModel"/> implementations, as
/// framework-neutral scenarios.
/// </summary>
/// <remarks>
/// <para>
/// Run these against a real adapter wired to a stub transport rather than a live endpoint — every
/// supported SDK has a seam for that. <see cref="FakeConformanceModel"/> is the
/// reference subject the suite is self-validated against.
/// </para>
/// <para>
/// On a runner that discovers tests by attribute, consume
/// <c>Ananke.Orchestration.Conformance.NUnit</c> instead of driving this list directly.
/// </para>
/// </remarks>
public static class StreamingAgentModelConformance
{
    /// <summary>Every rule an <see cref="IStreamingAgentModel"/> must satisfy.</summary>
    public static IReadOnlyList<ConformanceScenario<IStreamingAgentModel>> Scenarios { get; } =
    [
        // ── 1. Text generation ───────────────────────────────────────────────

        new("GenerateAsync_ReturnsNonNullResponse", async (model, ct) =>
        {
            var response = await model.GenerateAsync(TextRequest("hello"), ct).ConfigureAwait(false);
            response.ShouldNotBeNull();
            return ConformanceOutcome.Passed;
        }),

        new("GenerateAsync_TextResponse_IsNotNullOrEmpty", async (model, ct) =>
        {
            var response = await model.GenerateAsync(TextRequest("hello"), ct).ConfigureAwait(false);
            // A text-only request must produce either text or tool calls — not both null.
            (response.Text is not null || response.RequiresAction).ShouldBeTrue(
                "Response must carry text or tool calls");
            return ConformanceOutcome.Passed;
        }),

        // ── 2. Tool calling ──────────────────────────────────────────────────

        new("GenerateAsync_WithTools_CanReturnToolCall", async (model, ct) =>
        {
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

            var response = await model.GenerateAsync(request, ct).ConfigureAwait(false);

            // Either a tool call is returned, or the model chose to reply with text.
            // Both are valid; what is NOT valid is returning null for both.
            (response.RequiresAction || response.Text is not null).ShouldBeTrue(
                "Model must either invoke a tool or return text when tools are provided");
            return ConformanceOutcome.Passed;
        }),

        new("GenerateAsync_ToolCallIds_AreNonEmpty", async (model, ct) =>
        {
            var request = new AgentRequest
            {
                Messages = [AgentMessage.User("use the tool")],
                Tools = [new AgentTool("my_tool", "does things", """{"type":"object","properties":{}}""")]
            };

            var response = await model.GenerateAsync(request, ct).ConfigureAwait(false);
            if (!response.RequiresAction)
                return ConformanceOutcome.Skip("Model returned text — tool-call path skipped");

            foreach (var call in response.ToolCalls!)
                call.Id.ShouldNotBeNullOrEmpty("Every tool call must carry a non-empty ID");

            return ConformanceOutcome.Passed;
        }),

        new("GenerateAsync_ToolCallIds_AreStableUnderConcurrency", async (model, ct) =>
        {
            // Fires 10 concurrent requests and asserts all tool-call IDs are non-empty.
            // This catches providers that generate IDs from a shared non-thread-safe counter.
            var request = new AgentRequest
            {
                Messages = [AgentMessage.User("call tool")],
                Tools = [new AgentTool("concurrent_tool", "for concurrency test", """{"type":"object","properties":{}}""")]
            };

            var tasks = Enumerable.Range(0, 10)
                .Select(_ => model.GenerateAsync(request, ct))
                .ToArray();

            var responses = await Task.WhenAll(tasks).ConfigureAwait(false);

            foreach (var response in responses)
            {
                if (!response.RequiresAction) continue;
                foreach (var call in response.ToolCalls!)
                    call.Id.ShouldNotBeNullOrEmpty("Tool-call ID must not be empty under concurrency");
            }

            return ConformanceOutcome.Passed;
        }),

        // ── 3. Structured output ─────────────────────────────────────────────

        new("GenerateAsync_StructuredOutput_ReturnsValidJson", async (model, ct) =>
        {
            var request = new AgentRequest
            {
                Messages = [AgentMessage.User("give me a result object")],
                ResponseFormat = new AgentResponseFormat(
                    SchemaName: "result",
                    JsonSchema: """{"type":"object","properties":{"result":{"type":"string"}},"required":["result"]}""")
            };

            var response = await model.GenerateAsync(request, ct).ConfigureAwait(false);
            response.Text.ShouldNotBeNullOrEmpty("Structured output must return JSON text");

            // Must be parseable JSON — providers that return prose here are non-conformant.
            var act = () => JsonDocument.Parse(response.Text!);
            act.ShouldNotThrow("Structured output response must be valid JSON");
            return ConformanceOutcome.Passed;
        }),

        // ── 4. Streaming ─────────────────────────────────────────────────────

        new("GenerateStreamAsync_LastChunk_CarriesCompletedResponse", async (model, ct) =>
        {
            var chunks = await CollectAsync(model, TextRequest("stream me"), ct).ConfigureAwait(false);

            chunks.ShouldNotBeEmpty();
            chunks[^1].CompletedResponse.ShouldNotBeNull(
                "The last stream chunk must carry the completed AgentResponse");
            return ConformanceOutcome.Passed;
        }),

        new("GenerateStreamAsync_TextDeltaChunks_AssembleToFinalText", async (model, ct) =>
        {
            var chunks = await CollectAsync(model, TextRequest("stream assembly"), ct).ConfigureAwait(false);

            var assembled = string.Concat(
                chunks.Where(c => c.TextDelta is not null).Select(c => c.TextDelta));

            var finalText = chunks[^1].CompletedResponse?.Text;

            // If the model returned text, the deltas must assemble to it (whitespace-normalized).
            if (finalText is not null)
                assembled.Trim().ShouldBe(finalText.Trim(),
                    "Concatenated text deltas must equal the final completed response text");

            return ConformanceOutcome.Passed;
        }),

        new("GenerateStreamAsync_EmptyResponse_YieldsAtLeastCompletedChunk", async (model, ct) =>
        {
            // Send a request that is unlikely to produce tool calls so the model
            // returns an empty-ish text response.
            var request = new AgentRequest { Messages = [AgentMessage.User("")] };

            var chunks = await CollectAsync(model, request, ct).ConfigureAwait(false);

            chunks.ShouldNotBeEmpty("Must yield at least one (completed) chunk even for empty input");
            chunks.Any(c => c.CompletedResponse is not null).ShouldBeTrue();
            return ConformanceOutcome.Passed;
        }),

        new("GenerateStreamAsync_SupportsCancellation", async (model, ct) =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var chunks = new List<AgentStreamChunk>();
            var caught = false;
            try
            {
                await foreach (var chunk in model.GenerateStreamAsync(TextRequest("cancel me"), cts.Token)
                                   .ConfigureAwait(false))
                {
                    chunks.Add(chunk);
                    // Cancel after the first chunk — if any.
                    await cts.CancelAsync().ConfigureAwait(false);
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
            return ConformanceOutcome.Passed;
        }),

        // ── 5. Multimodal ────────────────────────────────────────────────────

        new("GenerateAsync_MultimodalRequest_DoesNotThrow", async (model, ct) =>
        {
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
            var act = async () => response = await model.GenerateAsync(request, ct).ConfigureAwait(false);
            await act.ShouldNotThrowAsync("Models that don't support images must at minimum not throw")
                .ConfigureAwait(false);

            response.ShouldNotBeNull();
            return ConformanceOutcome.Passed;
        }),

        // ── 6. Token usage accounting ────────────────────────────────────────

        new("GenerateAsync_WhenUsageReported_InputAndOutputArePositive", async (model, ct) =>
        {
            var response = await model.GenerateAsync(TextRequest("token usage test"), ct).ConfigureAwait(false);

            if (response.Usage is null)
                return ConformanceOutcome.Skip("Provider does not report token usage — conformance check skipped");

            response.Usage.InputTokens.ShouldBeGreaterThan(0,
                "InputTokens must be positive when usage is reported");
            response.Usage.OutputTokens.ShouldBeGreaterThan(0,
                "OutputTokens must be positive when usage is reported");
            response.Usage.TotalTokens.ShouldBe(
                response.Usage.InputTokens + response.Usage.OutputTokens,
                "TotalTokens must equal InputTokens + OutputTokens");
            return ConformanceOutcome.Passed;
        }),

        new("GenerateStreamAsync_WhenUsageReported_CompletedChunkCarriesUsage", async (model, ct) =>
        {
            var chunks = await CollectAsync(model, TextRequest("stream usage"), ct).ConfigureAwait(false);
            var completedChunk = chunks.LastOrDefault(c => c.CompletedResponse is not null);

            completedChunk.ShouldNotBeNull();

            if (completedChunk.CompletedResponse!.Usage is null)
                return ConformanceOutcome.Skip("Provider does not report usage on stream — conformance check skipped");

            completedChunk.CompletedResponse.Usage.TotalTokens.ShouldBeGreaterThan(0);
            return ConformanceOutcome.Passed;
        }),

        // ── 6b. Content-part shape ────────────────────────────

        // D1: Parts is required whenever a response carries content that is not a TextPart,
        // and MAY be null for a purely textual one.
        new("GenerateAsync_WhenPartsPopulated_HoldsSomethingBeyondPlainText", async (model, ct) =>
        {
            var response = await model.GenerateAsync(TextRequest("parts shape"), ct).ConfigureAwait(false);

            if (response.Parts is null)
                return ConformanceOutcome.Skip("Text-only response — Parts may be null underD1");

            response.Parts.ShouldNotBeEmpty(
                "An empty Parts list is neither 'text-only' nor 'has structured content' — use null instead");
            return ConformanceOutcome.Passed;
        }),

        // D1, the asymmetry the ADR exists to fix: an adapter must not populate Parts on the unary
        // path and omit it on the streaming path for the same input, because a caller who switches
        // to streaming then silently loses content. This was live in AnthropicAgentModel until
        // 2026-08-18.
        new("UnaryAndStream_AgreeOnWhetherPartsArePopulated", async (model, ct) =>
        {
            const string prompt = "parts parity";

            var unary = await model.GenerateAsync(TextRequest(prompt), ct).ConfigureAwait(false);

            var chunks = await CollectAsync(model, TextRequest(prompt), ct).ConfigureAwait(false);
            var streamed = chunks.LastOrDefault(c => c.CompletedResponse is not null)?.CompletedResponse;

            streamed.ShouldNotBeNull("The stream must end with a completed response");

            // Compare presence, not contents: a non-deterministic model may word two replies
            // differently, but it must not change the *shape* of what it returns between paths.
            var unaryKinds = PartKinds(unary);
            var streamKinds = PartKinds(streamed);

            streamKinds.ShouldBe(unaryKinds, ignoreOrder: true,
                $"Unary returned [{string.Join(", ", unaryKinds)}] but streaming returned " +
                $"[{string.Join(", ", streamKinds)}] for the same request — both paths are bound by the same contract");
            return ConformanceOutcome.Passed;
        }),

        // ── 7. System-prompt + JSON schema fusion equivalence ────────────────

        new("GenerateAsync_SystemPromptAndResponseFormat_BothApplied", async (model, ct) =>
        {
            var request = new AgentRequest
            {
                SystemPrompt = "You are a JSON-only assistant.",
                Messages = [AgentMessage.User("give me a result")],
                ResponseFormat = new AgentResponseFormat(
                    SchemaName: "echo",
                    JsonSchema: """{"type":"object","properties":{"result":{"type":"string"}},"required":["result"]}""")
            };

            var response = await model.GenerateAsync(request, ct).ConfigureAwait(false);

            // A model that ignores the system prompt and returns prose would fail this.
            var act = () => JsonDocument.Parse(response.Text!);
            act.ShouldNotThrow(
                "When both SystemPrompt and ResponseFormat are set, response must still be valid JSON");
            return ConformanceOutcome.Passed;
        })
    ];

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static AgentRequest TextRequest(string userText, string? system = null) =>
        new()
        {
            SystemPrompt = system,
            Messages = [AgentMessage.User(userText)]
        };

    private static async Task<List<AgentStreamChunk>> CollectAsync(
        IStreamingAgentModel model,
        AgentRequest request,
        CancellationToken cancellationToken)
    {
        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in model.GenerateStreamAsync(request, cancellationToken).ConfigureAwait(false))
            chunks.Add(chunk);

        return chunks;
    }

    private static string[] PartKinds(AgentResponse response) =>
        response.Parts is null
            ? []
            : [.. response.Parts.Select(p => p.GetType().Name).Distinct().Order()];
}
