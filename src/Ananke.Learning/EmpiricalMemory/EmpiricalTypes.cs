namespace Ananke.Learning.EmpiricalMemory;

/// <summary>
/// Structured semantic description of an empirical entry's content.
/// Decomposes the entry's meaning into weighted semantic tags and an optional
/// human-readable summary, enabling causal-aware dedup, dimension-projected
/// recall, and gap-aware exploration beyond embedding-only similarity.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="FromText"/> to create from free text (the summary becomes
/// the embedding source until tags are populated). Use the primary constructor
/// or init syntax to populate <see cref="SemanticTags"/> directly from LLM
/// extraction, structural data ingestion, or any other mechanism.
/// </para>
/// <para>
/// <see cref="ToString"/> returns a human-readable representation.
/// <see cref="ToEmbeddingText"/> returns text optimized for vector embedding,
/// combining the summary with tag keys for richer semantic coverage.
/// Processing paths (dedup, recall scoring) should prefer <see cref="SemanticTags"/>
/// when populated, falling back to embedding text when tags are absent.
/// </para>
/// </remarks>
public sealed record SemanticDescription
{
    /// <summary>
    /// Optional human-readable summary. When present, <see cref="ToString"/>
    /// returns this value. When <see langword="null"/>, the readable text is
    /// synthesized from <see cref="SemanticTags"/>.
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Weighted semantic tags decomposing the entry's content into namespaced
    /// dimensions (e.g. <c>"cause:gc-pause"</c>, <c>"effect:timeout"</c>,
    /// <c>"location:service-a"</c>). Values are relevance weights in [0.0, 1.0].
    /// Produced by LLM extraction at commit time, derived from existing fields,
    /// or ingested from structural data (service diagrams, schemas).
    /// </summary>
    public IReadOnlyDictionary<string, float> SemanticTags { get; init; }
        = new Dictionary<string, float>();

    /// <summary>
    /// Creates a <see cref="SemanticDescription"/> from free text.
    /// The text becomes the <see cref="Summary"/>; semantic tags can be
    /// populated later by an LLM or extraction pipeline.
    /// </summary>
    public static SemanticDescription FromText(string text) => new() { Summary = text };

    /// <summary>
    /// Creates a <see cref="SemanticDescription"/> from weighted semantic tags.
    /// The readable summary is synthesized from the tag keys.
    /// </summary>
    public static SemanticDescription FromTags(IReadOnlyDictionary<string, float> tags) => new() { SemanticTags = tags };

    /// <summary>
    /// Returns text suitable for vector embedding. Combines the <see cref="Summary"/>
    /// (when present) with <see cref="SemanticTags"/> keys ordered by weight,
    /// giving the embedding model richer semantic coverage than either alone.
    /// </summary>
    public string ToEmbeddingText()
    {
        if (SemanticTags.Count == 0)
            return Summary ?? string.Empty;

        var tagText = string.Join(" ", SemanticTags
            .OrderByDescending(t => t.Value)
            .Select(t => t.Key));

        return Summary is not null
            ? $"{Summary} [{tagText}]"
            : tagText;
    }

    /// <summary>
    /// Returns a human-readable representation. Uses <see cref="Summary"/>
    /// when available; otherwise synthesizes from <see cref="SemanticTags"/>.
    /// </summary>
    public override string ToString()
    {
        if (Summary is not null)
            return Summary;

        if (SemanticTags.Count == 0)
            return string.Empty;

        return string.Join("; ", SemanticTags
            .OrderByDescending(t => t.Value)
            .Select(t => t.Key));
    }

    /// <summary>
    /// Computes the overlap score between this description's tags and another's.
    /// For each tag present in both, the minimum weight is summed and divided
    /// by the maximum possible score. Returns 0 when either side has no tags.
    /// </summary>
    public float TagOverlap(SemanticDescription other)
    {
        if (SemanticTags.Count == 0 || other.SemanticTags.Count == 0)
            return 0f;

        var overlapScore = 0f;
        var maxScore = 0f;

        foreach (var (key, weight) in SemanticTags)
        {
            maxScore += weight;
            if (other.SemanticTags.TryGetValue(key, out var otherWeight))
                overlapScore += MathF.Min(weight, otherWeight);
        }

        return maxScore > 0f ? overlapScore / maxScore : 0f;
    }
}

/// <summary>Discriminator for <see cref="EmpiricalEntry"/> kinds.</summary>
public enum EmpiricalKind
{
    /// <summary>Observational: "when X happens, Y follows."</summary>
    Pattern,

    /// <summary>Procedural: "how to do X" — steps, tools, strategy.</summary>
    Skill,

    /// <summary>Rule of thumb: "prefer X over Y in situation Z."</summary>
    Heuristic
}

/// <summary>
/// A unit of empirical knowledge. The <see cref="Kind"/> determines which
/// shape-specific fields are populated.
/// </summary>
public sealed record EmpiricalEntry
{
    // ── Identity and classification ──────────────────────────────────

    /// <summary>Unique identifier for this entry.</summary>
    public required string Id { get; init; }

    /// <summary>The kind of empirical knowledge this entry represents.</summary>
    public required EmpiricalKind Kind { get; init; }

    /// <summary>Descriptive tags for filtering and categorization.</summary>
    public required IReadOnlyList<string> Tags { get; init; }

    /// <summary>
    /// How this entry was created: <c>"human-confirmed"</c>,
    /// <c>"background-analysis"</c>, <c>"auto-detected"</c>, <c>"authored"</c>.
    /// </summary>
    public required string Source { get; init; }

    // ── Entity scoping ───────────────────────────────────────────────

    /// <summary>
    /// The entity this knowledge pertains to (user, customer, device,
    /// household, etc.). When <see langword="null"/>, the entry is global —
    /// visible to all entities and used as fallback during entity-scoped recall.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Entity-scoped entries are isolated during dedup: a pattern about
    /// entity A will not merge with a semantically similar pattern about
    /// entity B. This prevents cross-entity knowledge leakage.
    /// </para>
    /// <para>
    /// The value is typically an external identifier (user ID, customer ID,
    /// device serial, session group ID) and should be stable across the
    /// entity's lifetime. Ananke does not interpret the value — it is an
    /// opaque partition key.
    /// </para>
    /// </remarks>
    public string? EntityId { get; init; }

    // ── Core content (embedded for vector search) ────────────────────

    /// <summary>
    /// Structured semantic description of what this entry represents.
    /// Contains an optional human-readable summary and weighted semantic tags
    /// for causal-aware dedup, dimension-projected recall, and gap-aware
    /// exploration. Use <see cref="SemanticDescription.ToEmbeddingText"/> for
    /// the text that gets embedded for vector search.
    /// </summary>
    public required SemanticDescription Description { get; init; }

    // ── Confidence and tracking ──────────────────────────────────────

    /// <summary>Confidence score in the range [0.0, 1.0]. Increases with reinforcement.</summary>
    public required float Confidence { get; init; }

    /// <summary>Number of times this entry has been observed or applied.</summary>
    public required int ObservationCount { get; init; }

    /// <summary>Links to logs, incidents, sessions, or other evidence supporting this entry.</summary>
    public required IReadOnlyList<string> Evidence { get; init; }

    /// <summary>When this entry was first observed or created.</summary>
    public required DateTimeOffset FirstObserved { get; init; }

    /// <summary>When this entry was last observed, reinforced, or applied.</summary>
    public required DateTimeOffset LastObserved { get; init; }

    // ── Pattern-specific (Kind == Pattern) ───────────────────────────

    /// <summary>The triggering condition (e.g. "ServiceA GC pause &gt; 200ms").</summary>
    public string? Condition { get; init; }

    /// <summary>The observed effect (e.g. "ServiceB timeout rate spikes").</summary>
    public string? Effect { get; init; }

    /// <summary>How or why the correlation exists.</summary>
    public string? Mechanism { get; init; }

    /// <summary>Delay between condition and effect.</summary>
    public TimeSpan? Latency { get; init; }

    // ── Skill-specific (Kind == Skill) ───────────────────────────────

    /// <summary>What this skill achieves.</summary>
    public string? Goal { get; init; }

    /// <summary>When this skill is applicable.</summary>
    public string? Applicability { get; init; }

    /// <summary>Ordered procedural steps.</summary>
    public IReadOnlyList<string>? Steps { get; init; }

    /// <summary>Tools useful for executing this skill.</summary>
    public IReadOnlyList<string>? Tools { get; init; }

    /// <summary>What a successful application of this skill looks like.</summary>
    public string? ExpectedOutcome { get; init; }

    /// <summary>Number of times this skill has been applied.</summary>
    public int TimesUsed { get; init; }

    /// <summary>Number of times applying this skill led to a successful outcome.</summary>
    public int TimesSucceeded { get; init; }

    // ── Heuristic-specific (Kind == Heuristic) ───────────────────────

    /// <summary>The context or trigger in which this heuristic applies (e.g. "high-traffic deploy window").</summary>
    public string? Situation { get; init; }

    /// <summary>The recommended approach (e.g. "use canary deploys").</summary>
    public string? PreferredApproach { get; init; }

    /// <summary>The approach to avoid (e.g. "big-bang deploys"). Optional — not all heuristics are comparative.</summary>
    public string? AvoidedApproach { get; init; }

    // ── Affective signals ──────────────────────────────────

    /// <summary>
    /// Belief strength — driven by prediction-error-modulated reinforcement.
    /// Decays over time; entries below the configured threshold are candidates
    /// for removal. Distinct from <see cref="Confidence"/>, which is derived
    /// from prediction error variance when affective signals are active.
    /// Default: <c>0.5</c>.
    /// </summary>
    public float Strength { get; init; } = 0.5f;

    /// <summary>
    /// Surprise direction: <c>-1.0</c> (worse than expected) to <c>+1.0</c>
    /// (better than expected). Computed as <c>actual − predicted</c> during
    /// reinforcement. Influences recall priority, not truth.
    /// </summary>
    public float Valence { get; init; }

    /// <summary>
    /// Surprise magnitude: <c>0.0</c> (expected) to <c>1.0</c> (maximally unexpected).
    /// Computed as <c>|predicted − actual|</c> during reinforcement.
    /// Influences recall priority, not truth.
    /// </summary>
    public float Intensity { get; init; }

    /// <summary>
    /// Exponential moving average of squared prediction errors.
    /// Used to derive <see cref="Confidence"/>: <c>1 / (1 + Variance)</c>
    /// when affective reinforcement is active.
    /// Default: <c>1.0</c> (maximum uncertainty).
    /// </summary>
    public float Variance { get; init; } = 1.0f;

    /// <summary>
    /// Most recent prediction error <c>(|predicted − actual|)</c>.
    /// Stored for diagnostics and reinforcement cooldown.
    /// </summary>
    public float LastPredictionError { get; init; }

    /// <summary>
    /// Last prediction formed for this entry, independent of <see cref="Confidence"/>.
    /// When an <see cref="IPredictionSource"/> is configured, this stores the
    /// prediction used in the most recent reinforcement cycle. When <c>null</c>,
    /// the reinforcement path falls back to <see cref="Confidence"/> as the
    /// prediction (backward-compatible default).
    /// </summary>
    public float? Prediction { get; init; }

    // ── Episode linkage ─────────────────────────────────────────────

    /// <summary>
    /// Episode this entry belongs to, or <see langword="null"/> for standalone entries.
    /// </summary>
    public string? EpisodeId { get; init; }

    /// <summary>
    /// Zero-based step index within the episode. Meaningful only when
    /// <see cref="EpisodeId"/> is set.
    /// </summary>
    public int? StepIndex { get; init; }

    // ── Consolidation ────────────────────────────────────────────────

    /// <summary>
    /// When set, this entry has been promoted to <see cref="Ananke.Orchestration.Knowledge.IKnowledgeStore"/>
    /// as the document with this ID. Consolidated entries are excluded from
    /// future recall and exploration — the knowledge store version is canonical.
    /// </summary>
    public string? ConsolidatedInto { get; init; }
}

/// <summary>A recall result pairing an <see cref="EmpiricalEntry"/> with a composite score.</summary>
public sealed record EmpiricalMatch
{
    /// <summary>The matched empirical entry.</summary>
    public required EmpiricalEntry Entry { get; init; }

    /// <summary>Composite score: relevance × confidence × recency.</summary>
    public required float Score { get; init; }
}

/// <summary>Evidence provided when reinforcing an experience entry.</summary>
public sealed record Reinforcement
{
    /// <summary>New evidence links to append to the existing evidence list.</summary>
    public required IReadOnlyList<string> NewEvidence { get; init; }

    /// <summary>
    /// Optional explicit confidence adjustment (e.g. +0.1). When <see langword="null"/>,
    /// the implementation applies a default increment.
    /// </summary>
    public float? ConfidenceAdjustment { get; init; }

    /// <summary>
    /// Optional explicit strength adjustment (e.g. -0.02 for decay). When provided,
    /// the implementation applies this delta to the entry's <see cref="EmpiricalEntry.Strength"/>.
    /// Used by decay sweeps to reduce strength over time without going through
    /// the prediction-error path. When <see langword="null"/>, strength is unchanged
    /// (flat path) or computed from prediction error (PE path).
    /// </summary>
    public float? StrengthAdjustment { get; init; }

    /// <summary>
    /// How this reinforcement was produced: <c>"human-confirmed"</c>,
    /// <c>"background-analysis"</c>, <c>"auto-detected"</c>.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Actual outcome value for prediction-error computation. When provided,
    /// the implementation computes prediction error as
    /// <c>|entry.Confidence − Reward|</c> and modulates reinforcement.
    /// When <see langword="null"/>, falls back to flat confidence adjustment.
    /// </summary>
    public float? Reward { get; init; }
}

/// <summary>Options controlling <see cref="IEmpiricalMemory.RecallAsync"/> behavior.</summary>
public sealed record RecallOptions
{
    /// <summary>Maximum number of results to return. Default is 5.</summary>
    public int TopK { get; init; } = 5;

    /// <summary>Filter by empirical kind. When <see langword="null"/>, all kinds are returned.</summary>
    public EmpiricalKind? Kind { get; init; }

    /// <summary>Minimum confidence threshold. Entries below this are excluded.</summary>
    public float MinConfidence { get; init; }

    /// <summary>Minimum composite score threshold. Results below this are excluded.</summary>
    public float ScoreThreshold { get; init; }

    /// <summary>
    /// Filter by tags. Entries must contain all specified tags to be included.
    /// When <see langword="null"/> or empty, no tag filtering is applied.
    /// </summary>
    public IReadOnlyList<string>? RequiredTags { get; init; }

    /// <summary>
    /// Filter by entity scope. When set, only entries whose
    /// <see cref="EmpiricalEntry.EntityId"/> matches are returned.
    /// When <see cref="IncludeGlobal"/> is <see langword="true"/>,
    /// global entries (<c>EntityId = null</c>) are also included.
    /// When this property is <see langword="null"/>, all entries
    /// (entity-scoped and global) are searched — no entity filtering is applied.
    /// </summary>
    public string? EntityId { get; init; }

    /// <summary>
    /// When <see cref="EntityId"/> is set and this is <see langword="true"/>,
    /// global entries are included alongside entity-specific results.
    /// Has no effect when <see cref="EntityId"/> is <see langword="null"/>.
    /// Default is <see langword="false"/>.
    /// </summary>
    public bool IncludeGlobal { get; init; }
}

/// <summary>
/// Options controlling <see cref="IEmpiricalMemory.BrowseAsync(BrowseOptions, CancellationToken)"/>
/// and <see cref="IEmpiricalMemory.CountAsync"/> behavior.
/// Mirrors the filtering capabilities of <see cref="RecallOptions"/> but for
/// non-vector browsing and counting operations.
/// </summary>
public sealed record BrowseOptions
{
    /// <summary>Zero-based offset for paging. Default is 0.</summary>
    public int Offset { get; init; }

    /// <summary>Maximum number of entries to return. Default is 100.</summary>
    public int Limit { get; init; } = 100;

    /// <summary>Filter by empirical kind. When <see langword="null"/>, all kinds are returned.</summary>
    public EmpiricalKind? Kind { get; init; }

    /// <summary>
    /// Filter by entity scope. When set, only entries scoped to this entity are returned.
    /// When <see langword="null"/>, all entries (entity-scoped and global) are returned.
    /// </summary>
    public string? EntityId { get; init; }

    /// <summary>
    /// Filter by tags. Entries must contain all specified tags to be included.
    /// When <see langword="null"/> or empty, no tag filtering is applied.
    /// </summary>
    public IReadOnlyList<string>? RequiredTags { get; init; }

    /// <summary>Minimum confidence threshold. Entries below this are excluded.</summary>
    public float MinConfidence { get; init; }

    /// <summary>
    /// When <see langword="true"/>, entries that have been consolidated
    /// (i.e., <see cref="EmpiricalEntry.ConsolidatedInto"/> is set) are excluded.
    /// Default is <see langword="false"/> (consolidated entries are included).
    /// </summary>
    public bool ExcludeConsolidated { get; init; }
}

/// <summary>
/// Configuration for affect-driven learning mechanics:
/// prediction-error reinforcement, decay, and priority boosting.
/// </summary>
public sealed record AffectOptions
{
    // ── Reinforcement ────────────────────────────────────────────

    /// <summary>Base learning rate for strength reinforcement. Default: 0.1.</summary>
    public float LearningRate { get; init; } = 0.1f;

    /// <summary>
    /// EMA smoothing factor for variance. Higher values weight recent
    /// errors more. Range (0, 1). Default: 0.1.
    /// </summary>
    public float VarianceSmoothingFactor { get; init; } = 0.1f;

    /// <summary>
    /// Minimum hours between full-strength reinforcements for the same entry.
    /// Prevents frequency-driven self-reinforcement loops. Default: 1.0.
    /// </summary>
    public float ReinforcementCooldownHours { get; init; } = 1.0f;

    /// <summary>
    /// Default confidence adjustment applied in the flat (non-PE) reinforcement
    /// path when <see cref="Reinforcement.ConfidenceAdjustment"/> is
    /// <see langword="null"/>. Default: 0.1.
    /// </summary>
    public float DefaultConfidenceAdjustment { get; init; } = 0.1f;

    /// <summary>
    /// Initial confidence assigned to entries committed by agent tools
    /// (e.g. <c>commit_insight</c>). Default: 0.5.
    /// </summary>
    public float InitialCommitConfidence { get; init; } = 0.5f;

    // ── Decay ─────────────────────────────────────────────────────

    /// <summary>Per-cycle multiplicative decay applied to strength. Default: 0.98.</summary>
    public float BaseDecayRate { get; init; } = 0.98f;

    /// <summary>
    /// Variance-amplified decay multiplier. Unstable beliefs decay faster.
    /// Default: 0.02.
    /// </summary>
    public float VarianceDecayRate { get; init; } = 0.02f;

    /// <summary>Strength below which entries are candidates for removal. Default: 0.05.</summary>
    public float DeletionThreshold { get; init; } = 0.05f;

    // ── Contradiction ────────────────────────────────────────────

    /// <summary>
    /// Multiplier applied to <see cref="LearningRate"/> when computing the
    /// strength penalty during contradiction. Contradiction is treated as
    /// maximally surprising, so the default penalty is <c>LearningRate × 2</c>.
    /// Higher values make contradictions more punishing. Default: 2.0.
    /// </summary>
    public float ContradictionStrengthMultiplier { get; init; } = 2.0f;

    /// <summary>
    /// Valence shift applied during contradiction (affect path).
    /// Negative values push valence toward −1. Default: −0.5.
    /// </summary>
    public float ContradictionValenceShift { get; init; } = -0.5f;

    /// <summary>
    /// Intensity shift applied during contradiction (affect path).
    /// Positive values increase surprise intensity. Default: 0.3.
    /// </summary>
    public float ContradictionIntensityShift { get; init; } = 0.3f;

    /// <summary>
    /// Confidence penalty applied during contradiction in the flat
    /// (non-affect) path. Default: 0.3.
    /// </summary>
    public float ContradictionConfidencePenalty { get; init; } = 0.3f;

    // ── Recall priority ──────────────────────────────────────────

    /// <summary>
    /// Max recall priority boost from valence × intensity.
    /// Applied as: <c>score × (1 + MaxPriorityBoost × intensity × |valence|)</c>.
    /// Default: 0.3 (up to 30% boost).
    /// </summary>
    public float MaxPriorityBoost { get; init; } = 0.3f;

    // ── Evidence management ──────────────────────────────────────

    /// <summary>
    /// Maximum number of evidence entries retained per empirical entry.
    /// Oldest entries are trimmed when the cap is exceeded. Default: 50.
    /// </summary>
    public int MaxEvidenceCount { get; init; } = 50;
}
