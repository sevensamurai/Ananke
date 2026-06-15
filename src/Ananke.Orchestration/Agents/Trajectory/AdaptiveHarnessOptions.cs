namespace Ananke.Orchestration.Agents.Trajectory;

/// <summary>
/// Configuration options for <see cref="CompositeAdaptiveHarnessPolicy"/>.
/// </summary>
public sealed class AdaptiveHarnessOptions
{
    /// <summary>
    /// Minimum number of hallucinated tool calls in a single trajectory before a
    /// learning cycle is triggered. Default: <c>2</c>.
    /// </summary>
    public int HallucinationThreshold { get; set; } = 2;

    /// <summary>
    /// Reward applied to all tracked tools in the kit when the trajectory ends
    /// with abandoned faults (non-positive terminal reward). Range: (-∞, 0].
    /// Default: <c>-0.8</c>.
    /// </summary>
    public float AbandonedFaultPenalty { get; set; } = -0.8f;

    /// <summary>
    /// Reward applied to all tracked tools in the kit when the trajectory succeeds
    /// on the first LLM call (zero retries). Range: [0, 1]. Default: <c>1.0</c>.
    /// </summary>
    public float SuccessReward { get; set; } = 1.0f;

    /// <summary>
    /// Name of the <see cref="Tools.ToolKit"/> whose affinity entries this policy manages.
    /// Required for affinity-update rules; leave empty to skip affinity updates.
    /// </summary>
    public string KitName { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of snapshots queued in the background adaptation channel before
    /// the oldest entry is dropped. Default: <c>512</c>.
    /// </summary>
    public int AdaptationChannelCapacity { get; set; } = 512;
}
