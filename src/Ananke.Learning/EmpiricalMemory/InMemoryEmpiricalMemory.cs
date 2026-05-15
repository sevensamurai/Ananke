using Ananke.Abstractions;
using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Graph;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Catalog;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Ananke.Learning.EmpiricalMemory;

/// <summary>
/// In-memory <see cref="IEmpiricalMemory"/> for testing and single-process scenarios.
/// Uses brute-force cosine similarity and client-side composite scoring.
/// </summary>
public sealed class InMemoryEmpiricalMemory : IEmpiricalMemory
{
    private readonly ConcurrentDictionary<string, StoredEntry> _entries = new();
    // 5.6: Per-Kind secondary index — maps Kind → set of entry IDs.
    // Reduces recall scan set from O(all) to O(kind) when a Kind filter is in use.
    private readonly ConcurrentDictionary<EmpiricalKind, HashSet<string>> _kindIndex = new();
    private readonly Lock _writeLock = new();
    private readonly IEmbeddingModel _embedder;
    private readonly float _dedupThreshold;
    private readonly TimeDecayOptions _decayOptions;
    private readonly AffectOptions? _affectOptions;
    private readonly IPredictionSource? _predictionSource;
    private readonly ILogger _logger;
    private readonly int _maxEntries;
    private readonly IEmpiricalGraphProjector? _graphProjector;
    private readonly IKnowledgeGraph? _graph;

    // ── Observability ────────────────────────────────────────────
    private static readonly ActivitySource Activity = new(AnankeSourceNames.EmpiricalMemory);
    private static readonly Meter Meter = new(AnankeSourceNames.EmpiricalMemoryMeter);
    private static readonly Counter<long> CommitCounter = Meter.CreateCounter<long>("empirical.commits", description: "Total entries committed");
    private static readonly Counter<long> DedupCounter = Meter.CreateCounter<long>("empirical.dedup_merges", description: "Entries merged via semantic dedup");
    private static readonly Counter<long> RecallCounter = Meter.CreateCounter<long>("empirical.recalls", description: "Total recall queries");
    private static readonly Counter<long> RecallHitCounter = Meter.CreateCounter<long>("empirical.recall_hits", description: "Recall queries that returned ≥1 result");
    private static readonly Counter<long> ReinforceCounter = Meter.CreateCounter<long>("empirical.reinforcements", description: "Total reinforcements applied");
    private static readonly Counter<long> ContradictCounter = Meter.CreateCounter<long>("empirical.contradictions", description: "Total contradictions applied");

    /// <summary>Maximum evidence entries retained per entry to prevent unbounded growth.</summary>
    private int MaxEvidenceCount => _affectOptions?.MaxEvidenceCount ?? new AffectOptions().MaxEvidenceCount;

    /// <summary>
    /// Creates a new in-memory empirical memory store.
    /// </summary>
    /// <param name="embedder">Embedding model for vectorizing descriptions and queries.</param>
    /// <param name="dedupThreshold">
    /// Cosine similarity threshold above which a new entry is merged into an existing one
    /// of the same <see cref="EmpiricalKind"/>. Default is <c>0.9</c>.
    /// </param>
    /// <param name="decayOptions">
    /// Time-decay options for recency weighting in composite scoring.
    /// Default is 90-day half-life with 0.3 floor.
    /// </param>
    /// <param name="affectOptions">
    /// Optional configuration for affect-driven learning mechanics.
    /// When provided, <see cref="ReinforceAsync"/> uses prediction-error modulated
    /// reinforcement when <see cref="Reinforcement.Reward"/> is supplied, and
    /// <see cref="RecallAsync"/> applies valence/intensity priority boosting.
    /// </param>
    /// <param name="predictionSource">
    /// Optional prediction source for forming predictions before PE computation.
    /// When provided, breaks the confidence-as-prediction circularity by forming
    /// predictions independently. When <c>null</c>, falls back to
    /// <see cref="EmpiricalEntry.Confidence"/> as the prediction.
    /// </param>
    /// <param name="maxEntries">
    /// Maximum number of entries retained in the heap. When the store is at capacity,
    /// the entry with the lowest composite score (confidence × recency) is evicted
    /// before a new entry is written. Default is <c>10_000</c>.
    /// </param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    /// <param name="graph">
    /// Optional knowledge graph. When both <paramref name="graph"/> and
    /// <paramref name="graphProjector"/> are supplied, every committed entry is
    /// projected into the graph immediately after the commit completes.
    /// </param>
    /// <param name="graphProjector">
    /// Strategy that translates an <see cref="EmpiricalEntry"/> into graph nodes/edges.
    /// Ignored when <paramref name="graph"/> is <see langword="null"/>.
    /// </param>
    public InMemoryEmpiricalMemory(
        IEmbeddingModel embedder,
        float dedupThreshold = 0.9f,
        TimeDecayOptions? decayOptions = null,
        AffectOptions? affectOptions = null,
        IPredictionSource? predictionSource = null,
        int maxEntries = 10_000,
        ILogger<InMemoryEmpiricalMemory>? logger = null,
        IKnowledgeGraph? graph = null,
        IEmpiricalGraphProjector? graphProjector = null)
    {
        ArgumentNullException.ThrowIfNull(embedder);
        if (maxEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxEntries), "Must be positive.");
        _embedder = embedder;
        _dedupThreshold = dedupThreshold;
        _decayOptions = decayOptions ?? new TimeDecayOptions { HalfLifeDays = 90, FloorWeight = 0.3f };
        _affectOptions = affectOptions;
        _predictionSource = predictionSource;
        _maxEntries = maxEntries;
        _logger = logger ?? NullLogger<InMemoryEmpiricalMemory>.Instance;
        _graph = graph;
        _graphProjector = graphProjector;
    }

    /// <inheritdoc />
    public async Task<EmpiricalEntry> CommitAsync(EmpiricalEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var span = Activity.StartActivity("EmpiricalMemory.Commit");
        span?.SetTag("empirical.entry_id", entry.Id);
        span?.SetTag("empirical.kind", entry.Kind.ToString());

        var embedding = await _embedder.EmbedAsync(entry.Description.ToEmbeddingText(), ct);

        lock (_writeLock)
        {
            // Semantic dedup: search for a similar existing entry of the same kind and entity
            foreach (var (_, stored) in _entries)
            {
                if (stored.Entry.Kind != entry.Kind)
                    continue;

                if (stored.Entry.EntityId != entry.EntityId)
                    continue;

                var similarity = CosineSimilarity(embedding.Span, stored.Embedding.Span);
                if (similarity >= _dedupThreshold)
                {
                    // Merge into existing: increment observation count and merge evidence.
                    // Do NOT bump confidence — that is the prediction-error path's job.
                    var merged = stored.Entry with
                    {
                        ObservationCount = stored.Entry.ObservationCount + 1,
                        LastObserved = entry.LastObserved > stored.Entry.LastObserved
                            ? entry.LastObserved : stored.Entry.LastObserved,
                        Evidence = TrimEvidence([.. stored.Entry.Evidence, .. entry.Evidence])
                    };

                    _entries[stored.Entry.Id] = new StoredEntry(merged, stored.Embedding);
                    DedupCounter.Add(1);
                    _logger.LogDebug("Empirical dedup: merged '{NewId}' into '{ExistingId}' (similarity: {Similarity:F3})",
                        entry.Id, stored.Entry.Id, similarity);
                    return merged;
                }
            }

            // No duplicate found — evict if at capacity, then store
            if (_entries.Count >= _maxEntries)
                EvictLowestScored();

            _entries[entry.Id] = new StoredEntry(entry, embedding);
            _kindIndex.GetOrAdd(entry.Kind, _ => []).Add(entry.Id);
        }

        CommitCounter.Add(1);
        _logger.LogDebug("Empirical commit: new {Kind} '{Id}' (confidence: {Confidence:F2})",
            entry.Kind, entry.Id, entry.Confidence);

        if (_graph is not null && _graphProjector is not null)
            await (_graphProjector.ProjectAsync(entry, _graph, ct));

        return entry;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmpiricalMatch>> RecallAsync(
        string situation, RecallOptions? options = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(situation);
        options ??= new RecallOptions();

        using var span = Activity.StartActivity("EmpiricalMemory.Recall");

        var queryEmbedding = await _embedder.EmbedAsync(situation, ct);
        var scored = new List<EmpiricalMatch>();

        // 5.6: Use per-Kind index to narrow the scan when a Kind filter is specified.
        IEnumerable<StoredEntry> candidates = options.Kind is not null
            && _kindIndex.TryGetValue(options.Kind.Value, out var kindIds)
            ? kindIds.Select(id => _entries.TryGetValue(id, out var e) ? e : null!).Where(e => e is not null)
            : _entries.Values;

        foreach (var stored in candidates)
        {
            if (!MatchesFilters(stored.Entry, options))
                continue;

            var vectorScore = CosineSimilarity(queryEmbedding.Span, stored.Embedding.Span);
            var recencyWeight = TimeDecay.ComputeWeight(stored.Entry.LastObserved, _decayOptions);
            var compositeScore = vectorScore * stored.Entry.Confidence * recencyWeight;

            if (_affectOptions is not null)
            {
                if (_affectOptions.StrengthHalfLifeDays is { } shl)
                {
                    var elapsedDays = (float)(DateTimeOffset.UtcNow - stored.Entry.LastObserved).TotalDays;
                    compositeScore *= MathF.Pow(2f, -elapsedDays / shl);
                }

                var effectiveValence = MathF.Abs(stored.Entry.Valence);
                if (_affectOptions.ValenceHalfLifeDays is { } vhl)
                {
                    var elapsedDays = (float)(DateTimeOffset.UtcNow - stored.Entry.LastObserved).TotalDays;
                    effectiveValence *= MathF.Pow(2f, -elapsedDays / vhl);
                }

                var priorityBoost = 1f + _affectOptions.MaxPriorityBoost
                                       * stored.Entry.Intensity
                                       * effectiveValence;
                compositeScore *= priorityBoost;
            }

            if (compositeScore >= options.ScoreThreshold)
                scored.Add(new EmpiricalMatch { Entry = stored.Entry, Score = compositeScore });
        }

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));

        var results = scored
            .Take(options.TopK)
            .ToList();

        RecallCounter.Add(1);
        if (results.Count > 0) RecallHitCounter.Add(1);

        span?.SetTag("empirical.recall_count", results.Count);
        _logger.LogDebug("Empirical recall: '{Situation}' → {Count} results",
            situation, results.Count);

        return results;
    }

    /// <inheritdoc />
    public async Task ReinforceAsync(string entryId, Reinforcement reinforcement, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentNullException.ThrowIfNull(reinforcement);

        // Form prediction outside the lock (may involve async I/O)
        float? freshPrediction = null;
        if (reinforcement.Reward is not null && _affectOptions is not null && _predictionSource is not null)
        {
            if (_entries.TryGetValue(entryId, out var peek))
                freshPrediction = await _predictionSource.PredictAsync(peek.Entry, this, ct);
        }

        lock (_writeLock)
        {
            if (!_entries.TryGetValue(entryId, out var stored))
                throw new KeyNotFoundException($"Empirical entry '{entryId}' not found.");

            EmpiricalEntry updated;

            if (reinforcement.Reward is not null && _affectOptions is not null)
            {
                // ── Prediction-error path ───────────────────────────────────────
                float predicted = freshPrediction
                    ?? stored.Entry.Prediction
                    ?? stored.Entry.Confidence;
                float actual = reinforcement.Reward.Value;
                float predictionError = MathF.Abs(predicted - actual);

                // Cooldown
                float hours = (float)(DateTimeOffset.UtcNow - stored.Entry.LastObserved).TotalHours;
                float cooldown = MathF.Min(1f, hours / _affectOptions.ReinforcementCooldownHours);

                // Strength: confirming ≈ +lr, maximally surprising ≈ 0
                float strengthDelta = _affectOptions.LearningRate * (1f - predictionError) * cooldown;

                // Variance: EMA of squared prediction errors
                float a = _affectOptions.VarianceSmoothingFactor;
                float newVariance = (1f - a) * stored.Entry.Variance + a * predictionError * predictionError;

                // Confidence derived from variance
                float newConfidence = 1f / (1f + newVariance);

                // Priority signals: direction and magnitude of surprise (not raw outcome)
                float newValence = Math.Clamp(actual - predicted, -1f, 1f);
                float newIntensity = Math.Clamp(predictionError, 0f, 1f);

                updated = stored.Entry with
                {
                    Strength = MathF.Max(0f, stored.Entry.Strength + strengthDelta),
                    Confidence = newConfidence,
                    Prediction = predicted,
                    Variance = newVariance,
                    Valence = newValence,
                    Intensity = newIntensity,
                    LastPredictionError = predictionError,
                    ObservationCount = stored.Entry.ObservationCount + 1,
                    LastObserved = DateTimeOffset.UtcNow,
                    Evidence = TrimEvidence([.. stored.Entry.Evidence, .. reinforcement.NewEvidence])
                };
            }
            else
            {
                // ── Flat path (backward compatible) ────────────────────────────
                var adjustment = reinforcement.ConfidenceAdjustment
                    ?? (_affectOptions?.DefaultConfidenceAdjustment ?? 0.1f);
                var newStrength = reinforcement.StrengthAdjustment is not null
                    ? MathF.Max(0f, stored.Entry.Strength + reinforcement.StrengthAdjustment.Value)
                    : stored.Entry.Strength;
                updated = stored.Entry with
                {
                    Confidence = Math.Min(1.0f, stored.Entry.Confidence + adjustment),
                    Strength = newStrength,
                    ObservationCount = stored.Entry.ObservationCount + 1,
                    LastObserved = DateTimeOffset.UtcNow,
                    Evidence = TrimEvidence([.. stored.Entry.Evidence, .. reinforcement.NewEvidence])
                };
            }

            _entries[entryId] = new StoredEntry(updated, stored.Embedding);
        }

        ReinforceCounter.Add(1);
        _logger.LogDebug("Empirical reinforce: '{Id}'", entryId);
    }

    /// <inheritdoc />
    public Task ContradictAsync(string entryId, string reason, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        lock (_writeLock)
        {
            if (!_entries.TryGetValue(entryId, out var stored))
                throw new KeyNotFoundException($"Empirical entry '{entryId}' not found.");

            EmpiricalEntry updated;

            if (_affectOptions is not null)
            {
                // Contradiction is maximally surprising negative evidence.
                // Record prediction error = 1.0 (worst case) and weaken strength.
                float a = _affectOptions.VarianceSmoothingFactor;
                float newVariance = (1f - a) * stored.Entry.Variance + a * 1f; // error² = 1.0
                float newConfidence = 1f / (1f + newVariance);

                updated = stored.Entry with
                {
                    Confidence = newConfidence,
                    Strength = MathF.Max(0f, stored.Entry.Strength - _affectOptions.LearningRate * _affectOptions.ContradictionStrengthMultiplier),
                    Variance = newVariance,
                    Valence = Math.Clamp(stored.Entry.Valence + _affectOptions.ContradictionValenceShift, -1f, 1f),
                    Intensity = Math.Clamp(stored.Entry.Intensity + _affectOptions.ContradictionIntensityShift, 0f, 1f),
                    LastPredictionError = 1f,
                    LastObserved = DateTimeOffset.UtcNow,
                    Evidence = TrimEvidence([.. stored.Entry.Evidence, $"contradicted: {reason}"])
                };
            }
            else
            {
                var penalty = _affectOptions?.ContradictionConfidencePenalty
                    ?? new AffectOptions().ContradictionConfidencePenalty;
                updated = stored.Entry with
                {
                    Confidence = Math.Max(0f, stored.Entry.Confidence - penalty),
                    LastObserved = DateTimeOffset.UtcNow,
                    Evidence = TrimEvidence([.. stored.Entry.Evidence, $"contradicted: {reason}"])
                };
            }

            _entries[entryId] = new StoredEntry(updated, stored.Embedding);
        }

        ContradictCounter.Add(1);
        _logger.LogDebug("Empirical contradict: '{Id}' — {Reason}", entryId, reason);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<EmpiricalEntry?> GetAsync(string entryId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        return Task.FromResult(
            _entries.TryGetValue(entryId, out var stored) ? stored.Entry : null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EmpiricalEntry>> BrowseAsync(
        int offset, int limit, EmpiricalKind? kind = null,
        string? entityId = null, CancellationToken ct = default)
    {
        var query = _entries.Values.Select(s => s.Entry).AsEnumerable();
        if (kind is not null)
            query = query.Where(e => e.Kind == kind.Value);
        if (entityId is not null)
            query = query.Where(e => e.EntityId == entityId);

        IReadOnlyList<EmpiricalEntry> result = query
            .Skip(offset)
            .Take(limit)
            .ToList();

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EmpiricalEntry>> BrowseAsync(
        BrowseOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<EmpiricalEntry> result = ApplyBrowseFilters(options)
            .Skip(options.Offset)
            .Take(options.Limit)
            .ToList();

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<int> CountAsync(BrowseOptions? options = null, CancellationToken ct = default)
    {
        if (options is null)
            return Task.FromResult(_entries.Count);

        return Task.FromResult(ApplyBrowseFilters(options).Count());
    }

    /// <inheritdoc />
    public Task MarkConsolidatedAsync(string entryId, string knowledgeDocId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(knowledgeDocId);

        lock (_writeLock)
        {
            if (!_entries.TryGetValue(entryId, out var stored))
                throw new KeyNotFoundException($"Empirical entry '{entryId}' not found.");

            var updated = stored.Entry with { ConsolidatedInto = knowledgeDocId };
            _entries[entryId] = new StoredEntry(updated, stored.Embedding);
        }

        _logger.LogDebug("Empirical consolidated: '{Id}' → '{DocId}'", entryId, knowledgeDocId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EmpiricalMatch>> PairRecallAsync(
        EmpiricalEntry reference,
        PairRecallOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        options ??= new PairRecallOptions();
        var scorer = options.Scorer ?? EmpiricalPairScorers.TagOverlap;

        var results = new List<EmpiricalMatch>();

        foreach (var (_, stored) in _entries)
        {
            ct.ThrowIfCancellationRequested();

            var entry = stored.Entry;

            if (entry.Id == reference.Id)
                continue;

            if (entry.ConsolidatedInto is not null)
                continue;

            if (options.CandidateFilter is not null && !options.CandidateFilter(entry))
                continue;

            var score = scorer(reference, entry);
            if (score >= options.MinScore)
                results.Add(new EmpiricalMatch { Entry = entry, Score = score });
        }

        results.Sort((a, b) => b.Score.CompareTo(a.Score));

        IReadOnlyList<EmpiricalMatch> top = results.Count > options.MaxResults
            ? results.Take(options.MaxResults).ToList()
            : results;

        return Task.FromResult(top);
    }

    /// <summary>Returns the number of entries currently stored.</summary>
    public int Count => _entries.Count;

    private static bool MatchesFilters(EmpiricalEntry entry, RecallOptions options)
    {
        if (entry.ConsolidatedInto is not null)
            return false;

        if (options.EntityId is not null)
        {
            var isMatch = entry.EntityId == options.EntityId;
            var isGlobal = entry.EntityId is null && options.IncludeGlobal;
            if (!isMatch && !isGlobal)
                return false;
        }

        if (options.Kind is not null && entry.Kind != options.Kind)
            return false;

        if (entry.Confidence < options.MinConfidence)
            return false;

        if (options.RequiredTags is { Count: > 0 })
        {
            foreach (var tag in options.RequiredTags)
            {
                if (!entry.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    return false;
            }
        }

        return true;
    }

    private IEnumerable<EmpiricalEntry> ApplyBrowseFilters(BrowseOptions options)
    {
        var query = _entries.Values.Select(s => s.Entry).AsEnumerable();

        if (options.Kind is not null)
            query = query.Where(e => e.Kind == options.Kind.Value);
        if (options.EntityId is not null)
            query = query.Where(e => e.EntityId == options.EntityId);
        if (options.MinConfidence > 0)
            query = query.Where(e => e.Confidence >= options.MinConfidence);
        if (options.ExcludeConsolidated)
            query = query.Where(e => e.ConsolidatedInto is null);
        if (options.RequiredTags is { Count: > 0 })
        {
            foreach (var tag in options.RequiredTags)
                query = query.Where(e => e.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
        }

        return query;
    }

    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var dot = 0f;
        var normA = 0f;
        var normB = 0f;

        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator == 0f ? 0f : dot / denominator;
    }

    /// <summary>Keeps only the most recent evidence entries to prevent unbounded growth.</summary>
    private IReadOnlyList<string> TrimEvidence(IReadOnlyList<string> evidence) =>
        evidence.Count <= MaxEvidenceCount
            ? evidence
            : evidence.Skip(evidence.Count - MaxEvidenceCount).ToList();

    /// <summary>
    /// Evicts the entry with the lowest confidence × recency composite score.
    /// Must be called inside <c>_writeLock</c>.
    /// </summary>
    private void EvictLowestScored()
    {
        string? worstId = null;
        var worstScore = float.MaxValue;

        foreach (var (id, stored) in _entries)
        {
            var score = stored.Entry.Confidence
                        * TimeDecay.ComputeWeight(stored.Entry.LastObserved, _decayOptions);
            if (score < worstScore)
            {
                worstScore = score;
                worstId = id;
            }
        }

        if (worstId is not null && _entries.TryRemove(worstId, out var evicted))
        {
            // Remove from Kind index
            if (_kindIndex.TryGetValue(evicted.Entry.Kind, out var ids))
                ids.Remove(worstId);

            _logger.LogDebug(
                "Empirical eviction: removed '{Id}' (Kind={Kind}, score={Score:F4}) — heap at capacity ({Max})",
                worstId, evicted.Entry.Kind, worstScore, _maxEntries);
        }
    }

    private sealed record StoredEntry(EmpiricalEntry Entry, ReadOnlyMemory<float> Embedding);
}
