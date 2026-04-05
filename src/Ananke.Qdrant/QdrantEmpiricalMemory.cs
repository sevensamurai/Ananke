using System.Diagnostics;
using System.Diagnostics.Metrics;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Learning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Ananke.Qdrant;

/// <summary>
/// Qdrant-backed <see cref="IEmpiricalMemory"/> for persistent, distributed empirical knowledge.
/// Automatically creates the collection on first use if it does not exist.
/// </summary>
/// <remarks>
/// <para>
/// Uses <c>SetPayloadAsync</c> for <see cref="ReinforceAsync"/> and <see cref="ContradictAsync"/>
/// to update metadata without re-embedding — the vector (derived from the entry's
/// <see cref="EmpiricalEntry.Description"/>) remains stable across reinforcements.
/// </para>
/// <para>
/// Composite scoring is performed client-side:
/// <c>vectorScore × confidence × recencyWeight</c>, using <see cref="TimeDecay"/>
/// for recency. This matches the pattern established by <see cref="CatalogAwareKnowledgeStore"/>.
/// </para>
/// </remarks>
public sealed class QdrantEmpiricalMemory : IEmpiricalMemory
{
    // ── Payload field constants ───────────────────────────────────
    private const string DescriptionKey = "_description";
    private const string SemanticTagsKey = "_semantic_tags";
    private const string KindKey = "kind";
    private const string ConfidenceKey = "confidence";
    private const string ObservationCountKey = "observation_count";
    private const string FirstObservedKey = "first_observed";
    private const string LastObservedKey = "last_observed";
    private const string SourceKey = "source";
    private const string TagsKey = "tags";
    private const string EvidenceKey = "evidence";
    private const string EntityIdKey = "entity_id";

    // Pattern-specific
    private const string ConditionKey = "condition";
    private const string EffectKey = "effect";
    private const string MechanismKey = "mechanism";
    private const string LatencyMinutesKey = "latency_minutes";

    // Skill-specific
    private const string GoalKey = "goal";
    private const string ApplicabilityKey = "applicability";
    private const string StepsKey = "steps";
    private const string ToolsKey = "tools";
    private const string ExpectedOutcomeKey = "expected_outcome";
    private const string TimesUsedKey = "times_used";
    private const string TimesSucceededKey = "times_succeeded";

    // Heuristic-specific
    private const string SituationKey = "situation";
    private const string PreferredApproachKey = "preferred_approach";
    private const string AvoidedApproachKey = "avoided_approach";

    // Affective signals
    private const string StrengthKey = "strength";
    private const string ValenceKey = "valence";
    private const string IntensityKey = "intensity";
    private const string VarianceKey = "variance";
    private const string LastPredictionErrorKey = "last_prediction_error";
    private const string PredictionKey = "prediction";

    // Consolidation
    private const string ConsolidatedIntoKey = "consolidated_into";

    // RFC 4122 §4.3 — predefined DNS namespace UUID used for deterministic v5 UUID generation
    private static readonly Guid UuidNamespaceDns = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

    // ── Observability ────────────────────────────────────────────
    private static readonly ActivitySource ActivitySrc = new("Ananke.EmpiricalMemory");
    private static readonly Meter Meter = new("Ananke.EmpiricalMemory");
    private static readonly Counter<long> CommitCounter = Meter.CreateCounter<long>("empirical.commits", description: "Total entries committed");
    private static readonly Counter<long> DedupCounter = Meter.CreateCounter<long>("empirical.dedup_merges", description: "Entries merged via semantic dedup");
    private static readonly Counter<long> RecallCounter = Meter.CreateCounter<long>("empirical.recalls", description: "Total recall queries");
    private static readonly Counter<long> RecallHitCounter = Meter.CreateCounter<long>("empirical.recall_hits", description: "Recall queries that returned ≥1 result");
    private static readonly Counter<long> ReinforceCounter = Meter.CreateCounter<long>("empirical.reinforcements", description: "Total reinforcements applied");
    private static readonly Counter<long> ContradictCounter = Meter.CreateCounter<long>("empirical.contradictions", description: "Total contradictions applied");

    /// <summary>Maximum evidence entries retained per entry to prevent unbounded growth.</summary>
    private int MaxEvidenceCount => _affectOptions?.MaxEvidenceCount ?? new AffectOptions().MaxEvidenceCount;

    private readonly QdrantClient _client;
    private readonly IEmbeddingModel _embedder;
    private readonly string _collectionName;
    private readonly uint _vectorSize;
    private readonly float _dedupThreshold;
    private readonly TimeDecayOptions _decayOptions;
    private readonly AffectOptions? _affectOptions;
    private readonly IPredictionSource? _predictionSource;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    /// <summary>
    /// Creates a Qdrant-backed empirical memory store.
    /// </summary>
    /// <param name="client">Qdrant gRPC client instance.</param>
    /// <param name="embedder">Embedding model for vectorizing descriptions and queries.</param>
    /// <param name="collectionName">Qdrant collection name. Default is <c>"empirical_memory"</c>.</param>
    /// <param name="vectorSize">
    /// Dimensionality of the embedding vectors. Must match the embedding model output.
    /// Default is <c>1536</c> (OpenAI text-embedding-3-small).
    /// </param>
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
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public QdrantEmpiricalMemory(
        QdrantClient client,
        IEmbeddingModel embedder,
        string collectionName = "empirical_memory",
        uint vectorSize = 1536,
        float dedupThreshold = 0.9f,
        TimeDecayOptions? decayOptions = null,
        AffectOptions? affectOptions = null,
        IPredictionSource? predictionSource = null,
        ILogger<QdrantEmpiricalMemory>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(embedder);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        _client = client;
        _embedder = embedder;
        _collectionName = collectionName;
        _vectorSize = vectorSize;
        _dedupThreshold = dedupThreshold;
        _decayOptions = decayOptions ?? new TimeDecayOptions { HalfLifeDays = 90, FloorWeight = 0.3f };
        _affectOptions = affectOptions;
        _predictionSource = predictionSource;
        _logger = logger ?? NullLogger<QdrantEmpiricalMemory>.Instance;
    }

    /// <inheritdoc />
    public async Task<EmpiricalEntry> CommitAsync(EmpiricalEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await EnsureCollectionAsync(ct);

        using var span = ActivitySrc.StartActivity("EmpiricalMemory.Commit");
        span?.SetTag("empirical.entry_id", entry.Id);
        span?.SetTag("empirical.kind", entry.Kind.ToString());

        var embedding = await _embedder.EmbedAsync(entry.Description.ToEmbeddingText(), ct);

        // Semantic dedup: search for a similar existing entry of the same kind and entity
        var kindFilter = new Filter
        {
            Must = { Conditions.MatchKeyword(KindKey, entry.Kind.ToString().ToLowerInvariant()) }
        };

        if (entry.EntityId is not null)
            kindFilter.Must.Add(Conditions.MatchKeyword(EntityIdKey, entry.EntityId));
        else
            kindFilter.Must.Add(new Condition
            {
                IsEmpty = new IsEmptyCondition { Key = EntityIdKey }
            });

        var similar = await _client.SearchAsync(
            collectionName: _collectionName,
            vector: embedding,
            filter: kindFilter,
            limit: 1,
            scoreThreshold: _dedupThreshold,
            payloadSelector: true,
            cancellationToken: ct);

        if (similar is [var existing])
        {
            // Merge into existing: bump confidence and observation count
            var existingEntry = MapPayloadToEntry(existing.Id.Uuid, existing.Payload);
            var currentConfidence = GetDouble(existing.Payload, ConfidenceKey);
            var currentCount = GetLong(existing.Payload, ObservationCountKey, 1);
            var currentEvidence = GetStringList(existing.Payload, EvidenceKey);

            // Merge into existing: increment observation count and merge evidence.
            // Do NOT bump confidence — that is the prediction-error path's job.
            var mergedEvidence = TrimEvidence([.. currentEvidence, .. entry.Evidence]);
            var mergedLastObserved = Math.Max(
                GetLong(existing.Payload, LastObservedKey),
                entry.LastObserved.ToUnixTimeSeconds());

            var updatedPayload = new Dictionary<string, Value>
            {
                [ObservationCountKey] = currentCount + 1,
                [LastObservedKey] = mergedLastObserved,
                [EvidenceKey] = ToListValue(mergedEvidence)
            };

            await _client.SetPayloadAsync(
                _collectionName,
                updatedPayload,
                Guid.Parse(existing.Id.Uuid),
                cancellationToken: ct);

            DedupCounter.Add(1);
            _logger.LogDebug("Empirical dedup: merged '{NewId}' into '{ExistingId}'",
                entry.Id, existingEntry.Id);

            return existingEntry with
            {
                ObservationCount = (int)(currentCount + 1),
                LastObserved = DateTimeOffset.FromUnixTimeSeconds(mergedLastObserved),
                Evidence = mergedEvidence
            };
        }

        // No duplicate — upsert new point
        var point = BuildPoint(entry, embedding);
        await _client.UpsertAsync(_collectionName, [point], cancellationToken: ct);

        CommitCounter.Add(1);
        _logger.LogDebug("Empirical commit: new {Kind} '{Id}' (confidence: {Confidence:F2})",
            entry.Kind, entry.Id, entry.Confidence);
        return entry;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmpiricalMatch>> RecallAsync(
        string situation, RecallOptions? options = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(situation);
        await EnsureCollectionAsync(ct);

        using var span = ActivitySrc.StartActivity("EmpiricalMemory.Recall");

        options ??= new RecallOptions();
        var queryEmbedding = await _embedder.EmbedAsync(situation, ct);
        var filter = BuildRecallFilter(options);

        var results = await _client.SearchAsync(
            collectionName: _collectionName,
            vector: queryEmbedding,
            filter: filter,
            limit: (ulong)options.TopK,
            payloadSelector: true,
            cancellationToken: ct);

        // Client-side composite scoring: vectorScore × confidence × recencyWeight
        var matches = results
            .Select(p =>
            {
                var entry = MapScoredPointToEntry(p);
                var recencyWeight = TimeDecay.ComputeWeight(entry.LastObserved, _decayOptions);
                var compositeScore = p.Score * entry.Confidence * recencyWeight;

                if (_affectOptions is not null)
                {
                    var priorityBoost = 1f + _affectOptions.MaxPriorityBoost
                                           * entry.Intensity
                                           * MathF.Abs(entry.Valence);
                    compositeScore *= priorityBoost;
                }

                return new EmpiricalMatch { Entry = entry, Score = compositeScore };
            })
            .Where(m => m.Entry.ConsolidatedInto is null && m.Score >= options.ScoreThreshold)
            .OrderByDescending(m => m.Score)
            .ToList();

        RecallCounter.Add(1);
        if (matches.Count > 0) RecallHitCounter.Add(1);

        span?.SetTag("empirical.recall_count", matches.Count);
        _logger.LogDebug("Empirical recall: '{Situation}' → {Count} results",
            situation, matches.Count);

        return matches;
    }

    /// <inheritdoc />
    public async Task ReinforceAsync(string entryId, Reinforcement reinforcement, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentNullException.ThrowIfNull(reinforcement);
        await EnsureCollectionAsync(ct);

        var pointId = ToPointId(entryId);
        var points = await _client.RetrieveAsync(
            _collectionName,
            [pointId],
            withPayload: true,
            cancellationToken: ct);

        var point = points.FirstOrDefault()
            ?? throw new KeyNotFoundException($"Empirical entry '{entryId}' not found.");

        var currentConfidence = GetDouble(point.Payload, ConfidenceKey);
        var currentCount = GetLong(point.Payload, ObservationCountKey, 1);
        var currentEvidence = GetStringList(point.Payload, EvidenceKey);
        var currentPrediction = point.Payload.TryGetValue(PredictionKey, out var predVal)
            ? (float?)predVal.DoubleValue : null;

        Dictionary<string, Value> updatedPayload;

        if (reinforcement.Reward is not null && _affectOptions is not null)
        {
            // ── Prediction-error path ───────────────────────────────────────
            float predicted;
            if (_predictionSource is not null)
            {
                var entry = MapPayloadToEntry(pointId.Uuid, point.Payload);
                var fresh = await _predictionSource.PredictAsync(entry, this, ct);
                predicted = fresh ?? currentPrediction ?? (float)currentConfidence;
            }
            else
            {
                predicted = currentPrediction ?? (float)currentConfidence;
            }

            float actual = reinforcement.Reward.Value;
            float predictionError = MathF.Abs(predicted - actual);

            var lastObservedUnix = GetLong(point.Payload, LastObservedKey);
            var lastObserved = DateTimeOffset.FromUnixTimeSeconds(lastObservedUnix);
            float hours = (float)(DateTimeOffset.UtcNow - lastObserved).TotalHours;
            float cooldown = MathF.Min(1f, hours / _affectOptions.ReinforcementCooldownHours);

            float currentStrength = (float)GetDouble(point.Payload, StrengthKey, 0.5);
            float currentVariance = (float)GetDouble(point.Payload, VarianceKey, 1.0);

            float strengthDelta = _affectOptions.LearningRate * (1f - predictionError) * cooldown;
            float a = _affectOptions.VarianceSmoothingFactor;
            float newVariance = (1f - a) * currentVariance + a * predictionError * predictionError;
            float newConfidence = 1f / (1f + newVariance);
            float newValence = Math.Clamp(actual - predicted, -1f, 1f);
            float newIntensity = Math.Clamp(predictionError, 0f, 1f);

            var newEvidence = TrimEvidence([.. currentEvidence, .. reinforcement.NewEvidence]);

            updatedPayload = new Dictionary<string, Value>
            {
                [ConfidenceKey] = (double)newConfidence,
                [ObservationCountKey] = currentCount + 1,
                [LastObservedKey] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                [EvidenceKey] = ToListValue(newEvidence),
                [StrengthKey] = (double)MathF.Max(0f, currentStrength + strengthDelta),
                [ValenceKey] = (double)newValence,
                [IntensityKey] = (double)newIntensity,
                [VarianceKey] = (double)newVariance,
                [LastPredictionErrorKey] = (double)predictionError,
                [PredictionKey] = (double)predicted
            };

            _logger.LogDebug(
                "Empirical reinforce (PE): '{Id}' confidence {Old:F2} → {New:F2}, strength delta {Delta:F3}",
                entryId, currentConfidence, newConfidence, strengthDelta);
        }
        else
        {
            // ── Flat path (backward compatible) ────────────────────────────
            var adjustment = reinforcement.ConfidenceAdjustment
                ?? (_affectOptions?.DefaultConfidenceAdjustment ?? 0.1f);
            var newConfidence = Math.Min(1.0, currentConfidence + adjustment);
            var newEvidence = TrimEvidence([.. currentEvidence, .. reinforcement.NewEvidence]);

            updatedPayload = new Dictionary<string, Value>
            {
                [ConfidenceKey] = newConfidence,
                [ObservationCountKey] = currentCount + 1,
                [LastObservedKey] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                [EvidenceKey] = ToListValue(newEvidence)
            };

            if (reinforcement.StrengthAdjustment is not null)
            {
                float currentStrength = (float)GetDouble(point.Payload, StrengthKey, 0.5);
                updatedPayload[StrengthKey] = (double)MathF.Max(0f,
                    currentStrength + reinforcement.StrengthAdjustment.Value);
            }

            _logger.LogDebug("Empirical reinforce: '{Id}' confidence {Old:F2} → {New:F2}",
                entryId, currentConfidence, newConfidence);
        }

        await _client.SetPayloadAsync(
            _collectionName,
            updatedPayload,
            ToGuid(entryId),
            cancellationToken: ct);

        ReinforceCounter.Add(1);
    }

    /// <inheritdoc />
    public async Task ContradictAsync(string entryId, string reason, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        await EnsureCollectionAsync(ct);

        var pointId = ToPointId(entryId);
        var points = await _client.RetrieveAsync(
            _collectionName,
            [pointId],
            withPayload: true,
            cancellationToken: ct);

        var point = points.FirstOrDefault()
            ?? throw new KeyNotFoundException($"Empirical entry '{entryId}' not found.");

        var currentConfidence = GetDouble(point.Payload, ConfidenceKey);
        var currentEvidence = GetStringList(point.Payload, EvidenceKey);

        Dictionary<string, Value> updatedPayload;
        double newConfidence;

        if (_affectOptions is not null)
        {
            // Contradiction is maximally surprising negative evidence.
            float currentVariance = (float)GetDouble(point.Payload, VarianceKey, 1.0);
            float currentStrength = (float)GetDouble(point.Payload, StrengthKey, 0.5);
            float currentValence = (float)GetDouble(point.Payload, ValenceKey);
            float currentIntensity = (float)GetDouble(point.Payload, IntensityKey);

            float a = _affectOptions.VarianceSmoothingFactor;
            float newVariance = (1f - a) * currentVariance + a * 1f; // error² = 1.0
            newConfidence = 1f / (1f + newVariance);

            updatedPayload = new Dictionary<string, Value>
            {
                [ConfidenceKey] = newConfidence,
                [StrengthKey] = (double)MathF.Max(0f, currentStrength - _affectOptions.LearningRate * _affectOptions.ContradictionStrengthMultiplier),
                [VarianceKey] = (double)newVariance,
                [ValenceKey] = (double)Math.Clamp(currentValence + _affectOptions.ContradictionValenceShift, -1.0, 1.0),
                [IntensityKey] = (double)Math.Clamp(currentIntensity + _affectOptions.ContradictionIntensityShift, 0.0, 1.0),
                [LastPredictionErrorKey] = 1.0,
                [LastObservedKey] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                [EvidenceKey] = ToListValue(TrimEvidence([.. currentEvidence, $"contradicted: {reason}"]))
            };
        }
        else
        {
            var penalty = _affectOptions?.ContradictionConfidencePenalty
                ?? new AffectOptions().ContradictionConfidencePenalty;
            newConfidence = Math.Max(0.0, currentConfidence - penalty);

            updatedPayload = new Dictionary<string, Value>
            {
                [ConfidenceKey] = newConfidence,
                [LastObservedKey] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                [EvidenceKey] = ToListValue(TrimEvidence([.. currentEvidence, $"contradicted: {reason}"]))
            };
        }

        await _client.SetPayloadAsync(
            _collectionName,
            updatedPayload,
            ToGuid(entryId),
            cancellationToken: ct);

        ContradictCounter.Add(1);
        _logger.LogDebug("Empirical contradict: '{Id}' confidence {Old:F2} → {New:F2} — {Reason}",
            entryId, currentConfidence, newConfidence, reason);
    }

    /// <inheritdoc />
    public async Task<EmpiricalEntry?> GetAsync(string entryId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        await EnsureCollectionAsync(ct);

        var points = await _client.RetrieveAsync(
            _collectionName,
            [ToPointId(entryId)],
            withPayload: true,
            cancellationToken: ct);

        var point = points.FirstOrDefault();
        return point is null ? null : MapPayloadToEntry(entryId, point.Payload);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmpiricalEntry>> BrowseAsync(
        int offset, int limit, EmpiricalKind? kind = null,
        string? entityId = null, CancellationToken ct = default)
    {
        await EnsureCollectionAsync(ct);

        Filter? filter = null;
        if (kind is not null || entityId is not null)
        {
            filter = new Filter();
            if (kind is not null)
                filter.Must.Add(Conditions.MatchKeyword(KindKey, kind.Value.ToString().ToLowerInvariant()));
            if (entityId is not null)
                filter.Must.Add(Conditions.MatchKeyword(EntityIdKey, entityId));
        }

        var result = await _client.ScrollAsync(
            _collectionName,
            filter: filter,
            limit: (uint)limit,
            offset: offset > 0 ? new PointId { Num = (ulong)offset } : null,
            payloadSelector: true,
            cancellationToken: ct);

        return result.Result.Select(p => MapPayloadToEntry(p.Id.Uuid, p.Payload)).ToList();
    }

    /// <inheritdoc />
    public async Task MarkConsolidatedAsync(string entryId, string knowledgeDocId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(knowledgeDocId);
        await EnsureCollectionAsync(ct);

        var pointId = ToPointId(entryId);
        var points = await _client.RetrieveAsync(
            _collectionName,
            [pointId],
            withPayload: false,
            cancellationToken: ct);

        if (points.Count == 0)
            throw new KeyNotFoundException($"Empirical entry '{entryId}' not found.");

        await _client.SetPayloadAsync(
            _collectionName,
            new Dictionary<string, Value>
            {
                [ConsolidatedIntoKey] = knowledgeDocId
            },
            ToGuid(entryId),
            cancellationToken: ct);

        _logger.LogDebug("Empirical consolidated: '{Id}' → '{DocId}'", entryId, knowledgeDocId);
    }

    // ── Collection initialization ────────────────────────────────

    private async Task EnsureCollectionAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            var exists = await _client.CollectionExistsAsync(_collectionName, ct);
            if (!exists)
            {
                await _client.CreateCollectionAsync(
                    _collectionName,
                    new VectorParams { Size = _vectorSize, Distance = Distance.Cosine },
                    cancellationToken: ct);

                await _client.CreatePayloadIndexAsync(
                    _collectionName, KindKey,
                    PayloadSchemaType.Keyword, cancellationToken: ct);

                await _client.CreatePayloadIndexAsync(
                    _collectionName, ConfidenceKey,
                    PayloadSchemaType.Float, cancellationToken: ct);

                await _client.CreatePayloadIndexAsync(
                    _collectionName, LastObservedKey,
                    PayloadSchemaType.Integer, cancellationToken: ct);

                await _client.CreatePayloadIndexAsync(
                    _collectionName, SourceKey,
                    PayloadSchemaType.Keyword, cancellationToken: ct);

                await _client.CreatePayloadIndexAsync(
                    _collectionName, TagsKey,
                    PayloadSchemaType.Keyword, cancellationToken: ct);

                await _client.CreatePayloadIndexAsync(
                    _collectionName, EntityIdKey,
                    PayloadSchemaType.Keyword, cancellationToken: ct);

                // Affective signals — strength indexed for decay filtering
                await _client.CreatePayloadIndexAsync(
                    _collectionName, StrengthKey,
                    PayloadSchemaType.Float, cancellationToken: ct);
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    // ── Point building ───────────────────────────────────────────

    private PointStruct BuildPoint(EmpiricalEntry entry, ReadOnlyMemory<float> embedding)
    {
        var payload = new Dictionary<string, Value>
        {
            [DescriptionKey] = entry.Description.Summary ?? entry.Description.ToString(),
            [KindKey] = entry.Kind.ToString().ToLowerInvariant(),
            [ConfidenceKey] = (double)entry.Confidence,
            [ObservationCountKey] = entry.ObservationCount,
            [FirstObservedKey] = entry.FirstObserved.ToUnixTimeSeconds(),
            [LastObservedKey] = entry.LastObserved.ToUnixTimeSeconds(),
            [SourceKey] = entry.Source,
            [TagsKey] = ToListValue(entry.Tags),
            [EvidenceKey] = ToListValue(entry.Evidence)
        };

        // Entity scoping
        if (entry.EntityId is not null)
            payload[EntityIdKey] = entry.EntityId;

        // Pattern-specific fields
        if (entry.Condition is not null) payload[ConditionKey] = entry.Condition;
        if (entry.Effect is not null) payload[EffectKey] = entry.Effect;
        if (entry.Mechanism is not null) payload[MechanismKey] = entry.Mechanism;
        if (entry.Latency is not null) payload[LatencyMinutesKey] = (long)entry.Latency.Value.TotalMinutes;

        // Skill-specific fields
        if (entry.Goal is not null) payload[GoalKey] = entry.Goal;
        if (entry.Applicability is not null) payload[ApplicabilityKey] = entry.Applicability;
        if (entry.Steps is not null) payload[StepsKey] = ToListValue(entry.Steps);
        if (entry.Tools is not null) payload[ToolsKey] = ToListValue(entry.Tools);
        if (entry.ExpectedOutcome is not null) payload[ExpectedOutcomeKey] = entry.ExpectedOutcome;
        if (entry.TimesUsed > 0) payload[TimesUsedKey] = entry.TimesUsed;
        if (entry.TimesSucceeded > 0) payload[TimesSucceededKey] = entry.TimesSucceeded;

        // Heuristic-specific fields
        if (entry.Situation is not null) payload[SituationKey] = entry.Situation;
        if (entry.PreferredApproach is not null) payload[PreferredApproachKey] = entry.PreferredApproach;
        if (entry.AvoidedApproach is not null) payload[AvoidedApproachKey] = entry.AvoidedApproach;

        // Affective signals (always stored — defaults are meaningful)
        payload[StrengthKey] = (double)entry.Strength;
        payload[ValenceKey] = (double)entry.Valence;
        payload[IntensityKey] = (double)entry.Intensity;
        payload[VarianceKey] = (double)entry.Variance;
        payload[LastPredictionErrorKey] = (double)entry.LastPredictionError;

        // Prediction (stored independently of confidence)
        if (entry.Prediction is not null)
            payload[PredictionKey] = (double)entry.Prediction.Value;

        // Consolidation
        if (entry.ConsolidatedInto is not null)
            payload[ConsolidatedIntoKey] = entry.ConsolidatedInto;

        // Semantic tags
        if (entry.Description.SemanticTags.Count > 0)
        {
            var tagStruct = new Struct();
            foreach (var (key, weight) in entry.Description.SemanticTags)
                tagStruct.Fields[key] = new Value { DoubleValue = weight };
            payload[SemanticTagsKey] = new Value { StructValue = tagStruct };
        }

        return new PointStruct
        {
            Id = ToPointId(entry.Id),
            Vectors = embedding.ToArray(),
            Payload = { payload }
        };
    }

    // ── Filter building ──────────────────────────────────────────

    private static Filter? BuildRecallFilter(RecallOptions options)
    {
        var filter = new Filter();

        if (options.EntityId is not null)
        {
            if (options.IncludeGlobal)
            {
                // Match entity-specific OR global (entity_id absent)
                var entityOrGlobal = new Filter
                {
                    Should =
                    {
                        Conditions.MatchKeyword(EntityIdKey, options.EntityId),
                        new Condition { IsEmpty = new IsEmptyCondition { Key = EntityIdKey } }
                    }
                };
                filter.Must.Add(new Condition { Filter = entityOrGlobal });
            }
            else
            {
                filter.Must.Add(Conditions.MatchKeyword(EntityIdKey, options.EntityId));
            }
        }

        if (options.Kind is not null)
            filter.Must.Add(Conditions.MatchKeyword(KindKey,
                options.Kind.Value.ToString().ToLowerInvariant()));

        if (options.MinConfidence > 0)
            filter.Must.Add(Conditions.Range(ConfidenceKey,
                new global::Qdrant.Client.Grpc.Range { Gte = options.MinConfidence }));

        if (options.RequiredTags is { Count: > 0 })
        {
            foreach (var tag in options.RequiredTags)
                filter.Must.Add(Conditions.MatchKeyword(TagsKey, tag));
        }

        return filter.Must.Count > 0 ? filter : null;
    }

    // ── Payload mapping ──────────────────────────────────────────

    private EmpiricalEntry MapScoredPointToEntry(ScoredPoint point) =>
        MapPayloadToEntry(point.Id.Uuid, point.Payload);

    private static EmpiricalEntry MapPayloadToEntry(
        string id, IReadOnlyDictionary<string, Value> payload)
    {
        var kindStr = GetString(payload, KindKey, "pattern");
        var kind = Enum.TryParse<EmpiricalKind>(kindStr, ignoreCase: true, out var k)
            ? k
            : EmpiricalKind.Pattern;

        return new EmpiricalEntry
        {
            Id = id,
            Kind = kind,
            Tags = GetStringList(payload, TagsKey),
            Source = GetString(payload, SourceKey),
            Description = MapPayloadToDescription(payload),
            Confidence = (float)GetDouble(payload, ConfidenceKey),
            ObservationCount = (int)GetLong(payload, ObservationCountKey, 1),
            Evidence = GetStringList(payload, EvidenceKey),
            FirstObserved = DateTimeOffset.FromUnixTimeSeconds(GetLong(payload, FirstObservedKey)),
            LastObserved = DateTimeOffset.FromUnixTimeSeconds(GetLong(payload, LastObservedKey)),
            Condition = GetStringOrNull(payload, ConditionKey),
            Effect = GetStringOrNull(payload, EffectKey),
            Mechanism = GetStringOrNull(payload, MechanismKey),
            Latency = payload.TryGetValue(LatencyMinutesKey, out var lat)
                ? TimeSpan.FromMinutes(lat.IntegerValue)
                : null,
            Goal = GetStringOrNull(payload, GoalKey),
            Applicability = GetStringOrNull(payload, ApplicabilityKey),
            Steps = payload.TryGetValue(StepsKey, out var steps)
                ? ExtractStringList(steps) : null,
            Tools = payload.TryGetValue(ToolsKey, out var tools)
                ? ExtractStringList(tools) : null,
            ExpectedOutcome = GetStringOrNull(payload, ExpectedOutcomeKey),
            TimesUsed = (int)GetLong(payload, TimesUsedKey),
            TimesSucceeded = (int)GetLong(payload, TimesSucceededKey),
            Situation = GetStringOrNull(payload, SituationKey),
            PreferredApproach = GetStringOrNull(payload, PreferredApproachKey),
            AvoidedApproach = GetStringOrNull(payload, AvoidedApproachKey),
            Strength = (float)GetDouble(payload, StrengthKey, 0.5),
            Valence = (float)GetDouble(payload, ValenceKey),
            Intensity = (float)GetDouble(payload, IntensityKey),
            Variance = (float)GetDouble(payload, VarianceKey, 1.0),
            LastPredictionError = (float)GetDouble(payload, LastPredictionErrorKey),
            Prediction = payload.TryGetValue(PredictionKey, out var pred)
                ? (float)pred.DoubleValue : null,
            ConsolidatedInto = GetStringOrNull(payload, ConsolidatedIntoKey),
            EntityId = GetStringOrNull(payload, EntityIdKey)
        };
    }

    // ── Payload helpers ──────────────────────────────────────────

    private static SemanticDescription MapPayloadToDescription(
        IReadOnlyDictionary<string, Value> payload)
    {
        var summary = GetStringOrNull(payload, DescriptionKey);
        Dictionary<string, float>? tags = null;

        if (payload.TryGetValue(SemanticTagsKey, out var tagsValue)
            && tagsValue.KindCase == Value.KindOneofCase.StructValue)
        {
            tags = [];
            foreach (var field in tagsValue.StructValue.Fields)
            {
                if (field.Value.KindCase == Value.KindOneofCase.DoubleValue)
                    tags[field.Key] = (float)field.Value.DoubleValue;
            }
        }

        return new SemanticDescription
        {
            Summary = summary,
            SemanticTags = tags ?? new Dictionary<string, float>()
        };
    }

    private static string GetString(
        IReadOnlyDictionary<string, Value> payload, string key, string fallback = "")
    {
        if (payload.TryGetValue(key, out var v) && v.KindCase == Value.KindOneofCase.StringValue)
            return v.StringValue;
        return fallback;
    }

    private static string? GetStringOrNull(IReadOnlyDictionary<string, Value> payload, string key)
    {
        if (payload.TryGetValue(key, out var v) && v.KindCase == Value.KindOneofCase.StringValue
            && v.StringValue.Length > 0)
            return v.StringValue;
        return null;
    }

    private static double GetDouble(IReadOnlyDictionary<string, Value> payload, string key, double fallback = 0)
    {
        if (!payload.TryGetValue(key, out var v)) return fallback;
        return v.KindCase switch
        {
            Value.KindOneofCase.DoubleValue => v.DoubleValue,
            Value.KindOneofCase.IntegerValue => v.IntegerValue,
            _ => fallback
        };
    }

    private static long GetLong(
        IReadOnlyDictionary<string, Value> payload, string key, long fallback = 0)
    {
        if (!payload.TryGetValue(key, out var v)) return fallback;
        return v.KindCase switch
        {
            Value.KindOneofCase.IntegerValue => v.IntegerValue,
            Value.KindOneofCase.DoubleValue => (long)v.DoubleValue,
            _ => fallback
        };
    }

    private static List<string> GetStringList(
        IReadOnlyDictionary<string, Value> payload, string key)
    {
        if (!payload.TryGetValue(key, out var v)) return [];
        return ExtractStringList(v);
    }

    private static List<string> ExtractStringList(Value value)
    {
        if (value.KindCase == Value.KindOneofCase.ListValue)
            return value.ListValue.Values
                .Where(v => v.KindCase == Value.KindOneofCase.StringValue)
                .Select(v => v.StringValue)
                .ToList();

        // Fallback: comma-separated string
        if (value.KindCase == Value.KindOneofCase.StringValue && value.StringValue.Length > 0)
            return value.StringValue
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

        return [];
    }

    private static Value ToListValue(IEnumerable<string> items)
    {
        var list = new ListValue();
        foreach (var item in items)
            list.Values.Add(new Value { StringValue = item });
        return new Value { ListValue = list };
    }

    /// <summary>Keeps only the most recent evidence entries to prevent unbounded growth.</summary>
    private List<string> TrimEvidence(List<string> evidence) =>
        evidence.Count <= MaxEvidenceCount
            ? evidence
            : evidence.GetRange(evidence.Count - MaxEvidenceCount, MaxEvidenceCount);

    // ── Deterministic point IDs ──────────────────────────────────

    private static PointId ToPointId(string entryId) =>
        new() { Uuid = ToUuidV5(entryId).ToString("D") };

    private static Guid ToGuid(string entryId) => ToUuidV5(entryId);

    /// <summary>
    /// RFC 4122 §4.3 — generates a version 5 UUID using SHA-1 hashing of the namespace UUID and name.
    /// </summary>
    private static Guid ToUuidV5(string name)
    {
        var namespaceBytes = UuidNamespaceDns.ToByteArray();
        SwapGuidBytes(namespaceBytes);

        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, input, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, input, namespaceBytes.Length, nameBytes.Length);

        var hash = System.Security.Cryptography.SHA1.HashData(input);
        var result = new byte[16];
        Array.Copy(hash, result, 16);

        result[6] = (byte)((result[6] & 0x0F) | 0x50);
        result[8] = (byte)((result[8] & 0x3F) | 0x80);

        SwapGuidBytes(result);
        return new Guid(result);
    }

    private static void SwapGuidBytes(byte[] bytes)
    {
        (bytes[0], bytes[3]) = (bytes[3], bytes[0]);
        (bytes[1], bytes[2]) = (bytes[2], bytes[1]);
        (bytes[4], bytes[5]) = (bytes[5], bytes[4]);
        (bytes[6], bytes[7]) = (bytes[7], bytes[6]);
    }
}
