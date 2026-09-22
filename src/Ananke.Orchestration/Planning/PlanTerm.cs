namespace Ananke.Orchestration.Planning;

/// <summary>
/// Why a term stopped holding — and the two answers are about different things.
/// </summary>
/// <remarks>
/// <b>They look identical in a record and mean opposite things.</b> A person taking a preference back
/// is a statement about <em>what they want</em>, and it is true from now on. A run letting one go
/// because this plan could not hold it is a statement about <em>this plan</em> — the dates were wrong,
/// the trip was full — and says nothing about whether they still want it next time. Anything carrying
/// a term beyond this run has to be able to tell them apart, because one should follow and the other
/// must not.
/// </remarks>
public enum PlanTermEnd
{
    /// <summary>Somebody took it back. They wanted this, and now they want something else.</summary>
    Retracted = 0,

    /// <summary>
    /// This plan could not hold it, and it was let go to get the run moving. A concession, not a
    /// change of mind.
    /// </summary>
    Waived
}

/// <summary>
/// Something settled about the plan that outlives the halt it was settled at.
/// </summary>
/// <remarks>
/// <para>
/// <b>The story this exists for is the one where a run makes somebody say the same thing three
/// times.</b> Asked what to do about a step that will not fit, a person answers in their own words —
/// <em>keep Naoshima; find the slack somewhere else</em> — and that is two things, not one: an answer
/// to the question asked, and a preference about the plan as a whole. Consumed only as the first, it
/// dies with the halt that produced it, and the next planner proposes dropping Naoshima again.
/// </para>
/// <para>
/// <b>So it is kept on the plan rather than on the halt</b> — <see cref="PlanTree.Terms"/>, beside the
/// lineage rather than inside a version, because a term is not something a version changed. Every
/// re-ruling afterwards binds it into the contract it authors, whichever node halted and however many
/// versions later.
/// </para>
/// <para>
/// <b>Both sentences, kept apart (R33).</b> <see cref="Said"/> is what somebody actually wrote and
/// <see cref="Reading"/> is what the supervisor made of it. Everything downstream acts on the reading,
/// which is exactly why the words it was read from have to survive next to it — a reader who cannot
/// check one against the other has a record that looks like testimony and is partly an inference.
/// </para>
/// <para>
/// <b>Revoked, never deleted.</b> A term that stopped holding is a fact about the run, and the reason
/// it stopped is the part no diff could recover afterwards.
/// </para>
/// </remarks>
public sealed record PlanTerm
{
    /// <summary>Identifies it, so it can be revoked without being described again.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// What it binds, as the supervisor read it — the sentence a contract will carry.
    /// </summary>
    public required string Reading { get; init; }

    /// <summary>
    /// The words somebody actually used, verbatim. Never replaced by <see cref="Reading"/>.
    /// </summary>
    public required string Said { get; init; }

    /// <summary>Who said it.</summary>
    public required string By { get; init; }

    /// <summary>When it was committed.</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>Why it no longer holds, or <see langword="null"/> while it does.</summary>
    public string? Contradicted { get; init; }

    /// <summary>When it stopped holding.</summary>
    public DateTimeOffset? ContradictedAt { get; init; }

    /// <summary>
    /// Whether somebody took it back or this plan merely could not hold it. <see langword="null"/>
    /// while it still holds.
    /// </summary>
    public PlanTermEnd? End { get; init; }

    /// <summary>Who ended it — which is not always who said it.</summary>
    public string? EndedBy { get; init; }

    /// <summary>Whether it still binds what gets authored next.</summary>
    public bool InForce => Contradicted is null;
}
