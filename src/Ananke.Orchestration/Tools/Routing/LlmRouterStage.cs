using System.Text.Json;
using System.Text.Json.Serialization;
using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tools.Routing;
using Ananke.Orchestration.Agents;

using Ananke.Orchestration.Usage;

namespace Ananke.Orchestration.Tools.Routing;

/// <summary>
/// Routing stage that delegates the selection decision to a cheap <see cref="IAgentModel"/>.
/// </summary>
/// <remarks>
/// <para>
/// The stage builds a structured prompt via an <see cref="IRoutingPromptTemplate"/>,
/// calls the model with <b>no tools</b>, then parses the JSON response into a
/// <see cref="ToolRoutingDecision"/>.
/// </para>
/// <para>
/// Tool names the model "invents" (not present in the candidates) are silently dropped
/// to enforce the subset invariant without throwing.
/// </para>
/// <para>
/// On parse failure the stage retries once with a corrective prompt. If the second
/// attempt also fails it returns <see cref="RoutingConfidence.Low"/> with the original
/// candidates so the <see cref="CompositeSmartToolRouter"/> escalates rather than
/// regresses.
/// </para>
/// </remarks>
public sealed class LlmRouterStage : ISmartToolRouter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IAgentModel _cheapModel;
    private readonly IRoutingPromptTemplate _template;
    private readonly int _maxRetries;

    /// <summary>Creates the stage.</summary>
    /// <param name="cheapModel">
    /// A fast, low-cost model used for routing decisions. Must not be the same
    /// frontier model that will consume the narrowed tool window.
    /// </param>
    /// <param name="template">
    /// Prompt template. When <see langword="null"/> a <see cref="DefaultRoutingPromptTemplate"/>
    /// is used.
    /// </param>
    /// <param name="maxRetries">
    /// Number of additional retry attempts on parse failure. Defaults to 1.
    /// </param>
    public LlmRouterStage(
        IAgentModel cheapModel,
        IRoutingPromptTemplate? template = null,
        int maxRetries = 1)
    {
        ArgumentNullException.ThrowIfNull(cheapModel);
        _cheapModel = cheapModel;
        _template = template ?? new DefaultRoutingPromptTemplate();
        _maxRetries = maxRetries;
    }

    /// <inheritdoc />
    public async Task<ToolRoutingDecision> RouteAsync(
        ToolRoutingRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var systemPrompt = _template.RenderSystemPrompt(request);
        var userPrompt = _template.RenderUserPrompt(request);

        var agentRequest = new AgentRequest
        {
            SystemPrompt = systemPrompt,
            Messages = [AgentMessage.User(userPrompt)],
            // Explicitly no tools — this is a routing-only call
            Tools = null,
        };

        var response = await _cheapModel.GenerateAsync(agentRequest, ct).ConfigureAwait(false);
        await UsageRecording.ReportAsync(response, ct).ConfigureAwait(false);
        var rawText = response.Text ?? string.Empty;

        if (TryParse(rawText, request, out var decision))
            return decision;

        // ── Retry ────────────────────────────────────────────────────
        for (var i = 0; i < _maxRetries; i++)
        {
            var retrySystemPrompt = _template.RenderRetrySystemPrompt(request, rawText);
            var retryRequest = agentRequest with { SystemPrompt = retrySystemPrompt };

            response = await _cheapModel.GenerateAsync(retryRequest, ct).ConfigureAwait(false);
            await UsageRecording.ReportAsync(response, ct).ConfigureAwait(false);
            rawText = response.Text ?? string.Empty;

            if (TryParse(rawText, request, out decision))
                return decision;
        }

        // ── Escalate on total failure ─────────────────────────────────
        return new ToolRoutingDecision
        {
            UseTools = true,
            SelectedTools = request.Candidates,
            Confidence = RoutingConfidence.Low,
            Rationale = "LLM router failed to produce a parseable response — escalating",
        };
    }

    // ── Parsing internals ─────────────────────────────────────────────

    private static bool TryParse(
        string raw,
        ToolRoutingRequest request,
        out ToolRoutingDecision decision)
    {
        decision = null!;

        var json = StripMarkdownFences(raw);
        if (string.IsNullOrWhiteSpace(json))
            return false;

        LlmRoutingPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<LlmRoutingPayload>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (payload is null)
            return false;

        // Map confidence string → enum (lenient)
        var confidence = payload.Confidence?.ToLowerInvariant() switch
        {
            "high" => RoutingConfidence.High,
            "medium" => RoutingConfidence.Medium,
            _ => RoutingConfidence.Low,
        };

        // Intersect selectedToolNames with current candidates (subset invariant).
        // Tools the LLM invented are silently dropped.
        IReadOnlyList<ToolMemoryEntry> selected;
        if (!payload.UseTools)
        {
            selected = [];
        }
        else
        {
            var nameSet = new HashSet<string>(
                payload.SelectedToolNames ?? [],
                StringComparer.OrdinalIgnoreCase);

            selected = request.Candidates
                .Where(e => nameSet.Contains(e.ToolName))
                .ToList();

            // 4.4: useTools=true with an empty selected list is ambiguous — the model
            // voted for tools but named none we recognise.  Escalate to Low so the
            // composite router can widen the window rather than proceeding with nothing.
            if (selected.Count == 0)
            {
                decision = new ToolRoutingDecision
                {
                    UseTools = true,
                    SelectedTools = request.Candidates,
                    Confidence = RoutingConfidence.Low,
                    Rationale = "useTools=true but no recognisable tools selected — escalating to low-confidence",
                };
                return true;
            }
        }

        decision = new ToolRoutingDecision
        {
            UseTools = payload.UseTools,
            SelectedTools = selected,
            Confidence = confidence,
            Rationale = payload.Rationale,
        };
        return true;
    }

    /// <summary>
    /// Strips optional Markdown code fences (```json … ```) that models sometimes emit.
    /// </summary>
    private static string StripMarkdownFences(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
                trimmed = trimmed[(firstNewline + 1)..];

            if (trimmed.EndsWith("```", StringComparison.Ordinal))
                trimmed = trimmed[..^3].TrimEnd();
        }
        return trimmed;
    }

    // ── Private DTO ───────────────────────────────────────────────────

    private sealed class LlmRoutingPayload
    {
        [JsonPropertyName("useTools")]
        public bool UseTools { get; init; }

        [JsonPropertyName("selectedToolNames")]
        public IReadOnlyList<string>? SelectedToolNames { get; init; }

        [JsonPropertyName("confidence")]
        public string? Confidence { get; init; }

        [JsonPropertyName("rationale")]
        public string? Rationale { get; init; }
    }
}
