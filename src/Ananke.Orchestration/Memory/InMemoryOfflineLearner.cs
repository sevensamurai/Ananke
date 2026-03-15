using Ananke.Orchestration.Knowledge;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ananke.Orchestration.Memory;

/// <summary>
/// In-memory <see cref="IOfflineLearner"/> implementation that operates on
/// <see cref="IEmpiricalMemory"/> and <see cref="IEmbeddingModel"/>.
/// Handles decay (forgetting), curiosity-driven exploration (wandering),
/// and intrinsic reward computation via vector-space predictions.
/// </summary>
public sealed class InMemoryOfflineLearner : IOfflineLearner
{
    private readonly IEmpiricalMemory _memory;
    private readonly IEmbeddingModel _embedder;
    private readonly IKnowledgeStore? _knowledgeStore;
    private readonly ISimulationSource? _simulator;
    private readonly IConsolidationSummarizer? _summarizer;
    private readonly OfflineLearnerOptions _options;
    private readonly ILogger _logger;
    private readonly Random _rng;

    /// <summary>Page size used when browsing entries for decay and exploration.</summary>
    private const int BrowsePageSize = 100;

    /// <summary>
    /// Creates a new in-memory offline learner.
    /// </summary>
    /// <param name="memory">Empirical memory store to read and write entries.</param>
    /// <param name="embedder">Embedding model for forming predictions and computing similarity.</param>
    /// <param name="knowledgeStore">Optional knowledge store for reflective evidence search and consolidation target.</param>
    /// <param name="simulator">Optional domain-specific simulation source for imagined evidence.</param>
    /// <param name="summarizer">Optional summarizer for consolidation. When provided with a knowledge store, mature entries are promoted.</param>
    /// <param name="options">Offline learner configuration. Defaults are used when <see langword="null"/>.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public InMemoryOfflineLearner(
        IEmpiricalMemory memory,
        IEmbeddingModel embedder,
        IKnowledgeStore? knowledgeStore = null,
        ISimulationSource? simulator = null,
        IConsolidationSummarizer? summarizer = null,
        OfflineLearnerOptions? options = null,
        ILogger<InMemoryOfflineLearner>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(embedder);

        _memory = memory;
        _embedder = embedder;
        _knowledgeStore = knowledgeStore;
        _simulator = simulator;
        _summarizer = summarizer;
        _options = options ?? new OfflineLearnerOptions();
        _logger = logger ?? NullLogger<InMemoryOfflineLearner>.Instance;
        _rng = new Random();
    }

    /// <inheritdoc />
    public async Task<OfflineLearningResult> LearnAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Offline learning cycle starting");

        var decayed = await DecayAsync(ct);
        var (explored, reinforced, contradicted, discoveries) = await ExploreAsync(ct);
        var consolidated = await ConsolidateAsync(ct);

        _logger.LogDebug(
            "Offline learning cycle complete: {Decayed} decayed, {Explored} explored, {Reinforced} reinforced, {Contradicted} contradicted, {Consolidated} consolidated, {Discoveries} discoveries",
            decayed, explored, reinforced, contradicted, consolidated, discoveries.Count);

        return new OfflineLearningResult
        {
            Decayed = decayed,
            Explored = explored,
            Reinforced = reinforced,
            Contradicted = contradicted,
            Consolidated = consolidated,
            Discoveries = discoveries
        };
    }

    /// <inheritdoc />
    public async Task<int> DecayAsync(CancellationToken ct = default)
    {
        var affect = _options.Affect;
        var decayed = 0;
        var offset = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var page = await _memory.BrowseAsync(offset, BrowsePageSize, ct: ct);
            if (page.Count == 0) break;

            foreach (var entry in page)
            {
                ct.ThrowIfCancellationRequested();

                // Apply strength decay: strength = strength * baseDecay - variance * varianceDecay
                var newStrength = entry.Strength * affect.BaseDecayRate
                                - entry.Variance * affect.VarianceDecayRate;

                if (newStrength < affect.DeletionThreshold)
                {
                    // Remove weak entry via contradiction
                    await _memory.ContradictAsync(entry.Id,
                        $"offline-learner decay: strength {entry.Strength:F3} → {newStrength:F3} below threshold {affect.DeletionThreshold:F3}",
                        ct);
                    decayed++;
                    _logger.LogDebug("Decay removed: '{Id}' (strength {Strength:F3})", entry.Id, entry.Strength);
                }
                else if (MathF.Abs(newStrength - entry.Strength) > 0.001f)
                {
                    // Update strength via explicit adjustment
                    var delta = newStrength - entry.Strength;
                    await _memory.ReinforceAsync(entry.Id, new Reinforcement
                    {
                        NewEvidence = [$"offline-learner decay: strength {entry.Strength:F3} → {newStrength:F3}"],
                        ConfidenceAdjustment = 0f, // don't change confidence via flat path
                        StrengthAdjustment = delta,
                        Source = "offline-learner-decay"
                    }, ct);
                }
            }

            offset += page.Count;
        }

        return decayed;
    }

    private async Task<(int Explored, int Reinforced, int Contradicted, List<string> Discoveries)> ExploreAsync(
        CancellationToken ct)
    {
        var batchSize = _options.ExplorationBatchSize;
        var explored = 0;
        var reinforced = 0;
        var contradicted = 0;
        var discoveries = new List<string>();

        // Gather all entries for selection
        var allEntries = new List<EmpiricalEntry>();
        var offset = 0;
        while (true)
        {
            var page = await _memory.BrowseAsync(offset, BrowsePageSize, ct: ct);
            if (page.Count == 0) break;
            allEntries.AddRange(page);
            offset += page.Count;
        }

        if (allEntries.Count == 0) return (0, 0, 0, discoveries);

        // Exclude already-consolidated entries from exploration
        allEntries.RemoveAll(e => e.ConsolidatedInto is not null);
        if (allEntries.Count == 0) return (0, 0, 0, discoveries);

        // Select exploration batch: high-surprise preferred, some random (ε-greedy)
        var randomCount = Math.Max(1, (int)(batchSize * _options.ExplorationRandomFraction));
        var curiousCount = batchSize - randomCount;

        var batch = new List<EmpiricalEntry>();

        // Curious entries: prefer high last prediction error or high variance
        var curious = allEntries
            .Where(e => e.LastPredictionError >= _options.CuriosityThreshold
                     || e.Variance >= _options.CuriosityThreshold)
            .OrderByDescending(e => e.LastPredictionError + e.Variance)
            .Take(curiousCount)
            .ToList();
        batch.AddRange(curious);

        // Random entries (ε-greedy)
        var remaining = allEntries.Except(batch).ToList();
        for (var i = 0; i < randomCount && remaining.Count > 0; i++)
        {
            var idx = _rng.Next(remaining.Count);
            batch.Add(remaining[idx]);
            remaining.RemoveAt(idx);
        }

        // If we didn't fill the curious slots, add more random
        while (batch.Count < batchSize && remaining.Count > 0)
        {
            var idx = _rng.Next(remaining.Count);
            batch.Add(remaining[idx]);
            remaining.RemoveAt(idx);
        }

        // Explore each entry in the batch
        foreach (var entry in batch)
        {
            ct.ThrowIfCancellationRequested();
            explored++;

            var (reward, summary) = await ExploreEntryAsync(entry, ct);

            if (reward > 0)
            {
                await _memory.ReinforceAsync(entry.Id, new Reinforcement
                {
                    NewEvidence = [$"offline-learner exploration: {summary}"],
                    Source = "offline-learner-curiosity",
                    Reward = reward
                }, ct);
                reinforced++;
            }
            else if (reward < _options.ExplorationContradictionThreshold)
            {
                await _memory.ContradictAsync(entry.Id,
                    $"offline-learner exploration: {summary}", ct);
                contradicted++;
            }

            if (reward >= _options.DiscoveryThreshold)
            {
                var discovery = $"{entry.Description} — {summary}";
                discoveries.Add(discovery);
                _logger.LogInformation("Discovery: {Discovery}", discovery);
            }
        }

        return (explored, reinforced, contradicted, discoveries);
    }

    private async Task<int> ConsolidateAsync(CancellationToken ct)
    {
        if (_knowledgeStore is null || _summarizer is null)
            return 0;

        var consolidated = 0;
        var offset = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var page = await _memory.BrowseAsync(offset, BrowsePageSize, ct: ct);
            if (page.Count == 0) break;

            foreach (var entry in page)
            {
                ct.ThrowIfCancellationRequested();

                if (!ShouldConsolidate(entry)) continue;

                var doc = await _summarizer.SummarizeAsync(entry, ct);
                await _knowledgeStore.UpsertAsync([doc], ct);
                await _memory.MarkConsolidatedAsync(entry.Id, doc.Id, ct);

                consolidated++;
                _logger.LogInformation(
                    "Consolidated: '{Id}' → '{DocId}' (strength {Strength:F2}, variance {Variance:F3}, observations {Obs})",
                    entry.Id, doc.Id, entry.Strength, entry.Variance, entry.ObservationCount);
            }

            offset += page.Count;
        }

        return consolidated;
    }

    /// <summary>
    /// Determines whether an empirical entry qualifies for promotion
    /// to the knowledge store.
    /// </summary>
    public bool ShouldConsolidate(EmpiricalEntry entry) =>
        entry.ConsolidatedInto is null
        && entry.Strength >= _options.ConsolidationMinStrength
        && entry.Variance <= _options.ConsolidationMaxVariance
        && entry.ObservationCount >= _options.ConsolidationMinObservations
        && entry.Kind is EmpiricalKind.Pattern or EmpiricalKind.Heuristic;

    private async Task<(float Reward, string Summary)> ExploreEntryAsync(
        EmpiricalEntry entry, CancellationToken ct)
    {
        // Step 1: Form prediction vector
        var entryEmbedding = await _embedder.EmbedAsync(entry.Description.ToEmbeddingText(), ct);

        // Recall similar entries for context
        var neighbors = await _memory.RecallAsync(entry.Description.ToString(),
            new RecallOptions { TopK = 3, MinConfidence = 0.1f }, ct);

        ReadOnlyMemory<float> predictedVector;
        if (neighbors.Count > 0)
        {
            // Weighted average of neighbor embeddings by confidence
            predictedVector = await FormPredictionVectorAsync(entry, neighbors, ct);
        }
        else
        {
            // Cold start: entry's own embedding (no noise since we can't add noise to ReadOnlyMemory easily)
            predictedVector = entryEmbedding;
        }

        // Step 2: Gather evidence
        float reflectiveReward = 0f;
        var hasReflective = false;
        string reflectiveSummary = "";

        // Reflective evidence: search knowledge store
        if (_knowledgeStore is not null)
        {
            var query = entry.Condition ?? entry.Goal ?? entry.Situation ?? entry.Description.ToString();
            var evidence = await _knowledgeStore.SearchAsync(query,
                new SearchOptions { TopK = 3 }, ct);

            if (evidence.Count > 0)
            {
                // Compute actual evidence centroid
                var evidenceEmbeddings = new List<ReadOnlyMemory<float>>();
                foreach (var doc in evidence)
                {
                    var emb = await _embedder.EmbedAsync(doc.Text, ct);
                    evidenceEmbeddings.Add(emb);
                }

                var actualVector = ComputeCentroid(evidenceEmbeddings);
                var surprise = 1f - CosineSimilarity(predictedVector.Span, actualVector.Span);
                var coherence = ComputeCoherence(actualVector, neighbors);

                reflectiveReward = ComputeIntrinsicReward(surprise, coherence);
                hasReflective = true;
                reflectiveSummary = $"reflective: surprise={surprise:F2}, coherence={coherence:F2}";
            }
        }

        // Simulated evidence
        float simulationReward = 0f;
        var hasSimulation = false;
        string simulationSummary = "";

        if (_simulator is not null && entry.Confidence >= _options.SimulationMinConfidence)
        {
            var outcome = await _simulator.SimulateAsync(
                entry, neighbors, _options.MaxSimulationEpisodes, ct);

            simulationReward = outcome.Reward;
            hasSimulation = true;
            simulationSummary = outcome.Summary;
        }

        // Step 3: Combine evidence
        float combinedReward;
        string combinedSummary;

        if (hasReflective && hasSimulation)
        {
            float reflectiveWeight = _options.ReflectiveEvidenceWeight;
            float simulationWeight = _options.SimulationEvidenceWeight;
            combinedReward = (reflectiveReward * reflectiveWeight + simulationReward * simulationWeight)
                           / (reflectiveWeight + simulationWeight);
            combinedSummary = $"{reflectiveSummary}; {simulationSummary}";
        }
        else if (hasSimulation)
        {
            combinedReward = simulationReward;
            combinedSummary = simulationSummary;
        }
        else if (hasReflective)
        {
            combinedReward = reflectiveReward;
            combinedSummary = reflectiveSummary;
        }
        else
        {
            // No evidence source available — use vector-space self-prediction
            var selfSurprise = 1f - CosineSimilarity(predictedVector.Span, entryEmbedding.Span);
            var selfCoherence = ComputeCoherence(entryEmbedding, neighbors);
            combinedReward = ComputeIntrinsicReward(selfSurprise, selfCoherence) * _options.SelfPredictionScale;
            combinedSummary = $"self-prediction: surprise={selfSurprise:F2}, coherence={selfCoherence:F2}";
        }

        return (combinedReward, combinedSummary);
    }

    private async Task<ReadOnlyMemory<float>> FormPredictionVectorAsync(
        EmpiricalEntry entry, IReadOnlyList<EmpiricalMatch> neighbors, CancellationToken ct)
    {
        var embeddings = new List<(ReadOnlyMemory<float> Embedding, float Weight)>();

        // Include entry's own embedding weighted by its confidence
        var selfEmb = await _embedder.EmbedAsync(entry.Description.ToEmbeddingText(), ct);
        embeddings.Add((selfEmb, entry.Confidence));

        // Include neighbor embeddings weighted by their confidence
        foreach (var neighbor in neighbors)
        {
            if (neighbor.Entry.Id == entry.Id) continue;
            var emb = await _embedder.EmbedAsync(neighbor.Entry.Description.ToEmbeddingText(), ct);
            embeddings.Add((emb, neighbor.Entry.Confidence));
        }

        return WeightedAverage(embeddings);
    }

    /// <summary>
    /// Computes an intrinsic reward from surprise and coherence signals.
    /// Uses a 2×2 matrix: surprising+coherent → discovery, surprising+incoherent → noise,
    /// expected+coherent → confirmation, expected+incoherent → contradiction.
    /// </summary>
    public float ComputeIntrinsicReward(float surprise, float coherence)
    {
        // 2×2 matrix:
        // Surprising + Coherent    → discovery (+0.7 to +1.0)
        // Surprising + Incoherent  → noise     (-0.3 to +0.1)
        // Expected + Coherent      → confirm   (+0.1 to +0.3)
        // Expected + Incoherent    → oddity    (-0.5 to -0.1)

        float discoveryComponent = surprise * coherence;
        float confirmationComponent = (1f - surprise) * coherence * _options.ConfirmationWeight;
        float noisePenalty = surprise * (1f - coherence) * _options.NoisePenaltyWeight;
        float contradictionPenalty = (1f - surprise) * (1f - coherence) * _options.ContradictionPenaltyWeight;

        return Math.Clamp(
            discoveryComponent + confirmationComponent + noisePenalty + contradictionPenalty,
            -1f, 1f);
    }

    private float ComputeCoherence(
        ReadOnlyMemory<float> actualVector, IReadOnlyList<EmpiricalMatch> neighbors)
    {
        if (neighbors.Count == 0) return _options.CoherenceNeutral; // neutral when no neighbors

        // Average cosine similarity between actual evidence and each neighbor
        // This is a simplified version — ideally we'd embed each neighbor
        // For now we use the score (which already incorporates vector similarity)
        return neighbors.Average(n => n.Score);
    }

    private static ReadOnlyMemory<float> ComputeCentroid(List<ReadOnlyMemory<float>> embeddings)
    {
        if (embeddings.Count == 0) return ReadOnlyMemory<float>.Empty;
        if (embeddings.Count == 1) return embeddings[0];

        var dim = embeddings[0].Length;
        var result = new float[dim];

        foreach (var emb in embeddings)
        {
            var span = emb.Span;
            for (var i = 0; i < dim; i++)
                result[i] += span[i];
        }

        var count = (float)embeddings.Count;
        for (var i = 0; i < dim; i++)
            result[i] /= count;

        return result;
    }

    private static ReadOnlyMemory<float> WeightedAverage(
        List<(ReadOnlyMemory<float> Embedding, float Weight)> items)
    {
        if (items.Count == 0) return ReadOnlyMemory<float>.Empty;

        var dim = items[0].Embedding.Length;
        var result = new float[dim];
        var totalWeight = 0f;

        foreach (var (emb, weight) in items)
        {
            var span = emb.Span;
            for (var i = 0; i < dim; i++)
                result[i] += span[i] * weight;
            totalWeight += weight;
        }

        if (totalWeight > 0)
        {
            for (var i = 0; i < dim; i++)
                result[i] /= totalWeight;
        }

        return result;
    }

    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0f;

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
}
