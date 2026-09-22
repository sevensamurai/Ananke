namespace Ananke.Orchestration.Planning;

/// <summary>
/// One change of plan a supervisor is putting forward, as the fields it is made of.
/// </summary>
/// <remarks>
/// <para>
/// <b>It never authors a decomposition.</b> Which steps a plan has is the Planner's, and an option
/// that could add, drop or split one would be this seat authoring a plan — the move four consecutive
/// live runs reached for and none of them could express. An option gives the step up
/// (<see cref="Abandon"/>), asks for a replan (<see cref="Replan"/>), or answers the step's own
/// question (<see cref="Answer"/>) — never two of the three.
/// </para>
/// <para>
/// <b>Not a <see cref="PlanDecision"/>, and that is not an oversight.</b> An alternative exists to be
/// shown to somebody, which means the run is very often paused while it is — and a paused run is
/// written down and read back. <c>System.Text.Json</c> will not deserialise an abstract type, so a
/// decision object in workflow state fails on resume, a long way from where it was put there. The
/// fields survive; the polymorphism does not, and it buys nothing here.
/// </para>
/// <para>
/// <b>Whether it is recommended is the supervisor's judgement</b> and travels with the option rather
/// than beside it, so nothing downstream has to hold two lists in step.
/// </para>
/// </remarks>
public sealed record PlanOption
{
    /// <summary>One line a person can weigh this against the others by.</summary>
    public required string Summary { get; init; }

    /// <summary>
    /// What to tell a step that is waiting to be told something, when that is what this option is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An option gives the step up, asks for a replan, or answers — never two of the three.</b>
    /// Answering says the plan is right and a choice inside the work has been made. Taking an answer
    /// <b>mints no version and re-words no criterion</b> — which is the whole of what a local fix
    /// means, and the cheapest thing this seat does.
    /// </para>
    /// <para>
    /// <b>The step supplied these, not the supervisor.</b> It found the admissible values by querying
    /// the facts; what it could not do is weigh them, so what is asked of the supervisor is a
    /// recommendation and nothing else.
    /// </para>
    /// </remarks>
    public string? Answer { get; init; }

    /// <summary>Why this option answers the halt, attributed to whoever concluded it.</summary>
    public PlanRationale? Rationale { get; init; }

    /// <summary>The supervisor's own pick of the alternatives it offered.</summary>
    public bool Recommended { get; init; }

    /// <summary>
    /// When set, taking this option asks for the plan to be changed, without saying what the change
    /// is — that is the Planner's to write, from <see cref="Rationale"/> and the rest of what halted.
    /// </summary>
    /// <remarks>
    /// <b>Choosing it mints no version by itself.</b> It signals <see cref="PlanDecision.ReplanPlan"/>
    /// with no contract, which the Planner reads and re-authors from; the version it mints is the
    /// Planner's, not this option's.
    /// </remarks>
    public bool Replan { get; init; }

    /// <summary>
    /// When set, taking this option gives the step up rather than changing it — and this is the
    /// reason, in the words of whoever offered it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>R32: dropping one step is an authored option.</b> It used to be a refusal — <em>none of
    /// these, and give this up</em> — which made it the one course of action a person could take that
    /// the supervisor never had to think about, weigh against the others, or say whether it was the
    /// one it would take. Here it is an option like any other: it competes, it can be recommended,
    /// and an unattended run can take it.
    /// </para>
    /// <para>
    /// <b>It gives up a step, never the run.</b> Cancelling is <see cref="PlanRefusal.Cancel"/>, is
    /// nobody's to author, and is not this. Taking this one abandons the halted node, mints no
    /// version, and lets the rest of the plan carry on — the work already done stays recorded, and
    /// what the step was holding is released by whoever knows what that means.
    /// </para>
    /// <para>
    /// <b>The reason is load-bearing.</b> A step given up with no reason reads afterwards exactly like
    /// a step that failed, and the two lead somewhere completely different.
    /// </para>
    /// </remarks>
    public string? Abandon { get; init; }
}

/// <summary>
/// What a supervisor offers at a halt: two or three real alternatives, one of them recommended.
/// </summary>
/// <remarks>
/// <para>
/// <b>Offering one proposal is a decision taken, not a decision put.</b> <em>This change, or stop</em>
/// reads as a question and is a veto — the choosing already happened, out of sight, and whoever is
/// asked can only refuse. Asking properly means alternatives that are genuinely different from each
/// other, and saying which one you would take.
/// </para>
/// <para>
/// <b>Cancel is not among them.</b> A way out is not a judgement, so it is not the judge's to
/// withhold: whatever puts the question appends it. A supervisor that forgot would leave somebody
/// with no way to stop, and nothing downstream would notice.
/// </para>
/// <para>
/// <b>This is the shape a consumer reaches for first</b>, which is why it ships rather than being
/// assembled per plan. A coordinator answers <em>what would you do</em>; this answers <em>what are my
/// options</em>, and they are not the same question however similar the call looks.
/// </para>
/// </remarks>
public sealed record PlanProposal
{
    /// <summary>The alternatives, in the order they should be shown.</summary>
    public required IReadOnlyList<PlanOption> Options { get; init; }

    /// <summary>The one the supervisor would take, or <see langword="null"/> if it named none.</summary>
    public PlanOption? Recommended => Options.FirstOrDefault(o => o.Recommended);

    /// <summary>Nothing was proposed, so there is nothing to choose between.</summary>
    public bool Empty => Options.Count == 0;

    /// <summary>
    /// The supervisor ran out of tool rounds before it answered, rather than finishing with nothing.
    /// </summary>
    /// <remarks>
    /// <b>Two empties that mean opposite things.</b> <em>I looked and there is nothing</em> is a
    /// statement about the plan and belongs to whoever can re-author it. <em>I did not finish
    /// looking</em> is a statement about a budget, and re-planning on the strength of it would spend
    /// an expensive seat on an accident and hide a tool loop that is going round.
    /// </remarks>
    public bool Exhausted { get; init; }

    /// <summary>
    /// What was offered and not kept, and why — one line each.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A discarded option is a fact about the run, not an absence of one.</b> Every guard here
    /// removes work that could not be used — an option with no criteria, one naming a check that
    /// does not exist, one whose step is its own child, one whose id means nothing in the domain —
    /// and each of them is right to. Dropping them silently is what makes *the supervisor thought of
    /// nothing* indistinguishable from *it thought of three things and none could be applied*, which
    /// are different problems with different fixes.
    /// </para>
    /// <para>
    /// <b>Empty with reasons is the interesting case.</b> A proposal that is empty and says nothing
    /// was a role with no ideas; one that is empty and lists three refusals was a role with ideas
    /// that the wiring would not take.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> Discarded { get; init; } = [];

    /// <summary>
    /// What the supervisor read as settled for the rest of the run, rather than as an answer to this
    /// halt. Empty unless somebody answered off the list and said something that outlives it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Read narrowly, and the narrowness is the ruling.</b> When it is unclear whether something is
    /// an answer or a term, <b>it is an answer</b> — a term binds every version authored after it, so
    /// one invented out of an offhand remark quietly narrows a plan nobody agreed to narrow, and does
    /// it in a way that reads afterwards like somebody asked for it.
    /// </para>
    /// <para>
    /// <b>Committed by whatever asked, not by this.</b> A proposal states what it read; putting it
    /// where later versions will be bound by it is a write, and this type does not do writes.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> Terms { get; init; } = [];
}

/// <summary>
/// Offers the changes of plan a halt admits, and says which one it would take.
/// </summary>
/// <remarks>
/// <para>
/// The role beside <see cref="PlanSupervisor"/> rather than a replacement for it. A coordinator
/// <em>decides</em>, which is what an unattended run needs; an advisor <em>offers</em>, which is what
/// anything asking somebody else needs. A run may have both, and a run that escalates needs the
/// second.
/// </para>
/// <para>
/// <b>It is asked at a wall, which is where understanding is needed</b> — so this is a model's work
/// and not a program's. A rules table can implement it, and for a fixed scenario that is honest; what
/// it must not be is a program parsing the contract's prose to work out what the alternatives are.
/// </para>
/// </remarks>
public delegate Task<PlanProposal> PlanAdvisor(PlanCoordination coordination, CancellationToken ct);

/// <summary>
/// How somebody refused every option they were shown. One way, because there is only one thing a
/// refusal can mean that an option cannot.
/// </summary>
/// <remarks>
/// <para>
/// <b>It carried two values and now carries one, and losing the second is the ruling.</b>
/// <em>Give this step up</em> used to live here, beside <em>stop</em>, because a person with no
/// acceptable option had no other way to say it. R32 puts it back where it belongs: <b>dropping one
/// step is an authored option</b> — <see cref="PlanOption.Abandon"/> — offered by the supervisor like
/// any other, weighed against the others, and picked rather than refused into.
/// </para>
/// <para>
/// <b>What is left is the one answer nobody may author.</b> Cancelling is not a judgement about the
/// plan and not a course of action within it; it is somebody deciding the run is over. A supervisor
/// that could offer it would be offering a way to overrule itself, and one that forgot to offer it
/// would leave a person with no way out — so it is appended by whatever puts the question, always,
/// and is never in the list.
/// </para>
/// </remarks>
public enum PlanRefusal
{
    /// <summary>Stop the run and leave the plan as it stands. The person has taken the decision.</summary>
    Cancel = 0
}

/// <summary>
/// A question put to somebody, and what came back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Neither refusal is one of the <see cref="Options"/>.</b> They are separate answers, so that a
/// supervisor cannot fail to offer them and a caller cannot forget to append them. A person with no
/// acceptable option still needs a way out, and making that structural rather than conventional is
/// the whole of R3.
/// </para>
/// <para>
/// <b>Outstanding is the load-bearing state.</b> A question nobody has answered must not let the run
/// advance — not because a job checks, but because the topology routes an outstanding question back
/// to where it was asked. Silence is not an answer, and a run that treated it as one would be
/// deciding on somebody's behalf while appearing to consult them.
/// </para>
/// </remarks>
public sealed record PlanQuestion
{
    /// <summary>The alternatives, in the order they were shown.</summary>
    public required IReadOnlyList<PlanOption> Options { get; init; }

    /// <summary>Which was taken, 1-based, or <see langword="null"/> until somebody answers.</summary>
    public int? Picked { get; init; }

    /// <summary>How every option was refused, if they were. <see langword="null"/> until somebody answers.</summary>
    public PlanRefusal? Refused { get; init; }

    /// <summary>Why they were refused, in the words of whoever refused them.</summary>
    public string? RefusalReason { get; init; }

    /// <summary>
    /// What somebody said instead — an answer nobody listed. <see langword="null"/> unless this is
    /// how the question was answered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The third shape of answer, and it is a shape rather than a string for a reason.</b> Picking
    /// an option, saying something else, and cancelling are three different things and everything
    /// downstream has to tell them apart: a pick is applied, a cancel ends the run, and this is
    /// neither — it goes <b>back to the supervisor</b>, which reads it and decides afresh with it in
    /// hand. Carried in its own field so that discerning them is a type question rather than a
    /// parsing one.
    /// </para>
    /// <para>
    /// <b>It is what makes narrowing safe.</b> A short list is only acceptable because it cannot trap
    /// anybody, and this is the half of that guarantee the supervisor does not author. Nothing
    /// validates it, nothing matches it against the options, and it is never treated as a pick that
    /// happened to be phrased badly — <b>whoever answered meant something the list did not contain</b>,
    /// which is exactly the case a narrowed list has to survive.
    /// </para>
    /// <para>
    /// <b>Kept verbatim, and it outlives being read</b> (R33). Everything downstream acts on the
    /// supervisor's reading of it, so the sentence it read has to stay beside that reading rather than
    /// being replaced by it.
    /// </para>
    /// </remarks>
    public string? Said { get; init; }

    /// <summary>Nobody has answered, so nothing may move.</summary>
    public bool Outstanding => Picked is null && Refused is null && Said is null;

    /// <summary>What was actually chosen, if anything was.</summary>
    public PlanOption? Chosen =>
        Picked is { } at && at >= 1 && at <= Options.Count ? Options[at - 1] : null;

    /// <summary>What the supervisor would have taken.</summary>
    public PlanOption? Recommended => Options.FirstOrDefault(o => o.Recommended);

    /// <summary>Whether the answer that came back was the supervisor's own recommendation.</summary>
    /// <remarks>
    /// Recorded because <em>agreed with</em> and <em>complied with</em> are different facts, and an
    /// audit of a steered run is most likely to want the first.
    /// </remarks>
    public bool TookTheRecommendation => Chosen is { Recommended: true };
}
