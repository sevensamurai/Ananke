using System.Net.Http.Headers;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;

namespace Ananke.Federation.Google.Observability;

/// <summary>
/// Production implementation of <see cref="IAgentObservabilityClient"/> that queries
/// Cloud Trace v2 (for per-invocation traces) and Cloud Monitoring v3 (for aggregated
/// execution metrics) via Application Default Credentials.
/// </summary>
internal sealed class AgentObservabilityClient : IAgentObservabilityClient
{
    private static readonly HttpClient Http = new();
    private const string TraceBaseUrl = "https://cloudtrace.googleapis.com/v2";
    private const string MonitoringBaseUrl = "https://monitoring.googleapis.com/v3";
    private const string Scope = "https://www.googleapis.com/auth/cloud-platform";

    private readonly string _project;

    internal AgentObservabilityClient(string project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        _project = project;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TraceRecord>> GetTracesAsync(
        string deploymentId,
        int lookBackMinutes,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentId);

        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-lookBackMinutes);
        var filter = Uri.EscapeDataString(
            $"+labels.\"deployment_id\"=\"{deploymentId}\" " +
            $"startTime>\"{cutoff:o}\"");

        var url = $"{TraceBaseUrl}/projects/{_project}/traces?filter={filter}&pageSize=200";
        var json = await GetAsync(url, ct);

        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("traces", out var tracesEl))
            return [];

        var results = new List<TraceRecord>();
        foreach (var trace in tracesEl.EnumerateArray())
        {
            if (!trace.TryGetProperty("spans", out var spans))
                continue;

            foreach (var span in spans.EnumerateArray())
            {
                if (!TryParseSpan(span, deploymentId, out var record))
                    continue;
                results.Add(record!);
            }
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<ObservabilitySnapshot?> GetMetricsSnapshotAsync(
        string deploymentId,
        int lookBackMinutes,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentId);

        var endTime = DateTimeOffset.UtcNow;
        var startTime = endTime.AddMinutes(-lookBackMinutes);

        // Query the agent_platform/agent/execution_count timeseries
        var filter = Uri.EscapeDataString(
            $"metric.type=\"agentplatform.googleapis.com/agent/execution_count\" " +
            $"resource.labels.deployment_id=\"{deploymentId}\"");

        var url = $"{MonitoringBaseUrl}/projects/{_project}/timeSeries" +
                  $"?filter={filter}" +
                  $"&interval.startTime={startTime:o}" +
                  $"&interval.endTime={endTime:o}" +
                  $"&aggregation.alignmentPeriod=3600s" +
                  $"&aggregation.perSeriesAligner=ALIGN_SUM";

        var json = await GetAsync(url, ct);

        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("timeSeries", out var series) ||
            series.GetArrayLength() == 0)
            return null;

        long executions = 0, tokens = 0, toolCalls = 0;
        long errors = 0;

        foreach (var ts in series.EnumerateArray())
        {
            var metricType = ts
                .GetProperty("metric")
                .GetProperty("type")
                .GetString() ?? string.Empty;

            var pointSum = ts.TryGetProperty("points", out var pts)
                ? pts.EnumerateArray()
                    .Sum(p => p.TryGetProperty("value", out var v) &&
                              v.TryGetProperty("int64Value", out var iv)
                        ? long.Parse(iv.GetString() ?? "0")
                        : 0L)
                : 0L;

            if (metricType.Contains("execution_count"))   executions = pointSum;
            else if (metricType.Contains("token_count"))  tokens     = pointSum;
            else if (metricType.Contains("tool_calls"))   toolCalls  = pointSum;
            else if (metricType.Contains("error_count"))  errors     = pointSum;
        }

        return new ObservabilitySnapshot
        {
            ExecutionCount = executions,
            TotalTokens    = tokens,
            ToolCallCount  = toolCalls,
            ErrorRate      = executions > 0 ? (double)errors / executions : 0.0
        };
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static bool TryParseSpan(
        JsonElement span, string deploymentId, out TraceRecord? record)
    {
        record = null;

        if (!span.TryGetProperty("startTime", out var st) ||
            !DateTimeOffset.TryParse(st.GetString(), out var startTime))
            return false;

        double latencyMs = 0;
        if (span.TryGetProperty("endTime", out var et) &&
            DateTimeOffset.TryParse(et.GetString(), out var endTime))
            latencyMs = (endTime - startTime).TotalMilliseconds;

        var isError = span.TryGetProperty("status", out var status) &&
                      status.TryGetProperty("code", out var code) &&
                      code.GetInt32() != 0;

        record = new TraceRecord
        {
            StartTime = startTime,
            LatencyMs = latencyMs,
            IsError   = isError
        };
        return true;
    }

    private async Task<string> GetAsync(string url, CancellationToken ct)
    {
        var credential = await GoogleCredential
            .GetApplicationDefaultAsync(ct)
            .ConfigureAwait(false);
        var scoped = credential.CreateScoped(Scope);
        var token = await scoped.UnderlyingCredential
            .GetAccessTokenForRequestAsync(cancellationToken: ct)
            .ConfigureAwait(false);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}
