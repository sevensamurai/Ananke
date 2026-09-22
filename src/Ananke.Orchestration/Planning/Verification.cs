namespace Ananke.Orchestration.Planning;

/// <summary>
/// What a check found: whether the criterion holds, and — when it does not — what the world said.
/// </summary>
/// <remarks>
/// <para>
/// <b>The channel a check had no way to use.</b> Before this, a check could say <em>false</em> and
/// nothing else, so the moment a verdict needed a reason — why a step could not be carried, what a
/// build actually printed — the only thing left in the room able to supply one was a model. That is
/// how a step came to classify its own outcome: not by design, because a vacancy needed filling.
/// </para>
/// <para>
/// <b><see cref="Detail"/> is evidence, never a summary.</b> It is what ran, in its own words — a
/// compiler's output, the world's own record — not a sentence composed about what ran. A check that
/// interprets rather than reports has stopped being deterministic in the sense this interface exists
/// to guarantee.
/// </para>
/// </remarks>
public sealed record Finding
{
    /// <summary>Whether the criterion holds.</summary>
    public required bool Holds { get; init; }

    /// <summary>
    /// Why not, verbatim from whatever ran. <see langword="null"/> when there is nothing to say, or
    /// when <see cref="Holds"/> is <see langword="true"/> — a check that passed needs no explaining.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>A criterion that holds, with nothing further to say.</summary>
    public static Finding Held { get; } = new() { Holds = true };

    /// <summary>A criterion that does not hold, with the world's own account of why — if there is one.</summary>
    public static Finding Not(string? detail = null) => new() { Holds = false, Detail = detail };
}

/// <summary>
/// A check that can decide a criterion without judgement — a build, a test run, a named assertion.
/// </summary>
/// <remarks>
/// The interface is deliberately narrow. Anything that needs to <em>read and weigh</em> in order to
/// answer is not one of these, and must not be dressed up as one: a check that quietly guesses is
/// worse than no check, because its verdict carries the authority of something that ran.
/// </remarks>
public interface IDeterministicCheck
{
    /// <summary>What runs, named so that re-running it later is a concrete instruction.</summary>
    string Oracle { get; }

    /// <summary>Whether this check can decide <paramref name="criterion"/>.</summary>
    bool CanRule(string criterion);

    /// <summary>Runs it.</summary>
    Task<Finding> RunAsync(string criterion, CancellationToken ct = default);
}

/// <summary>What an verifier is asked to rule on.</summary>
public sealed record VerificationRequest
{
    /// <summary>The node whose contract is being ruled against.</summary>
    public required PlanNode Node { get; init; }

    /// <summary>The tree as the verifier reads it — the record, not the node's account of it.</summary>
    /// <remarks>
    /// <b>The whole record, and not the node's projection of it.</b> What a node is shown is
    /// budgeted, because a node has work to do and a window to do it in; a ruling is made from
    /// outside the node, and one that inherited the node's omissions would be ruling on what the
    /// node happened to see.
    /// </remarks>
    public required string TreeView { get; init; }
}

/// <summary>How a node's contract stood up.</summary>
public enum VerificationOutcome
{
    /// <summary>Every acceptance criterion was checked and held.</summary>
    Passed = 0,

    /// <summary>At least one acceptance criterion was checked and failed.</summary>
    GateFailed,

    /// <summary>
    /// Some criterion had no check that could decide it, so nothing was ruled on it either way.
    /// </summary>
    Abstained,

    /// <summary>
    /// The node disputed its contract and nothing here could refute the dispute, so it travels
    /// upward unresolved.
    /// </summary>
    ViolationStands,

    /// <summary>
    /// The node disputed a criterion, and a check decided that criterion holds after all. The
    /// dispute does not travel.
    /// </summary>
    ViolationRefuted
}

/// <summary>An verifier's ruling.</summary>
public sealed record Verification
{
    /// <summary>Verdicts the verifier is prepared to stand behind.</summary>
    public required IReadOnlyList<CriterionVerdict> Verdicts { get; init; }

    /// <summary>The ruling.</summary>
    public required VerificationOutcome Outcome { get; init; }

    /// <summary>Criteria nothing could decide. Named, never silently treated as passing.</summary>
    public IReadOnlyList<string> Abstained { get; init; } = [];

    /// <summary>
    /// How the work ranks on its quality criteria, in <c>[0,1]</c>. Computed <b>only</b> when every
    /// gate passed — <see langword="null"/> otherwise, because a score alongside a failed gate is an
    /// invitation to average the two.
    /// </summary>
    public double? Score { get; init; }
}

/// <summary>
/// Rules on whether a node met its contract. External to the node, by construction.
/// </summary>
/// <remarks>
/// <para>
/// A node does not decide whether it satisfied its own contract. It reports what it observed and
/// returns; the altitude that authored the criterion rules, directly or through a delegate like
/// this. Verification cannot happen below the altitude that decided, and a node is below that
/// altitude by definition — which is also why the adapt-or-return distinction cannot be made from
/// inside a node: the two cases look identical from there.
/// </para>
/// <para>
/// <b>Authoritative over the record, never over the contract.</b> An verifier may rule that a
/// criterion was or was not met. It may not rule that a criterion is <em>wrong</em> — that judgement
/// belongs to whoever wrote it, and a dispute travels there regardless of what happens here.
/// </para>
/// <para>
/// Opt-in. A plan with no verifier has nothing verified and behaves exactly as before.
/// </para>
/// </remarks>
public interface IVerifier
{
    /// <summary>Rules on <paramref name="request"/>.</summary>
    Task<Verification> VerifyAsync(VerificationRequest request, CancellationToken ct = default);

    /// <summary>
    /// Which of <paramref name="criteria"/> nothing here could decide — answered <b>without running
    /// anything</b> — or <see langword="null"/> when this verifier cannot say in advance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Synchronous on purpose, and that is the ruling rather than a detail.</b> A decidability
    /// answer that costs what a ruling costs is not worth having — it would run the gates a second
    /// time to find out whether the gates exist. A signature with nothing to await is what stops one
    /// being written.
    /// </para>
    /// <para>
    /// <b><see langword="null"/> and an empty list are different facts.</b> Empty means every
    /// criterion has something prepared to decide it; <see langword="null"/> means nobody could say,
    /// which is what an verifier that would have to <em>do</em> the work to find out must answer.
    /// Reporting the second as the first is exactly the silence this exists to break.
    /// </para>
    /// <para>
    /// Defaults to <see langword="null"/>, so an verifier written before this existed claims
    /// nothing on its own behalf.
    /// </para>
    /// </remarks>
    IReadOnlyList<string>? CannotDecide(IReadOnlyList<string> criteria) => null;
}
