using System.ComponentModel;

namespace Ananke.Orchestration.Planning;

/// <summary>
/// What a node proposes doing, and what it says about proposing it. Narration and a candidate,
/// never a ruling.
/// </summary>
/// <remarks>
/// <b>There is no field here for a verdict.</b> The node proposes an operation and never performs
/// it — <see cref="PlanExecutor"/> hands <see cref="Operation"/> to whatever the supervision
/// configured to apply it, once the shape gate has looked at it. What is checked afterwards is
/// checked by something that did not propose it. <see cref="Done"/> and <see cref="Options"/> are
/// the node's own account of whether it could do its task; the loop marks the step's state from
/// them, and a failing check still stops the pass regardless of what the node reported.
/// </remarks>
public sealed record PlanNodeReport
{
    /// <summary>What the node proposes, or why it proposes nothing, in a sentence or two.</summary>
    /// <remarks>
    /// Narration, and never evidence: nothing derives from it and it never becomes a verdict. It is
    /// nevertheless <em>kept</em>, on the node's last reading, because it is the only account of what
    /// the node actually attempted — and whoever has to decide what a halted plan should become would
    /// otherwise be reasoning about work nobody described.
    /// </remarks>
    [Description("What you propose, or why you propose nothing, in a sentence or two.")]
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// The operation to apply — <see langword="null"/> when this node has nothing of its own to
    /// propose.
    /// </summary>
    /// <remarks>
    /// <b>Carried, never performed.</b> A node that could apply its own candidate has stopped
    /// proposing and started acting on nobody's authority but its own, which this design does not
    /// allow. Left <see langword="null"/> by a node that only decomposes; a leaf whose contract
    /// calls for a real change is expected to fill it, and an unnamed operation here — attempted
    /// and produced nothing — is what the shape gate retries against, distinctly from never having
    /// tried.
    /// <para>
    /// <b>An invocation rather than a sentence.</b> It was prose, and every domain that applied one
    /// had to parse English to find the change inside it.
    /// </para>
    /// </remarks>
    [Description(
        "The change you propose, as an operation from the legend and its arguments. Null when this "
        + "step has nothing of its own to change.")]
    public Operation? Operation { get; init; }

    /// <summary>
    /// Whether the node could do its task. <see langword="false"/> when what it found means its
    /// contract cannot be met as written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Never a verdict: the loop marks the step's state from it, and a failing check still stops
    /// the pass regardless of what the node reported.
    /// </para>
    /// <para>
    /// <b>A step that proposes an operation is done once it has proposed one.</b> Its contract is
    /// about the change, and nothing has applied or checked the change at that point, so reading
    /// this as "is my contract already met" would make every proposing step report false — and a
    /// step that reports false has its candidate dropped before anything can apply it. A step that
    /// reports findings instead answers the other question, which is what the advisor reads to tell
    /// options that meet the contract from options that only come close.
    /// </para>
    /// </remarks>
    [Description(
        "Whether you could do your task. When you propose a change, proposing it is your part and "
        + "this is true; otherwise it is whether what you found meets your contract.")]
    public bool Done { get; init; } = true;

    /// <summary>
    /// Every option the node found, one short line each for a person to read: those that meet its
    /// contract when <see cref="Done"/>, those that come close when it is not.
    /// </summary>
    [Description(
        "The choices you found, one short line each in words a person can act on. Never a reason "
        + "and never an operation.")]
    public IReadOnlyList<string> Options { get; init; } = [];
}
