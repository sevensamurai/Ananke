namespace Ananke.Orchestration.Planning;

/// <summary>
/// What a plan would leave unmet if it ran.
/// </summary>
/// <remarks>
/// <para>
/// <b>A different question from admission.</b> <see cref="PlanAdmission"/> answers whether a plan is
/// one at all — a step that is its own ancestor, a criterion nothing can decide. This runs the criteria
/// that <em>can</em> be decided and reports the ones that do not hold, so a plan nobody has executed
/// can be refused before a pass is spent discovering it.
/// </para>
/// <para>
/// <b>The steps first, then the plan's own.</b> A check over the whole plan reads what every step asks
/// for, so it has nothing to rule on until the steps are known.
/// </para>
/// <para>
/// Only what a check can decide is reported. A criterion that rests on judgement is not a finding
/// here, and neither is one about work that has not happened yet.
/// </para>
/// </remarks>
public static class PlanFindings
{
    /// <summary>
    /// Every criterion in <paramref name="tree"/> that does not hold, in the words of the check that
    /// ruled on it, or empty when the plan holds.
    /// </summary>
    /// <param name="tree">The plan, which may be a draft nothing has run.</param>
    /// <param name="checks">What is available to rule on it.</param>
    /// <param name="ct">Cancels the run.</param>
    public static async Task<IReadOnlyList<string>> UnmetAsync(
        PlanTree tree,
        IReadOnlyList<IDeterministicCheck> checks,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(checks);

        var root = tree.Root;

        IReadOnlyList<string> criteria =
        [
            .. root.ChildIds.Select(tree.Node).SelectMany(step => step.Contract.AcceptanceCriteria),
            .. root.Contract.AcceptanceCriteria
        ];

        var findings = new List<string>();

        foreach (var criterion in criteria)
        {
            if (checks.FirstOrDefault(check => check.CanRule(criterion)) is not { } check)
            {
                findings.Add($"{criterion}: nothing can decide it");
                continue;
            }

            // The right check asked about something that cannot exist answers no for ever, and says
            // more about why than running it would.
            if (PlanAdmission.Nonsense(checks, criterion) is { } why)
            {
                findings.Add($"{criterion}: {why}");
                continue;
            }

            var finding = await check.RunAsync(criterion, ct).ConfigureAwait(false);

            if (!finding.Holds)
                findings.Add($"{criterion}: {finding.Detail}");
        }

        return findings;
    }
}
