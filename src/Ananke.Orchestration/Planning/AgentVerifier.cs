using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;

namespace Ananke.Orchestration.Planning;

/// <summary>One criterion, as a reviewer ruled on it.</summary>
/// <remarks>
/// Numbered rather than quoted back, because a criterion is a sentence and a model asked to repeat
/// one will paraphrase it — and a verdict that cannot be matched to the criterion it is about is a
/// verdict about nothing.
/// </remarks>
public sealed record CriterionReview
{
    /// <summary>Which criterion this is about, as numbered in the question.</summary>
    public int Number { get; init; }

    /// <summary><c>met</c>, <c>not met</c>, or anything else — which is read as "could not tell".</summary>
    /// <remarks>
    /// Deliberately a string with a lenient reading. A model that answers off-script produces an
    /// abstention, which is the safe direction: the failure this design refuses is an unverified
    /// criterion recorded as met.
    /// </remarks>
    public string Verdict { get; init; } = string.Empty;

    /// <summary>What in the record supports it.</summary>
    public string Basis { get; init; } = string.Empty;
}

/// <summary>What a reviewer answers: one finding per criterion it was shown.</summary>
public sealed record Review
{
    /// <summary>The findings, one per criterion.</summary>
    public IReadOnlyList<CriterionReview> Findings { get; init; } = [];
}

/// <summary>
/// Rules on criteria that no deterministic check can decide, by asking a model to read the record.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Verdicts are matched by exact text, so a criterion nobody wrote a check
/// for is abstained — correctly — and a plan re-authored in new words comes out of a change of plan
/// gated by nothing. Observed live: a stronger planner authored visibly better criteria on every
/// pass, the node abstained on all of them, ran out of attempts, and came back. The loop the tier
/// exists for cannot close while nothing can rule on what the coordinator writes.
/// </para>
/// <para>
/// <b>It rules on the record, and only on the record.</b> The reviewer is shown the tree as an
/// verifier reads it — verdicts, contracts, structure — and <em>not</em> the node's own account
/// of what it did. That account is narration: nothing derives from it and it never becomes a
/// verdict, so a ruling resting on it would launder a claim by the thing whose work is in question
/// into evidence about that work.
/// </para>
/// <para>
/// <b>Three things it may never do</b>, and each has a failure behind it:
/// </para>
/// <list type="number">
///   <item><b>Pass what it cannot tell.</b> Anything but a clear ruling is an abstention, and an
///   abstention is reported rather than resolved. An unverified criterion recorded as met is the
///   one outcome that makes a run look finished when it is not.</item>
///   <item><b>Rule a criterion wrong.</b> Whether a contract is right belongs to whoever wrote it,
///   and a node's dispute travels there regardless. The answer shape cannot express it.</item>
///   <item><b>Refute a dispute.</b> A check that refutes one ran a program over the artifact; a
///   model that refutes one has re-read the record the node read and disagreed with the witness. So
///   a dispute stands through this type whatever it rules, and the coordinator is shown both.</item>
/// </list>
/// <para>
/// <b>Checks first, and they win.</b> Where a deterministic check can decide, it does — it is
/// cheaper, repeatable, and answers about the artifact rather than about the record. This only ever
/// sees what that left undecided.
/// </para>
/// </remarks>
public sealed class AgentVerifier : IVerifier
{
    private const string DefaultPersona =
        "You review work that has already been done, against the criteria it was given. You rule "
        + "only on what the record in front of you shows. If the record does not settle a "
        + "criterion, answer that you cannot tell — that is a correct answer, and recording "
        + "something as met when nothing showed it is the one mistake that matters here. You are "
        + "not asked whether a criterion is fair, achievable or well written: if one looks wrong, "
        + "that is somebody else's judgement and yours is 'cannot tell'.";

    private readonly IAgentModel _reviewer;
    private readonly IVerifier? _checks;
    private readonly string _persona;
    private readonly TimeProvider _time;

    /// <summary>Creates a reviewer over <paramref name="reviewer"/>, after <paramref name="checks"/>.</summary>
    /// <param name="reviewer">The model that reads the record.</param>
    /// <param name="checks">
    /// What rules first — usually a <see cref="DeterministicVerifier"/>. <see langword="null"/>
    /// sends every criterion to the model, which is the configuration with nothing to fall back on.
    /// </param>
    /// <param name="persona">Overrides what the reviewer is told it is doing.</param>
    /// <param name="timeProvider">Clock for the verdicts.</param>
    public AgentVerifier(
        IAgentModel reviewer,
        IVerifier? checks = null,
        string? persona = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(reviewer);

        _reviewer = reviewer;
        _checks = checks;
        _persona = persona ?? DefaultPersona;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<Verification> VerifyAsync(
        VerificationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ruled = _checks is null
            ? Nothing(request)
            : await _checks.VerifyAsync(request, ct).ConfigureAwait(false);

        // Nothing left over means nothing to ask about, and a model call for a node whose gates all
        // decided is a cost with no question attached.
        if (ruled.Abstained.Count == 0)
            return ruled;

        var open = ruled.Abstained;
        var review = await ReviewAsync(request, open, ct).ConfigureAwait(false);

        var added = new List<CriterionVerdict>();
        var stillOpen = new List<string>();

        for (var i = 0; i < open.Count; i++)
        {
            var finding = review?.Findings.FirstOrDefault(f => f.Number == i + 1);

            if (Read(finding?.Verdict) is not { } passed)
            {
                stillOpen.Add(open[i]);
                continue;
            }

            added.Add(new CriterionVerdict
            {
                Criterion = open[i],
                Passed = passed,
                Oracle = "the reviewer, from the record",
                Basis = string.IsNullOrWhiteSpace(finding!.Basis) ? null : finding.Basis,
                At = _time.GetUtcNow()
            });
        }

        return Merge(request, ruled, added, stillOpen);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>Nobody could say.</b> Whether this can decide a criterion is only knowable by reading the
    /// record and ruling on it, which is the work itself — so it answers <see langword="null"/>
    /// rather than claiming in advance that everything is decidable. What the checks beneath it
    /// would have abstained on is <em>not</em> the answer either: those are exactly the criteria
    /// this exists to try.
    /// </remarks>
    public IReadOnlyList<string>? CannotDecide(IReadOnlyList<string> criteria) => null;

    /// <summary>The starting point when nothing else ruled: everything open, nothing decided.</summary>
    private static Verification Nothing(VerificationRequest request)
    {
        var contract = request.Node.Contract;

        return new Verification
        {
            Verdicts = [],
            Outcome = request.Node.Violation is null
                ? VerificationOutcome.Abstained
                : VerificationOutcome.ViolationStands,
            Abstained = [.. contract.AcceptanceCriteria, .. contract.QualityCriteria]
        };
    }

    /// <summary>Reads a model's word for a verdict, and refuses to guess at anything else.</summary>
    private static bool? Read(string? verdict) => verdict?.Trim().ToLowerInvariant() switch
    {
        "met" or "yes" or "true" or "pass" or "passed" => true,
        "not met" or "unmet" or "no" or "false" or "fail" or "failed" => false,
        _ => null
    };

    /// <summary>
    /// Folds what the reviewer decided into what the checks already had.
    /// </summary>
    /// <remarks>
    /// <b>A disputed node keeps the outcome the checks gave it.</b> Refuting a dispute is a
    /// statement that the node was mistaken about its own contract, and only something that ran over
    /// the artifact is in a position to make it — so a model's verdicts are recorded beside the
    /// dispute and the dispute still travels.
    /// </remarks>
    private static Verification Merge(
        VerificationRequest request,
        Verification ruled,
        IReadOnlyList<CriterionVerdict> added,
        IReadOnlyList<string> stillOpen)
    {
        var verdicts = new List<CriterionVerdict>([.. ruled.Verdicts, .. added]);
        var contract = request.Node.Contract;

        var outcome = request.Node.Violation is not null
            ? ruled.Outcome
            : Decide(contract, verdicts, stillOpen);

        var quality = verdicts.Where(v => contract.QualityCriteria.Contains(v.Criterion, StringComparer.Ordinal)).ToList();

        return ruled with
        {
            Verdicts = verdicts,
            Outcome = outcome,
            Abstained = stillOpen,
            Score = outcome is VerificationOutcome.Passed && quality.Count > 0
                ? quality.Count(v => v.Passed) / (double)quality.Count
                : null
        };
    }

    private static VerificationOutcome Decide(
        AgentContract contract, IReadOnlyList<CriterionVerdict> verdicts, IReadOnlyList<string> stillOpen)
    {
        if (verdicts.Any(v => !v.Passed && contract.AcceptanceCriteria.Contains(v.Criterion, StringComparer.Ordinal)))
            return VerificationOutcome.GateFailed;

        return contract.AcceptanceCriteria.Any(c => stillOpen.Contains(c, StringComparer.Ordinal))
            ? VerificationOutcome.Abstained
            : VerificationOutcome.Passed;
    }

    /// <summary>Asks the reviewer about everything nothing else could decide, in one call.</summary>
    private async Task<Review?> ReviewAsync(
        VerificationRequest request, IReadOnlyList<string> open, CancellationToken ct)
    {
        Review? review = null;

        var job = AgentJobFactory.Create<VerificationRequest, Review>("plan-reviewer", _reviewer)
            .WithSystemPrompt(_persona)
            .WithPrompt(r => Describe(r, open))
            .MapResult((state, produced) =>
            {
                review = produced;
                return state;
            })
            .Build();

        await job.ExecuteAsync(request, ct).ConfigureAwait(false);

        return review;
    }

    /// <summary>
    /// What the reviewer is shown: the work item, the record around it, and the open criteria.
    /// </summary>
    /// <remarks>
    /// The node's own account of what it did is <b>deliberately absent</b>. It is the claim of the
    /// thing being ruled on, and a verdict resting on it would be the node grading its own work with
    /// an extra step in between.
    /// </remarks>
    private static string Describe(VerificationRequest request, IReadOnlyList<string> open)
    {
        var numbered = string.Join('\n', open.Select((c, i) => $"  {i + 1}. {c}"));

        return $"""
            The work item is '{request.Node.Id}'. Its goal: {request.Node.Contract.Goal}

            Constraints that held for the whole of it:
            {string.Join('\n', request.Node.Contract.Constraints.Select(c => $"  {c}"))}

            The record, as it stands:
            {request.TreeView}

            Rule on each of these, and nothing else:
            {numbered}

            For each one, answer with its number, a verdict of exactly `met`, `not met` or
            `cannot tell`, and the basis — what in the record above supports your verdict. If the
            record does not show it either way, `cannot tell` is the answer; do not infer it from
            what the work item was asked to do.
            """;
    }
}
