using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;

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
[Experimental(ExperimentalApis.PlanTier, Message = ExperimentalApis.PlanTierMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed record PlanNodeReport : IPlanNodeAnswer
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
    public required bool Done { get; init; }

    /// <summary>
    /// Every option the node found, one short line each for a person to read: those that meet its
    /// contract when <see cref="Done"/>, those that come close when it is not.
    /// </summary>
    [Description(
        "The choices you found, one short line each in words a person can act on. Never a reason "
        + "and never an operation.")]
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>What the node found about the world, each with where it can be checked again.</summary>
    /// <remarks>
    /// Unlike <see cref="Summary"/>, these are carried to whoever reads the step afterwards as data,
    /// because each names its source.
    /// </remarks>
    [Description(
        "Facts you found about the world, each with where it can be checked again. Empty when you "
        + "found nothing worth checking.")]
    public IReadOnlyList<NodeFinding> Findings { get; init; } = [];

    /// <inheritdoc />
    public NodeOutcome ToOutcome() => new()
    {
        Summary = Summary,
        Candidate = Operation,
        Done = Done,
        Options = Options,
        Findings = Findings
    };

    /// <inheritdoc />
    public static string Instructions(OperationCatalog? operations)
    {
        var text = new StringBuilder();

        // Without operations there is nothing to apply, so the step only reports what it found.
        text.Append(operations is null
            ? "Look up what your contract asks for with your tools, and say what you found in a sentence or two.\n\n"
            : "Propose the change that would satisfy your contract. You do not perform it yourself: "
              + "something outside you applies it, then checks whether it worked. Leave it out only if "
              + "this step has nothing of its own to change. Then say what you propose, or why nothing "
              + "needs to change, in a sentence or two.\n\n");

        text.Append("Set \"Done\" to true when what you found meets your contract, and false when it does ")
            .Append("not. List in \"Options\" the choices you found, one short line each that names the ")
            .Append("choice so a person can pick it: when \"Done\" is true, every choice that meets the ")
            .Append("contract; when it is false, every choice that comes close — the same thing on a smaller ")
            .Append("scale, on other dates or in another version, where the results show one. Take options ")
            .Append("only from what your tools returned — never advice, never a reason, and never something ")
            .Append("you did not look up — and keep the order the results gave them. Do not choose between ")
            .Append("them; whoever reads them decides. Why goes in your summary, not in \"Options\". If ")
            .Append("nothing comes close, leave \"Options\" empty.");

        // What may be proposed, rather than an invitation to invent one. A step asked for an
        // operation "in its own words" writes something plausible and unrecognised, and the run
        // spends a shape rejection discovering it.
        if (operations is { } catalog)
        {
            text.Append(" If you could not do it, propose nothing.\n\n")
                // Proposing is the whole of a step's part here, and its contract cannot be met while
                // it proposes: nothing has applied the change or checked it yet. Read the other way,
                // a step that proposed correctly reports it is not done, and the executor drops the
                // candidate it just made. Said only where operations exist — a step that reports
                // findings answers the question the advisor asks, which is a different one.
                .Append("Proposing a change is doing your part, so set \"Done\" to true when you ")
                .Append("propose one: something outside you applies it and checks it afterwards, and ")
                .Append("until that has happened no contract about the change can be met. Set ")
                .Append("\"Done\" to false only when you could not do your part at all — a tool or a ")
                .Append("service failed, what was asked was not clear enough to act on, or there is ")
                .Append("nothing you can propose.\n\n")
                // The proposal is the operation, so a step that proposed has nothing left to offer
                // anyone a choice between. Said here because a run put the whole apply_diff call
                // into an option, which a person would have been asked to pick between.
                .Append("When you propose a change, leave \"Options\" empty: the proposal is the ")
                .Append("operation, and there is nothing there for anyone to choose between. Options ")
                .Append("belong to the case where you could not do your part — one short line each, ")
                .Append("in words somebody can act on, never a diff and never an operation.\n\n")
                .Append("The operations that exist. Choose one and give its name and ")
                .Append("arguments exactly:\n\n").Append(catalog.Legend());
        }

        return text.ToString();
    }
}
