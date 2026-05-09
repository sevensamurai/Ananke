using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ananke.Federation.Anthropic;

/// <summary>
/// Thin HTTP client for the Anthropic Claude Managed Agents Beta API.
/// Covers the <c>/v1/agents</c> and <c>/v1/environments</c> endpoints.
/// </summary>
/// <remarks>
/// <para>
/// All requests carry the required <c>anthropic-beta: agents-2025-05-14</c> header
/// and the configured API key as <c>x-api-key</c>.
/// </para>
/// <para>
/// This client is intentionally minimal — it does not depend on the official Anthropic
/// .NET SDK's managed-agents surface (which is in Beta) and can be updated independently
/// as the API stabilises.
/// </para>
/// <para>
/// Status: <b>Preview</b> — pinned to Beta header <c>agents-2025-05-14</c>.
/// </para>
/// </remarks>
public sealed class ClaudeManagedAgentsClient : IDisposable
{
    internal const string AgentsBetaHeader = "agents-2025-05-14";
    internal const string BaseUrl = "https://api.anthropic.com";
    internal const string AnthropicVersionHeader = "2023-06-01";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    /// <summary>
    /// Creates a client using an internally managed <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="apiKey">Anthropic API key (<c>sk-ant-…</c>).</param>
    public ClaudeManagedAgentsClient(string apiKey) : this(apiKey, BuildDefaultClient(apiKey), ownsClient: true) { }

    /// <summary>
    /// Creates a client using a caller-supplied <see cref="HttpClient"/>. Used in tests.
    /// </summary>
    internal ClaudeManagedAgentsClient(string apiKey, HttpClient httpClient, bool ownsClient = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(httpClient);
        _http = httpClient;
        _ownsClient = ownsClient;
    }

    // ── Environments ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an environment (container template) for a workflow.
    /// <c>POST /v1/environments</c>
    /// </summary>
    /// <param name="name">Human-readable name for the environment.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The Anthropic-assigned environment ID.</returns>
    public async Task<string> CreateEnvironmentAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var body = new JsonObject { ["name"] = name };
        var response = await PostAsync("/v1/environments", body, ct);
        return ExtractId(response, "environment");
    }

    /// <summary>
    /// Deletes an environment.
    /// <c>DELETE /v1/environments/{id}</c>
    /// </summary>
    public async Task DeleteEnvironmentAsync(string environmentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        await DeleteAsync($"/v1/environments/{Uri.EscapeDataString(environmentId)}", ct);
    }

    // ── Agents ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a managed agent for a workflow job.
    /// <c>POST /v1/agents</c>
    /// </summary>
    /// <param name="name">Agent name (e.g. <c>"{workflow}-{jobName}"</c>).</param>
    /// <param name="model">Claude model identifier.</param>
    /// <param name="systemPrompt">Compiled system prompt.</param>
    /// <param name="tools">Tool definitions JSON array.</param>
    /// <param name="environmentId">Optional environment to associate the agent with.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The Anthropic-assigned agent ID.</returns>
    public async Task<string> CreateAgentAsync(
        string name,
        string model,
        string systemPrompt,
        JsonArray tools,
        string? environmentId = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentNullException.ThrowIfNull(tools);

        var body = new JsonObject
        {
            ["name"] = name,
            ["model"] = model,
            ["system"] = systemPrompt,
            ["tools"] = tools.DeepClone()
        };

        if (environmentId is not null)
            body["environment_id"] = environmentId;

        var response = await PostAsync("/v1/agents", body, ct);
        return ExtractId(response, "agent");
    }

    /// <summary>
    /// Deletes a managed agent.
    /// <c>DELETE /v1/agents/{id}</c>
    /// </summary>
    public async Task DeleteAgentAsync(string agentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        await DeleteAsync($"/v1/agents/{Uri.EscapeDataString(agentId)}", ct);
    }

    // ── Validation round-trip ─────────────────────────────────────────────────

    /// <summary>
    /// Performs a cheap <c>GET /v1/models</c> call to confirm the API key is accepted.
    /// </summary>
    /// <returns><see langword="true"/> if the server responds with HTTP 200; otherwise <see langword="false"/>.</returns>
    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("/v1/models", ct);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsClient)
            _http.Dispose();
    }

    // ── private helpers ───────────────────────────────────────────────────────

    private async Task<JsonObject> PostAsync(string path, JsonObject body, CancellationToken ct)
    {
        var json = body.ToJsonString(JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(path, content, ct);
        return await ReadResponseAsync(response, ct);
    }

    private async Task DeleteAsync(string path, CancellationToken ct)
    {
        var response = await _http.DeleteAsync(path, ct);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            await ThrowApiExceptionAsync(response, ct);
    }

    private static async Task<JsonObject> ReadResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var code = (int)response.StatusCode;
            string? apiMessage = null;
            try
            {
                var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var err) &&
                    err.TryGetProperty("message", out var msg))
                    apiMessage = msg.GetString();
            }
            catch { /* ignore parse failures — use raw body */ }

            throw new HttpRequestException(
                $"Anthropic API error {code}: {apiMessage ?? body}",
                inner: null,
                statusCode: response.StatusCode);
        }

        return JsonNode.Parse(body) as JsonObject
            ?? throw new InvalidOperationException("Anthropic API returned a non-object JSON response.");
    }

    private static async Task ThrowApiExceptionAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            $"Anthropic API error {(int)response.StatusCode}: {body}",
            inner: null,
            statusCode: response.StatusCode);
    }

    private static string ExtractId(JsonObject obj, string resourceKind)
    {
        if (obj.TryGetPropertyValue("id", out var idNode) && idNode?.GetValue<string>() is { Length: > 0 } id)
            return id;

        throw new InvalidOperationException(
            $"Anthropic API response for {resourceKind} did not contain an 'id' field. " +
            $"Response keys: {string.Join(", ", obj.Select(kv => kv.Key))}");
    }

    private static HttpClient BuildDefaultClient(string apiKey)
    {
        var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersionHeader);
        client.DefaultRequestHeaders.Add("anthropic-beta", AgentsBetaHeader);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
