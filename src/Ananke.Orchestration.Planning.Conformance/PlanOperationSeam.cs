namespace Ananke.Orchestration.Planning.Conformance;

/// <summary>
/// A consumer's side of the operation seam: what a step may propose, what applies it, and two
/// sample operations the suite drives through it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The samples are the consumer's because the contract cannot guess them.</b> Every domain names
/// its own operations — <c>carry(place)</c>, <c>apply_diff(path, diff)</c> — so a suite that supplied
/// its own would only ever prove that a catalog it built admits an operation it invented. What is
/// proved here is that <em>this</em> catalog and <em>this</em> applier behave as the tier expects
/// when the tier drives them.
/// </para>
/// <para>
/// Several scenarios turn on whether the applier was reached at all, and a delegate cannot be asked
/// afterwards — so the suite wraps <see cref="Applier"/> in a counter of its own rather than asking
/// the subject to report calls. Nothing here needs the consumer to instrument anything.
/// </para>
/// </remarks>
public sealed record PlanOperationSeam
{
    /// <summary>What a step in this domain may propose.</summary>
    public required OperationCatalog Operations { get; init; }

    /// <summary>What applies an admitted proposal to this domain's world.</summary>
    public required PlanCandidateApplier Applier { get; init; }

    /// <summary>
    /// An operation <see cref="Operations"/> admits and <see cref="Applier"/> can apply against a
    /// freshly built subject.
    /// </summary>
    public required Operation Admissible { get; init; }

    /// <summary>
    /// An operation <see cref="Operations"/> refuses on shape — an unknown name, or a known one at
    /// the wrong arity.
    /// </summary>
    /// <remarks>
    /// <b>Refused on shape, never on referents.</b> An operation naming somewhere that does not
    /// exist is admissible: the candidate reaches the world regardless, and what the world says is
    /// ordinary evidence for the acceptance gate. A catalog that refuses on referents has decided
    /// the work's outcome by declining to attempt it, and this field must not carry such a case.
    /// </remarks>
    public required Operation Refused { get; init; }
}
