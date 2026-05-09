using System.Runtime.CompilerServices;
using System.Text.Json;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Tools;

namespace Ananke.Orchestration.Conformance.Tests;

/// <summary>
/// Reference <see cref="IStreamingAgentModel"/> used by the conformance suite as the
/// default subject-under-test.  It is deterministic, in-process, and exercises every
/// code path the suite cares about:
/// <list type="bullet">
///   <item>Text deltas (one word per yield)</item>
///   <item>Tool calls — echoes back the first tool on the request</item>
///   <item>Structured output — returns a minimal JSON blob that satisfies the schema</item>
///   <item>Multimodal — returns a <see cref="TextPart"/> wrapped in <see cref="ContentPart"/> list</item>
///   <item>Token usage — always reports deterministic values</item>
/// </list>
/// </summary>
public sealed class FakeConformanceModel : IStreamingAgentModel
{
    // Deterministic token counts make usage-accounting tests reliable.
    public const int FakeInputTokens  = 10;
    public const int FakeOutputTokens = 5;

    public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(BuildResponse(request));
    }

    public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var response = BuildResponse(request);

        if (response.Text is { } text)
        {
            foreach (var word in text.Split(' '))
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return new AgentStreamChunk { TextDelta = word + " " };
            }
        }

        yield return new AgentStreamChunk { CompletedResponse = response };
    }

    // ── Internal response builder ─────────────────────────────────────────

    private static AgentResponse BuildResponse(AgentRequest request)
    {
        // Structured output path
        if (request.ResponseFormat is { } fmt)
        {
            var minimalJson = BuildMinimalJson(fmt.JsonSchema);
            return new AgentResponse
            {
                Text = minimalJson,
                Usage = MakeUsage()
            };
        }

        // Tool call path — echo back the first tool on the request
        if (request.Tools is { Count: > 0 } tools)
        {
            return new AgentResponse
            {
                ToolCalls =
                [
                    new AgentToolCall(
                        Id: $"call_{tools[0].Name}_0",
                        FunctionName: tools[0].Name,
                        Arguments: "{}")
                ],
                Usage = MakeUsage()
            };
        }

        // Multimodal path — when request carries image content parts
        var hasImagePart = request.Messages
            .SelectMany(m => m.Parts ?? [])
            .Any(p => p is ImagePart);

        if (hasImagePart)
        {
            return new AgentResponse
            {
                Parts = [new TextPart("image acknowledged")],
                Usage = MakeUsage()
            };
        }

        // Default: plain text echo
        var userText = request.Messages
            .LastOrDefault(m => m.Role == AgentRole.User)?.Content ?? "ok";

        return new AgentResponse
        {
            Text = $"echo: {userText}",
            Usage = MakeUsage()
        };
    }

    private static TokenUsage MakeUsage() => new()
    {
        InputTokens  = FakeInputTokens,
        OutputTokens = FakeOutputTokens
    };

    /// <summary>
    /// Produces the simplest possible JSON value that will not parse as null for the
    /// given schema string — used so the structured-output tests can round-trip.
    /// </summary>
    private static string BuildMinimalJson(string jsonSchema)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonSchema);
            if (doc.RootElement.TryGetProperty("type", out var typeEl)
                && typeEl.GetString() == "object")
            {
                return """{"result":"ok"}""";
            }
        }
        catch (JsonException) { /* fall through */ }

        return """{"result":"ok"}""";
    }
}
