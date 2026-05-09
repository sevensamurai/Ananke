using Ananke.Orchestration.Workflows;
using Ananke.Design;
using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Organics.Division;
using Ananke.Organics.Healing;
using Ananke.Organics.Division.Approval;
using Ananke.Organics.Kernel.Lineage;
using Ananke.Organics.Sensing;

namespace Ananke.Organics.Kernel;

/// <summary>
/// Configuration for organic growth behavior within an
/// <see cref="OrganicHost"/>.
/// </summary>
public sealed record OrganicGrowthOptions
{
    /// <summary>Division policy (cold-start or experience-driven).</summary>
    public required IDivisionPolicy Policy { get; init; }

    /// <summary>
    /// Approval gate for division proposals. Default: <see cref="AutoApprovalGate"/>.
    /// </summary>
    public IDivisionApprovalGate ApprovalGate { get; init; } = new AutoApprovalGate();

    /// <summary>
    /// Health monitor for complexity and operational fitness. Default: new <see cref="WorkflowExecutionMonitor"/>.
    /// </summary>
    public IHealthMonitor Monitor { get; init; } = new WorkflowExecutionMonitor();

    /// <summary>
    /// How often to evaluate complexity after executions. Default: every 10
    /// executions per workflow.
    /// </summary>
    public int EvaluationInterval { get; init; } = 10;

    /// <summary>
    /// Optional outcome tracker for learning feedback. When set, division
    /// outcomes are automatically tracked and fed back into empirical memory.
    /// </summary>
    public IDivisionOutcomeTracker? OutcomeTracker { get; init; }

    /// <summary>
    /// Workflow manifest factory for policy evaluation. Required when using
    /// policies that inspect the manifest (most do).
    /// </summary>
    public Func<string, WorkflowManifest>? ManifestFactory { get; init; }

    /// <summary>
    /// Optional source of remote cell names for timer-based polling. When set,
    /// <see cref="OrganicHost"/> periodically polls remote cells for complexity
    /// evaluation since their executions don't flow through the local
    /// observation channel.
    /// </summary>
    public IRemoteCellSource? RemoteCellSource { get; init; }

    /// <summary>
    /// How often to poll remote cells for complexity evaluation. Only used
    /// when <see cref="RemoteCellSource"/> is set. Default: 60 seconds.
    /// </summary>
    public TimeSpan RemotePollingInterval { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Optional workflow divider. When set and a division is approved by the gate,
    /// the host automatically executes the division on a background task. When
    /// <see langword="null"/>, approved divisions emit
    /// <see cref="OrganicHost.OnDivisionApproved"/> but are not executed
    /// (manual handling required).
    /// </summary>
    public IWorkflowDivider? Divider { get; init; }

    /// <summary>
    /// Shared empirical memory for RNA seeding during division. Required when
    /// <see cref="Divider"/> is set — the divider needs the parent's memory
    /// to seed children with domain-filtered knowledge.
    /// </summary>
    public IEmpiricalMemory? SharedMemory { get; init; }

    /// <summary>
    /// Controls the drain → switchover → complete handover sequence during
    /// cell division, ensuring in-flight requests are not silently dropped.
    /// Defaults to <see cref="StopTheWorldDivisionTransition"/> when
    /// <see cref="Divider"/> is set and this property is <see langword="null"/>.
    /// </summary>
    public IDivisionTransition? Transition { get; init; }

    /// <summary>
    /// Optional domain router to update after a successful division.
    /// When set, <see cref="OrganicHost"/> calls
    /// <see cref="IDomainRouter.IndexAsync"/> with the new child cells so
    /// the router immediately learns the post-division topology.
    /// </summary>
    public IDomainRouter? DomainRouter { get; init; }

    /// <summary>
    /// Milliseconds to wait after division before sampling child complexity
    /// snapshots for the outcome-tracker reward signal. Allows child cells
    /// time to stabilize before their metrics are captured.
    /// Set to <c>0</c> to skip the delay (useful in tests).
    /// Default: 5000 ms.
    /// </summary>
    public int StabilizationWindowMs { get; init; } = 5000;

    /// <summary>
    /// Milliseconds to wait for in-flight division tasks to complete during
    /// <see cref="OrganicHost.DisposeAsync"/>. If the timeout elapses, disposal
    /// proceeds and any remaining tasks are abandoned.
    /// Default: 30 000 ms.
    /// </summary>
    public int DivisionShutdownTimeoutMs { get; init; } = 30_000;

    // ── L1 Lineage ──────────────────────────────────────────────────

    /// <summary>
    /// Lineage store for recording cell births and deaths.
    /// Default: <see cref="InMemoryLineageStore"/>.
    /// </summary>
    public ILineageStore Lineage { get; init; } = new InMemoryLineageStore();

    // ── L3 Metabolism ───────────────────────────────────────────────

    /// <summary>
    /// Thresholds used to classify a cell's metabolic signal as
    /// <see cref="MetabolicSignal.Healthy"/>, <see cref="MetabolicSignal.Stressed"/>,
    /// or <see cref="MetabolicSignal.Starved"/>.
    /// Default: <see cref="MetabolicThresholds.Default"/>.
    /// </summary>
    public MetabolicThresholds MetabolicThresholds { get; init; } = MetabolicThresholds.Default;

    // ── L4 Apoptosis ────────────────────────────────────────────────

    /// <summary>
    /// Healing policies evaluated on the background loop. Opt-in for prune
    /// policies to avoid unexpected auto-deletion.
    /// Default: <see cref="CompositeHealingPolicy.Empty"/> (no apoptosis).
    /// </summary>
    public IHealingPolicy ApoptosisPolicy { get; init; } = CompositeHealingPolicy.Empty;

    // ── L5 Quorum ───────────────────────────────────────────────────

    /// <summary>
    /// Mesh aggregator for quorum sensing. Receives per-cell metabolic reports
    /// and emits <see cref="MeshSignal"/> events.
    /// Default: <see cref="InMemoryMeshAggregator"/>.
    /// </summary>
    public IMeshAggregator MeshAggregator { get; init; } = new InMemoryMeshAggregator();

    // ── P2-5 Failure classification ─────────────────────────────────

    /// <summary>
    /// Classifier used to categorise workflow execution failures into upstream,
    /// workflow, capability-mismatch, or infrastructure lanes.
    /// Default: <see cref="FailureClassifierProfiles.OpenAI()"/> profile.
    /// Use <see cref="FailureClassifierBuilder"/> to compose a custom profile.
    /// </summary>
    public FailureClassifier FailureClassifier { get; init; } =
        FailureClassifierProfiles.OpenAI().Build();

    /// <summary>
    /// Validates cross-field constraints. Called once by <see cref="OrganicHost"/>
    /// during construction. Throws <see cref="ArgumentException"/> on the first
    /// violation found.
    /// </summary>
    public void Validate()
    {
        if (Divider is not null && SharedMemory is null)
            throw new ArgumentException(
                $"'{nameof(SharedMemory)}' is required when '{nameof(Divider)}' is set.",
                nameof(SharedMemory));
    }

    /// <summary>
    /// Returns a new <see cref="OrganicGrowthOptionsBuilder"/> for fluent construction.
    /// </summary>
    public static OrganicGrowthOptionsBuilder CreateBuilder() => new();
}
