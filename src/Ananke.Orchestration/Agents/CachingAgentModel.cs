using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ananke.Abstractions.Distributed;

using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents;

/// <summary>
/// Decorator that caches non-streaming <see cref="AgentResponse"/> results via
/// <see cref="IKeyValueDataAdapter"/>. Streaming calls delegate to the inner model
/// and cache the final assembled response for subsequent non-streaming hits.
/// </summary>
/// <remarks>
/// <para>
/// Responses where <see cref="AgentResponse.RequiresAction"/> is <c>true</c> (tool-call
/// responses) are never cached because the tool results depend on external state that
/// may change between calls.
/// </para>
/// <para>
/// The cache key is a SHA256 hash of the semantically-relevant parts of the request:
/// <c>SystemPrompt</c>, <c>Messages</c>, <c>Tools</c>, and <c>ResponseFormat</c>.
/// <c>Metadata</c> and <c>StoreCompletions</c> are excluded because they do not
/// affect model output.
/// </para>
/// </remarks>
public sealed class CachingAgentModel : IStreamingAgentModel
{
    private readonly IStreamingAgentModel _inner;
    private readonly IKeyValueDataAdapter _cache;
    private readonly TimeSpan _ttl;
    private readonly string _keyPrefix;

    /// <summary>
    /// Creates a caching wrapper around an existing streaming agent model.
    /// </summary>
    /// <param name="inner">The model to delegate to on cache misses.</param>
    /// <param name="cache">Key-value store used for caching (e.g. <c>RedisDataAdapter</c>).</param>
    /// <param name="ttl">How long cached responses remain valid. Expired entries are treated as misses.</param>
    /// <param name="keyPrefix">Optional prefix for cache keys. Defaults to <c>"ananke:llm-cache"</c>.</param>
    public CachingAgentModel(
        IStreamingAgentModel inner,
        IKeyValueDataAdapter cache,
        TimeSpan ttl,
        string keyPrefix = "ananke:llm-cache")
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);

        _inner = inner;
        _cache = cache;
        _ttl = ttl;
        _keyPrefix = keyPrefix;
    }

    /// <inheritdoc />
    public async Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
    {
        var cacheKey = BuildCacheKey(request);
        var cached = await TryGetCachedAsync(cacheKey);
        if (cached is not null)
            return cached;

        var response = await _inner.GenerateAsync(request, ct);

        if (!response.RequiresAction)
            await CacheResponseAsync(cacheKey, response);

        return response;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var cacheKey = BuildCacheKey(request);
        var cached = await TryGetCachedAsync(cacheKey);
        if (cached is not null)
        {
            if (cached.Text is not null)
                yield return new AgentStreamChunk { TextDelta = cached.Text };

            yield return new AgentStreamChunk { CompletedResponse = cached };
            yield break;
        }

        AgentResponse? completed = null;
        await foreach (var chunk in _inner.GenerateStreamAsync(request, ct))
        {
            if (chunk.CompletedResponse is not null)
                completed = chunk.CompletedResponse;

            yield return chunk;
        }

        if (completed is not null && !completed.RequiresAction)
            await CacheResponseAsync(cacheKey, completed);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private string BuildCacheKey(AgentRequest request)
    {
        var hashInput = JsonSerializer.Serialize(new
        {
            request.SystemPrompt,
            request.Messages,
            request.Tools,
            request.ResponseFormat
        }, JsonOptions);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        return $"{_keyPrefix}:{Convert.ToHexStringLower(hash)}";
    }

    private async Task<AgentResponse?> TryGetCachedAsync(string cacheKey)
    {
        var json = await _cache.GetValueAsync(cacheKey);
        if (json is null)
            return null;

        var entry = JsonSerializer.Deserialize<CacheEntry>(json, JsonOptions);
        if (entry is null || DateTimeOffset.UtcNow >= entry.ExpiresAt)
        {
            try { await _cache.RemoveAsync(cacheKey); }
            catch { /* best-effort cleanup — errors logged by the adapter */ }
            return null;
        }

        return entry.Response;
    }

    private async Task CacheResponseAsync(string cacheKey, AgentResponse response)
    {
        var entry = new CacheEntry
        {
            Response = response,
            ExpiresAt = DateTimeOffset.UtcNow.Add(_ttl)
        };

        var json = JsonSerializer.Serialize(entry, JsonOptions);
        await _cache.SetValueAsync(cacheKey, json);
    }

    private sealed record CacheEntry
    {
        public required AgentResponse Response { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
    }
}
