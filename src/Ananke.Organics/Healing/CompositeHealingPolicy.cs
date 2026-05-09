using Ananke.Organics.Division;

namespace Ananke.Organics.Healing;

/// <summary>
/// Evaluates multiple <see cref="IHealingPolicy"/> instances in order and
/// returns the first non-<see langword="null"/> plan. Returns
/// <see langword="null"/> only when all policies pass.
/// </summary>
public sealed class CompositeHealingPolicy(params IHealingPolicy[] policies) : IHealingPolicy
{
    /// <summary>A composite with no policies — always returns <see langword="null"/>.</summary>
    public static readonly CompositeHealingPolicy Empty = new();

    /// <inheritdoc />
    public async Task<HealingPlan?> EvaluateAsync(
        HealthSnapshot health,
        ComplexitySnapshot complexity,
        CancellationToken ct = default)
    {
        foreach (var policy in policies)
        {
            var plan = await policy.EvaluateAsync(health, complexity, ct);
            if (plan is not null)
                return plan;
        }

        return null;
    }
}
