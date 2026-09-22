namespace Ananke.Orchestration.Planning;

/// <summary>Ways of running a node that a consumer would otherwise write around its own runner.</summary>
public static class PlanNodeRunners
{
    /// <summary>
    /// Runs <paramref name="runner"/> for a leaf, and records nothing for a node with children.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A node with children only decomposes.</b> What its criteria ask for is met by the steps
    /// beneath it, so asking a model what it did is a call whose answer nothing reads — and one a
    /// verifier then rules on, against a contract the children were the work for.
    /// </para>
    /// <para>
    /// Opt-in, because a parent that does work of its own is a real shape: a node that reviews what its
    /// children produced has something to do after them, and wrapping its runner in this would silence it.
    /// </para>
    /// </remarks>
    public static PlanNodeRunner LeavesOnly(PlanNodeRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);

        return (context, ct) => context.Node.ChildIds.Count > 0
            ? Task.FromResult(NodeOutcome.Nothing)
            : runner(context, ct);
    }
}
