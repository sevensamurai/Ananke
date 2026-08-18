using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ananke.Abstractions.Distributed;

using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents.Middleware;

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
/// <c>SystemPrompt</c>, <c>Messages</c>, <c>Tools</c>, <c>ResponseFormat</c>, and the model's
/// <c>cacheScope</c>. <c>Metadata</c> and <c>StoreCompletions</c> are excluded because
/// they do not affect model output.
/// </para>
/// <para>
/// <b>Instances wrapping different models must use distinct <c>cacheScope</c> values</b>
/// when they share one <see cref="IKeyValueDataAdapter"/> (the normal case with a single Redis) —
/// otherwise identical prompts sent to different models collide on the same cache key and each
/// silently serves the other's cached response. The default (<c>inner.GetType().FullName</c>)
/// covers the different-provider case (an OpenAI-backed wrapper and an Anthropic-backed wrapper
/// never collide) but does <b>not</b> cover two instances of the <i>same</i> provider class
/// configured with different model names (e.g. a Haiku-backed classifier and an Opus-backed
/// writer both using <c>AnthropicAgentModel</c>) — callers in that situation must pass an
/// explicit <c>cacheScope</c>, typically the model name itself.
/// </para>
/// </remarks>
public sealed class CachingAgentModel : IStreamingAgentModel
{
    private readonly IStreamingAgentModel _inner;
    private readonly IKeyValueDataAdapter _cache;
    private readonly TimeSpan _ttl;
    private readonly string _keyPrefix;
    private readonly string _cacheScope;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates a caching wrapper around an existing streaming agent model.
    /// </summary>
    /// <param name="inner">The model to delegate to on cache misses.</param>
    /// <param name="cache">Key-value store used for caching (e.g. <c>RedisDataAdapter</c>).</param>
    /// <param name="ttl">How long cached responses remain valid. Expired entries are treated as misses.</param>
    /// <param name="keyPrefix">Optional prefix for cache keys. Defaults to <c>"ananke:llm-cache"</c>.</param>
    /// <param name="cacheScope">
    /// Identity mixed into the cache key so wrappers around different models don't collide.
    /// Defaults to <c>inner.GetType().FullName</c> — sufficient when each wrapped provider class
    /// is used for exactly one model, but callers wrapping the same provider class with different
    /// model names (see remarks) must pass a distinct value, typically the model name.
    /// </param>
    /// <param name="timeProvider">
    /// Clock used for cache expiry. Defaults to <see cref="TimeProvider.System"/>; inject a fake
    /// in tests to assert TTL behavior without sleeping.
    /// </param>
    public CachingAgentModel(
        IStreamingAgentModel inner,
        IKeyValueDataAdapter cache,
        TimeSpan ttl,
        string keyPrefix = "ananke:llm-cache",
        string? cacheScope = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);

        _inner = inner;
        _cache = cache;
        _ttl = ttl;
        _keyPrefix = keyPrefix;
        _cacheScope = cacheScope ?? inner.GetType().FullName!;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default)
    {
        var cacheKey = BuildCacheKey(request);
        var cached = await TryGetCachedAsync(cacheKey, ct).ConfigureAwait(false);
        if (cached is not null)
            return cached;

        var response = await _inner.GenerateAsync(request, ct).ConfigureAwait(false);

        if (!response.RequiresAction)
            await CacheResponseAsync(cacheKey, response, ct).ConfigureAwait(false);

        return response;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var cacheKey = BuildCacheKey(request);
        var cached = await TryGetCachedAsync(cacheKey, ct).ConfigureAwait(false);
        if (cached is not null)
        {
            if (cached.Text is not null)
                yield return new AgentStreamChunk { TextDelta = cached.Text };

            yield return new AgentStreamChunk { CompletedResponse = cached };
            yield break;
        }

        AgentResponse? completed = null;
        await foreach (var chunk in _inner.GenerateStreamAsync(request, ct).ConfigureAwait(false))
        {
            if (chunk.CompletedResponse is not null)
                completed = chunk.CompletedResponse;

            yield return chunk;
        }

        if (completed is not null && !completed.RequiresAction)
            await CacheResponseAsync(cacheKey, completed, ct).ConfigureAwait(false);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private string BuildCacheKey(AgentRequest request)
    {
        var hashInput = JsonSerializer.Serialize(new
        {
            _cacheScope,
            request.SystemPrompt,
            request.Messages,
            request.Tools,
            request.ResponseFormat
        }, JsonOptions);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        return $"{_keyPrefix}:{Convert.ToHexStringLower(hash)}";
    }

    private async Task<AgentResponse?> TryGetCachedAsync(string cacheKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var json = await _cache.GetValueAsync(cacheKey, ct).ConfigureAwait(false);
        if (json is null)
            return null;

        var entry = JsonSerializer.Deserialize<CacheEntry>(json, JsonOptions);
        if (entry is null || _timeProvider.GetUtcNow() >= entry.ExpiresAt)
        {
            try { await _cache.RemoveAsync(cacheKey, ct).ConfigureAwait(false); }
            catch (Exception) { /* best-effort cleanup — errors logged by the adapter */ }
            return null;
        }

        return entry.Response;
    }

    private async Task CacheResponseAsync(string cacheKey, AgentResponse response, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var entry = new CacheEntry
        {
            Response = response,
            ExpiresAt = _timeProvider.GetUtcNow().Add(_ttl)
        };

        var json = JsonSerializer.Serialize(entry, JsonOptions);
        await _cache.SetValueAsync(cacheKey, json, ct).ConfigureAwait(false);
    }

    private sealed record CacheEntry
    {
        public required AgentResponse Response { get; init; }
        public required DateTimeOffset ExpiresAt { get; init; }
    }
}
