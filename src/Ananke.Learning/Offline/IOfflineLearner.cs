using Ananke.Learning.EmpiricalMemory;

namespace Ananke.Learning.Offline;

/// <summary>
/// Background learning service that operates on <see cref="IEmpiricalMemory"/>
/// independently of active conversations. Handles forgetting (decay),
/// curiosity-driven exploration (wandering), and eventually consolidation
/// (abstraction promotion).
/// </summary>
/// <remarks>
/// Analogous to sleep consolidation in neuroscience: a periodic process
/// that strengthens stable memories, prunes uncertain ones, and discovers
/// connections that weren't visible during active use.
/// Implementations may be hosted as <c>IHostedService</c>, scheduled via
/// a timer, or invoked manually (e.g., between games in the Connect4 demo).
/// </remarks>
public interface IOfflineLearner
{
    /// <summary>
    /// Runs one full learning cycle: decay → curiosity walk → (future: consolidation).
    /// Returns a summary of what happened.
    /// </summary>
    Task<OfflineLearningResult> LearnAsync(CancellationToken ct = default);

    /// <summary>
    /// Runs only the decay sweep. Useful when called on a separate schedule
    /// from exploration.
    /// </summary>
    Task<int> DecayAsync(CancellationToken ct = default);
}

/// <summary>Summary of a single offline learning cycle.</summary>
public sealed record OfflineLearningResult
{
    /// <summary>Entries removed by the decay sweep.</summary>
    public required int Decayed { get; init; }

    /// <summary>Entries explored during the curiosity walk.</summary>
    public required int Explored { get; init; }

    /// <summary>Entries reinforced by intrinsic reward (prediction confirmed).</summary>
    public required int Reinforced { get; init; }

    /// <summary>Entries contradicted (prediction failed).</summary>
    public required int Contradicted { get; init; }

    /// <summary>Entries promoted to <see cref="Ananke.Orchestration.Knowledge.IKnowledgeStore"/> during consolidation.</summary>
    public required int Consolidated { get; init; }

    /// <summary>
    /// Discoveries worth reporting. Each is a natural-language summary
    /// suitable for delivery via <c>SignalInsightAsync</c>, email, etc.
    /// </summary>
    public required IReadOnlyList<string> Discoveries { get; init; }
}

/// <summary>Configuration for the offline learner service.</summary>
public sealed record OfflineLearnerOptions
{
    /// <summary>
    /// How many entries to explore per curiosity walk. Default: 5.
    /// Higher values = more thorough but slower cycles.
    /// </summary>
    public int ExplorationBatchSize { get; init; } = 5;

    /// <summary>
    /// Selection bias for curiosity walk. Entries with prediction error
    /// above this threshold are preferred for exploration. Default: 0.5.
    /// </summary>
    public float CuriosityThreshold { get; init; } = 0.5f;

    /// <summary>
    /// Fraction of exploration batch reserved for random entries
    /// (ε-greedy exploration). Default: 0.2 (1 in 5 is random).
    /// </summary>
    public float ExplorationRandomFraction { get; init; } = 0.2f;

    /// <summary>
    /// Minimum score improvement over prediction to count as a discovery
    /// worth reporting. Default: 0.3.
    /// </summary>
    public float DiscoveryThreshold { get; init; } = 0.3f;

    /// <summary>Affect options to use for reinforcement and decay.</summary>
    public AffectOptions Affect { get; init; } = new();

    /// <summary>
    /// Max simulation episodes per explored entry. Only used when an
    /// <see cref="ISimulationSource"/> is provided. Default: 20.
    /// </summary>
    public int MaxSimulationEpisodes { get; init; } = 20;

    /// <summary>
    /// Minimum entry confidence before simulation is attempted.
    /// Very low-confidence entries should accumulate reflective evidence
    /// before spending simulation budget. Default: 0.2.
    /// </summary>
    public float SimulationMinConfidence { get; init; } = 0.2f;

    /// <summary>
    /// Weight of simulation evidence relative to reflective (real-data)
    /// evidence when combining rewards. Real data should always dominate.
    /// Default: 0.3 (simulation counts for 30% of reflective weight).
    /// </summary>
    public float SimulationEvidenceWeight { get; init; } = 0.3f;

    // ── Intrinsic reward ─────────────────────────────────────────

    /// <summary>
    /// Weight of the confirmation component (expected + coherent) in the
    /// intrinsic reward matrix. Default: 0.3.
    /// </summary>
    public float ConfirmationWeight { get; init; } = 0.3f;

    /// <summary>
    /// Weight of the noise penalty (surprising + incoherent) in the
    /// intrinsic reward matrix. Should be ≤ 0. Default: −0.3.
    /// </summary>
    public float NoisePenaltyWeight { get; init; } = -0.3f;

    /// <summary>
    /// Weight of the contradiction penalty (expected + incoherent) in the
    /// intrinsic reward matrix. Should be ≤ 0. Default: −0.5.
    /// </summary>
    public float ContradictionPenaltyWeight { get; init; } = -0.5f;

    /// <summary>
    /// Neutral coherence value used when no neighbors are available for
    /// comparison. Default: 0.5 (neither coherent nor incoherent).
    /// </summary>
    public float CoherenceNeutral { get; init; } = 0.5f;

    /// <summary>
    /// Reward threshold below which an exploration result is recorded as a
    /// contradiction. Default: −0.1.
    /// </summary>
    public float ExplorationContradictionThreshold { get; init; } = -0.1f;

    /// <summary>
    /// Scaling factor applied to self-prediction reward when no external
    /// evidence source (knowledge store or simulation) is available.
    /// Default: 0.5 (self-prediction counts for half weight).
    /// </summary>
    public float SelfPredictionScale { get; init; } = 0.5f;

    /// <summary>
    /// Weight of reflective (real-data) evidence when combining with
    /// simulated evidence. Default: 1.0.
    /// </summary>
    public float ReflectiveEvidenceWeight { get; init; } = 1.0f;

    // ── Consolidation ────────────────────────────────────────────

    /// <summary>
    /// Minimum strength for an entry to be considered for consolidation.
    /// Default: 0.8.
    /// </summary>
    public float ConsolidationMinStrength { get; init; } = 0.8f;

    /// <summary>
    /// Maximum variance for an entry to be considered for consolidation.
    /// Low variance means the belief is stable. Default: 0.05.
    /// </summary>
    public float ConsolidationMaxVariance { get; init; } = 0.05f;

    /// <summary>
    /// Minimum observation count for consolidation eligibility.
    /// Default: 10.
    /// </summary>
    public int ConsolidationMinObservations { get; init; } = 10;
}
