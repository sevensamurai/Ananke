namespace Ananke.Abstractions.Trajectory;

/// <summary>
/// Triggers a single learning cycle on the underlying empirical memory store.
/// Implemented by <c>OfflineLearner</c>; decouples the adaptive harness policy
/// from the concrete learner without requiring a circular project reference.
/// </summary>
public interface ILearningCycleTrigger
{
    /// <summary>Runs one learning cycle (decay, exploration, contradiction, consolidation).</summary>
    Task TriggerAsync(CancellationToken ct = default);
}
