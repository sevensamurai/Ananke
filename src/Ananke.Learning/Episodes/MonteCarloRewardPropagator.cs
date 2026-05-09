using Ananke.Learning.EmpiricalMemory;

namespace Ananke.Learning.Episodes;

/// <summary>
/// Monte Carlo reward propagator — computes discounted returns backward
/// through an episode trajectory and reinforces each empirical entry with
/// its computed return.
/// </summary>
/// <remarks>
/// The Monte Carlo return for step <c>t</c> in an episode of length <c>T</c>:
/// <code>G(t) = Σ_{k=t}^{T} γ^(k-t) × r(k)</code>
/// where <c>r(T)</c> is the terminal reward and <c>r(k)</c> for <c>k &lt; T</c>
/// is the intermediate reward at step <c>k</c> (zero when
/// <see cref="RewardPropagationOptions.IncludeIntermediateRewards"/> is <see langword="false"/>).
/// </remarks>
public sealed class MonteCarloRewardPropagator(
    RewardPropagationOptions? options = null) : IRewardPropagator
{
    private readonly RewardPropagationOptions _options = options ?? new();

    /// <inheritdoc />
    public async Task<int> PropagateAsync(
        Episode episode, IEmpiricalMemory memory, CancellationToken ct = default)
    {
        var steps = episode.Steps;
        if (steps.Count == 0) return 0;

        // Compute discounted returns backward
        var returns = new float[steps.Count];
        var T = steps.Count - 1;

        // Start from terminal step
        returns[T] = episode.TerminalReward
            + (_options.IncludeIntermediateRewards ? steps[T].IntermediateReward : 0f);

        for (var t = T - 1; t >= 0; t--)
        {
            var intermediate = _options.IncludeIntermediateRewards
                ? steps[t].IntermediateReward
                : 0f;
            returns[t] = intermediate + _options.DiscountFactor * returns[t + 1];
        }

        // Reinforce each entry with its computed return
        var reinforced = 0;
        for (var t = 0; t <= T; t++)
        {
            var entry = await memory.GetAsync(steps[t].EntryId, ct);
            if (entry is null) continue;

            await memory.ReinforceAsync(steps[t].EntryId, new Reinforcement
            {
                NewEvidence = [$"episode:{episode.Id} step:{t} return:{returns[t]:F3}"],
                Source = _options.EvidenceSource,
                Reward = returns[t]
            }, ct);
            reinforced++;
        }

        return reinforced;
    }
}
