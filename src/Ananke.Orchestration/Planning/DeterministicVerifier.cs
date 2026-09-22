namespace Ananke.Orchestration.Planning;

/// <summary>
/// Rules on a contract using only checks that can decide without judgement, and <b>abstains</b>
/// wherever none can.
/// </summary>
/// <remarks>
/// <para>
/// <b>Abstention is the feature.</b> Where no check exists this returns "nothing was decided" rather
/// than a guess, so the gap between what can be verified and what merely looks fine is visible
/// instead of papered over. Filling that gap with a model is a separate, riskier decision, and it
/// has to be earned by evidence rather than assumed — an verifier sits in the <em>control</em>
/// path, not only the measurement path, so a judge that is wrong here does not just mismeasure the
/// work, it misdirects it.
/// </para>
/// <para>
/// <b>Gates, then a score.</b> Acceptance criteria gate; quality criteria rank; the score is computed
/// only once every gate has passed. A failed gate cannot be bought back by a strong showing
/// elsewhere, which is precisely what any weighted blend of the two would allow.
/// </para>
/// <para>
/// <b>What it can and cannot do with a dispute.</b> If a node reports that a criterion is wrong and a
/// check decides that criterion holds, the dispute is refuted — the node was mistaken, and nothing
/// needs to travel. If no check can decide it, the dispute <em>stands</em> and travels upward. This
/// type never <em>upholds</em> a dispute on its own authority: upholding is a judgement about the
/// contract, and the contract is not its to rule on.
/// </para>
/// </remarks>
public sealed class DeterministicVerifier : IVerifier
{
    private readonly IReadOnlyList<IDeterministicCheck> _checks;
    private readonly TimeProvider _time;

    /// <summary>Creates an verifier over <paramref name="checks"/>, tried in order.</summary>
    public DeterministicVerifier(
        IEnumerable<IDeterministicCheck> checks, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(checks);
        _checks = [.. checks];
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<Verification> VerifyAsync(
        VerificationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contract = request.Node.Contract;

        var verdicts = new List<CriterionVerdict>();
        var abstained = new List<string>();
        var gateFailed = false;

        foreach (var criterion in contract.AcceptanceCriteria)
        {
            var ruling = await RuleAsync(criterion, ct).ConfigureAwait(false);
            if (ruling is null)
            {
                abstained.Add(criterion);
                continue;
            }

            verdicts.Add(ruling);
            gateFailed |= !ruling.Passed;
        }

        // Quality criteria are ruled on for the record either way, but they only ever *rank* — and
        // ranking a run whose gates failed would put a number next to a result that is simply not
        // acceptable.
        var qualityVerdicts = new List<CriterionVerdict>();
        foreach (var criterion in contract.QualityCriteria)
        {
            var ruling = await RuleAsync(criterion, ct).ConfigureAwait(false);
            if (ruling is null)
                abstained.Add(criterion);
            else
                qualityVerdicts.Add(ruling);
        }

        verdicts.AddRange(qualityVerdicts);

        var gatesDecided = abstained.Count == 0
            || contract.AcceptanceCriteria.All(c => !abstained.Contains(c));

        var outcome = Decide(request.Node, gateFailed, gatesDecided, verdicts);

        return new Verification
        {
            Verdicts = verdicts,
            Outcome = outcome,
            Abstained = abstained,
            Score = outcome is VerificationOutcome.Passed && qualityVerdicts.Count > 0
                ? qualityVerdicts.Count(v => v.Passed) / (double)qualityVerdicts.Count
                : null
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Free, and it asks exactly the question a pass asks: a check declares the criteria it covers,
    /// and <see cref="IDeterministicCheck.CanRule"/> is the same test <see cref="VerifyAsync"/>
    /// applies before running anything. So this cannot disagree with the ruling that follows it
    /// about <em>whether</em> a criterion is decidable — only the verdict itself costs a run.
    /// </remarks>
    public IReadOnlyList<string>? CannotDecide(IReadOnlyList<string> criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        return [.. criteria.Where(criterion => !_checks.Any(check => check.CanRule(criterion)))];
    }

    private static VerificationOutcome Decide(
        PlanNode node, bool gateFailed, bool gatesDecided, IReadOnlyList<CriterionVerdict> verdicts)
    {
        if (node.Violation is { } violation)
        {
            // The only thing this type may say about a dispute is that it was mistaken, and only
            // when something actually ran and disagreed with it.
            var refuting = verdicts.FirstOrDefault(
                v => v.Passed && string.Equals(v.Criterion, violation.Criterion, StringComparison.Ordinal));

            return refuting is not null
                ? VerificationOutcome.ViolationRefuted
                : VerificationOutcome.ViolationStands;
        }

        if (gateFailed)
            return VerificationOutcome.GateFailed;

        return gatesDecided ? VerificationOutcome.Passed : VerificationOutcome.Abstained;
    }

    private async Task<CriterionVerdict?> RuleAsync(string criterion, CancellationToken ct)
    {
        foreach (var check in _checks)
        {
            if (!check.CanRule(criterion))
                continue;

            var finding = await check.RunAsync(criterion, ct).ConfigureAwait(false);
            return new CriterionVerdict
            {
                Criterion = criterion,
                Passed = finding.Holds,
                Oracle = check.Oracle,
                Basis = finding.Detail,
                At = _time.GetUtcNow()
            };
        }

        return null;
    }
}
