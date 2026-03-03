namespace Ananke.Orchestration.Jobs;

/// <summary>
/// Tracks the current subflow nesting depth via <see cref="AsyncLocal{T}"/>.
/// Used by <see cref="SubFlowJob{TParent, TChild}"/> to enforce the maximum depth limit.
/// </summary>
public static class SubFlowContext
{
    private static readonly AsyncLocal<int> Depth = new();

    /// <summary>Gets the current subflow nesting depth (0 = top-level workflow).</summary>
    public static int CurrentDepth
    {
        get => Depth.Value;
        internal set => Depth.Value = value;
    }
}
