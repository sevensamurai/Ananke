using Ananke.Learning.Episodes;


using Ananke.Learning.EmpiricalMemory;

namespace Ananke.Learning.Offline;

/// <summary>
/// Domain-specific source of simulated experience. The offline learner calls
/// this during curiosity walks to generate new observations without real-world
/// interaction. Implementations are domain-specific: self-play for games,
/// Monte Carlo rollouts for planning, scenario replay for incident analysis.
/// </summary>
/// <remarks>
/// This is the "imagination" capability — analogous to mentally rehearsing
/// a scenario before acting. The offline learner uses it to test hypotheses
/// that cannot be verified by searching existing data alone.
/// <para>
/// Simulation evidence is always weighted below reflective (real-data)
/// evidence. A pattern confirmed by 50 self-play games is worth less than
/// the same pattern confirmed by 3 real games with a human. The weighting
/// is controlled by <see cref="OfflineLearnerOptions.SimulationEvidenceWeight"/>.
/// </para>
/// </remarks>
public interface ISimulationSource
{
    /// <summary>
    /// Runs a simulated scenario informed by a hypothesis and the system's
    /// current empirical knowledge. Returns the outcome for intrinsic
    /// reward computation.
    /// </summary>
    /// <param name="hypothesis">The entry being explored — the offline learner
    /// wants to know if this belief holds under simulation.</param>
    /// <param name="relatedKnowledge">Other entries recalled as context —
    /// the simulator can use these to inform strategy.</param>
    /// <param name="maxEpisodes">Maximum scenarios to run, from
    /// <see cref="OfflineLearnerOptions.MaxSimulationEpisodes"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The simulation outcome, including whether the hypothesis
    /// was supported or contradicted.</returns>
    Task<SimulationOutcome> SimulateAsync(
        EmpiricalEntry hypothesis,
        IReadOnlyList<EmpiricalMatch> relatedKnowledge,
        int maxEpisodes,
        CancellationToken ct = default);
}

/// <summary>Result of a simulated scenario.</summary>
public sealed record SimulationOutcome
{
    /// <summary>
    /// Reward signal from the simulation: positive if the hypothesis was
    /// supported, negative if contradicted. Same scale as
    /// <see cref="Reinforcement.Reward"/>.
    /// </summary>
    public required float Reward { get; init; }

    /// <summary>
    /// Natural-language description of what happened in the simulation.
    /// Used as evidence and for discovery reporting.
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>
    /// Number of scenarios/episodes run.
    /// </summary>
    public required int EpisodesRun { get; init; }

    /// <summary>
    /// How many episodes supported the hypothesis.
    /// </summary>
    public required int EpisodesSupported { get; init; }

    /// <summary>
    /// Optional trajectory of states visited during simulation. When provided,
    /// the offline learner can construct an <see cref="Episode"/> and perform
    /// temporal credit assignment on the simulated experience.
    /// </summary>
    public IReadOnlyList<EpisodeStep>? Trajectory { get; init; }

    /// <summary>
    /// Optional intermediate rewards at each simulation step. Length matches
    /// <see cref="Trajectory"/> when both are provided.
    /// </summary>
    public IReadOnlyList<float>? IntermediateRewards { get; init; }
}
