using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;

namespace Ananke.Federation.Google.AgentRuntime;

/// <summary>
/// Production implementation of <see cref="IAgentRuntimeClient"/> that calls the
/// Gemini Enterprise Agent Platform REST API using Application Default Credentials.
/// </summary>
/// <remarks>
/// All API calls are automatically retried up to <see cref="MaxRetries"/> times with
/// exponential back-off on transient HTTP failures (5xx, 429, or network errors).
/// </remarks>
internal sealed class AgentRuntimeClient : IAgentRuntimeClient
{
    private static readonly HttpClient Http = new();
    private const string BaseUrl = "https://agentplatform.googleapis.com/v1beta1";
    private const int MaxRetries = 3;
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(1);

    private readonly string _project;
    private readonly string _location;

    internal AgentRuntimeClient(string project, string location)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        _project = project;
        _location = location;
    }

    /// <inheritdoc />
    public async Task<string> CreateAgentAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var url = $"{BaseUrl}/projects/{_project}/locations/{_location}/agents";
        var body = BuildCreateBody(definition);

        var response = await ExecuteWithRetryAsync(
            () => BuildRequestAsync(HttpMethod.Post, url, body, ct), ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        // Agent Runtime returns { "name": "projects/.../agents/<id>", ... }
        if (doc.RootElement.TryGetProperty("name", out var nameProp))
            return nameProp.GetString()
                ?? throw new InvalidOperationException("Agent Runtime returned an empty resource name.");

        throw new InvalidOperationException(
            $"Agent Runtime response did not contain a 'name' field. Response: {json}");
    }

    /// <inheritdoc />
    public async Task DeleteAgentAsync(string resourceName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        var url = $"{BaseUrl}/{resourceName}";
        var response = await ExecuteWithRetryAsync(
            () => BuildRequestAsync(HttpMethod.Delete, url, body: null, ct), ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<HttpResponseMessage> ExecuteWithRetryAsync(
        Func<Task<HttpRequestMessage>> buildRequest,
        CancellationToken ct)
    {
        var delay = InitialRetryDelay;
        HttpResponseMessage? response = null;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var request = await buildRequest().ConfigureAwait(false);

            try
            {
                response = await Http.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < MaxRetries)
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * 2);
                continue;
            }

            if (attempt < MaxRetries &&
                ((int)response.StatusCode == 429 ||
                 (int)response.StatusCode >= 500))
            {
                response.Dispose();
                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * 2);
                continue;
            }

            return response;
        }

        return response!;
    }

    private static async Task<HttpRequestMessage> BuildRequestAsync(
        HttpMethod method, string url, string? body, CancellationToken ct)
    {
        var credential = await GoogleCredential
            .GetApplicationDefaultAsync(ct)
            .ConfigureAwait(false);
        var scoped = credential.CreateScoped("https://www.googleapis.com/auth/cloud-platform");
        var token = await scoped.UnderlyingCredential
            .GetAccessTokenForRequestAsync(cancellationToken: ct)
            .ConfigureAwait(false);

        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static string BuildCreateBody(AgentDefinition definition)
    {
        var toolsArray = definition.Tools
            .Select(t => new
            {
                functionDeclarations = t.FunctionDeclarations?
                    .Select(fd => new { name = fd.Name, description = fd.Description })
                    .ToArray()
            })
            .ToArray();

        var body = new
        {
            displayName = definition.DisplayName,
            model = definition.Model,
            systemInstructions = definition.SystemInstructions,
            tools = toolsArray
        };

        return JsonSerializer.Serialize(body, new JsonSerializerOptions { WriteIndented = false });
    }
}
