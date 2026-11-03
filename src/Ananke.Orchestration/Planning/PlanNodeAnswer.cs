using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Ananke.Orchestration.Tools;

namespace Ananke.Orchestration.Planning;

/// <summary>
/// What a step answers when it runs as an agent job: the shape it is asked to fill, how it is told to
/// fill it, and what that means to the executor.
/// </summary>
/// <remarks>
/// <para>
/// <b>The shape a step is asked for is its type.</b> The schema a model fills is generated from the
/// answer's type, so a step can only be asked for fewer fields by being given a different type. A step
/// that may change nothing is not asked what it proposes: an empty slot for a change is an invitation
/// to fill it, and a model given one with no list of what exists invents an operation.
/// </para>
/// <para>
/// <b>Every answer comes back as one <see cref="NodeOutcome"/>.</b> The executor records, gates and
/// rules on outcomes; how an answer maps onto one belongs to the answer, so a new kind of step is a
/// new type and not a new branch in the runner.
/// </para>
/// </remarks>
[Experimental(ExperimentalApis.PlanTier, Message = ExperimentalApis.PlanTierMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public interface IPlanNodeAnswer
{
    /// <summary>What this answer means to the executor.</summary>
    NodeOutcome ToOutcome();

    /// <summary>
    /// How a step is told to fill this answer in, at the end of its turn-one prompt.
    /// </summary>
    /// <param name="operations">
    /// The operations the step may propose, or <see langword="null"/> when it may propose none.
    /// </param>
    static abstract string Instructions(OperationCatalog? operations);

    /// <summary>
    /// Reads this answer from the step's last words, for an answer that is nothing but them; or
    /// <see langword="null"/>, the default, for one the step is asked for as JSON.
    /// </summary>
    static virtual PlanNodeAnswerFromText? FromText => null;

    /// <summary>
    /// Whether this answer has no slot for an operation, so a step proposes only by calling one: its
    /// runner then needs <see cref="PlanNodeAgentOptions{TAnswer}.Operations"/> offered as tools
    /// (<see cref="PlanNodeAgentOptions{TAnswer}.OperationsAsTools"/>). <see langword="false"/> by default.
    /// </summary>
    static virtual bool RequiresOperationsAsTools => false;

    /// <summary>
    /// The tool a step calls to give this answer, offered when
    /// <see cref="PlanNodeAgentOptions{TAnswer}.AnswerAsTool"/> is on: a call hands the answer to
    /// <paramref name="answered"/> and ends the step's job. <see langword="null"/>, the default, for an
    /// answer that cannot be given by a call.
    /// </summary>
    /// <param name="answered">Given the answer a call carried.</param>
    static virtual ToolKit? Tool(PlanNodeAnswerByCall answered) => null;
}

/// <summary>Reads a step's answer from its last words (<see cref="IPlanNodeAnswer.FromText"/>).</summary>
[Experimental(ExperimentalApis.PlanTier, Message = ExperimentalApis.PlanTierMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public delegate IPlanNodeAnswer PlanNodeAnswerFromText(string said);

/// <summary>Given the answer a step's call carried (<see cref="IPlanNodeAnswer.Tool"/>).</summary>
[Experimental(ExperimentalApis.PlanTier, Message = ExperimentalApis.PlanTierMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public delegate void PlanNodeAnswerByCall(IPlanNodeAnswer answer);

/// <summary>
/// What a step that changes nothing answers: what it found, each finding with where it can be checked
/// again. No operation, no options, no flag.
/// </summary>
/// <remarks>
/// <b>Nothing to mark it done or not.</b> Whether a step that reports findings did its task is what
/// its criteria are for, ruled on by something that did not do the work; a flag the step sets itself
/// would be a second, weaker answer to the same question.
/// </remarks>
[Experimental(ExperimentalApis.PlanTier, Message = ExperimentalApis.PlanTierMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed record PlanNodeFindings : IPlanNodeAnswer
{
    // Shared by the schema and the tool, so an answer asked for as JSON and one given by a call
    // describe their fields in the same words.
    private const string SummaryMeans = "What you found, in a sentence or two.";

    private const string FindingsMean =
        "Facts you found about the world, each with where it can be checked again. Empty when you "
        + "found nothing worth checking.";

    private static readonly JsonSerializerOptions Reading = new() { PropertyNameCaseInsensitive = true };

    /// <summary>What the step found, in its own words. Narration, as on <see cref="PlanNodeReport.Summary"/>.</summary>
    [Description(SummaryMeans)]
    public string Summary { get; init; } = string.Empty;

    /// <summary>What the step found about the world, each with where it can be checked again.</summary>
    [Description(FindingsMean)]
    public IReadOnlyList<NodeFinding> Findings { get; init; } = [];

    /// <inheritdoc />
    public NodeOutcome ToOutcome() => new() { Summary = Summary, Findings = Findings };

    /// <inheritdoc />
    /// <remarks>
    /// <c>report_findings</c>, whose parameters are this answer's fields. A call whose findings cannot be
    /// read as a list is answered with why and does not end the step, so the model can call again.
    /// </remarks>
    public static ToolKit Tool(PlanNodeAnswerByCall answered)
    {
        ArgumentNullException.ThrowIfNull(answered);

        return new ToolKit("answer").AddTool(
            "report_findings",
            "Report what you found. The call is your answer and ends your turn: make it once, when you have "
            + "looked up what your contract asks for.",
            b => b
                .Param<string>(nameof(Summary), SummaryMeans)
                .ParamList<NodeFinding>(nameof(Findings), FindingsMean)
                .EndsJob()
                .OnExecute(args =>
                {
                    List<NodeFinding> findings;

                    try
                    {
                        findings = args.GetOrDefault(nameof(Findings)) is { } listed
                            ? JsonSerializer.Deserialize<List<NodeFinding>>(listed, Reading) ?? []
                            : [];
                    }
                    catch (JsonException ex)
                    {
                        return ToolResult.Error(
                            $"report_findings: {nameof(Findings)} could not be read as a list of findings: {ex.Message}");
                    }

                    answered(new PlanNodeFindings
                    {
                        Summary = args.GetOrDefault(nameof(Summary)) ?? string.Empty,
                        Findings = findings
                    });

                    // The step does not read this: its job ends at the call. A conversation kept
                    // across steps does, so it says only what happened.
                    return ToolResult.Ok($"Reported {findings.Count} finding(s).");
                }));
    }

    /// <inheritdoc />
    /// <remarks>The same whatever <paramref name="operations"/> holds: this answer has nowhere to put one.</remarks>
    public static string Instructions(OperationCatalog? operations) =>
        "Look up what your contract asks for with your tools. Report each fact you found as a finding, "
        + "with where it can be checked again, and say what you found in a sentence or two.";
}

/// <summary>
/// What a step that proposes by calling its operation answers: what it proposed, in its own words. The
/// proposal itself is the call, and it ends the step's turn.
/// </summary>
/// <remarks>
/// <para>
/// <b>No operation, flag or options to fill.</b> With
/// <see cref="PlanNodeAgentOptions{TAnswer}.OperationsAsTools"/>, a step proposes by calling the
/// operation, so an answer with a slot for one asks for the same thing twice.
/// </para>
/// <para>
/// <b>What the step wrote with its call is its summary</b>, since nothing is asked after the call. A
/// step that calls nothing is read from its last words (<see cref="FromText"/>), not asked for them
/// as JSON: a schema for them asks the same thing a second time.
/// </para>
/// <para>
/// <b>No legend of the operations.</b> They are the step's tools, each with its own description, and a
/// second description in prose is one the two can disagree with.
/// </para>
/// </remarks>
[Experimental(ExperimentalApis.PlanTier, Message = ExperimentalApis.PlanTierMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed record PlanNodeProposal : IPlanNodeAnswer
{
    /// <summary>What the step proposed and why, in its own words. Narration, as on <see cref="PlanNodeReport.Summary"/>.</summary>
    [Description("What you proposed and why, in a sentence or two.")]
    public string Summary { get; init; } = string.Empty;

    /// <inheritdoc />
    /// <remarks>No candidate: the runner takes the operation the step called.</remarks>
    public NodeOutcome ToOutcome() => new() { Summary = Summary };

    /// <inheritdoc />
    public static PlanNodeAnswerFromText? FromText => said => new PlanNodeProposal { Summary = said };

    /// <inheritdoc />
    /// <remarks>The call is the proposal: with no operation to call, every step would propose nothing.</remarks>
    public static bool RequiresOperationsAsTools => true;

    /// <inheritdoc />
    public static string Instructions(OperationCatalog? operations) =>
        $"Propose by calling one of your operations — {Names(operations)} — and say with the call, in a "
        + "sentence or two, what you propose and why: the call ends your turn. Call exactly one; if none "
        + "fits, call none and say why.";

    private static string Names(OperationCatalog? operations) =>
        operations is null
            ? "none are offered"
            : string.Join(" or ", operations.Definitions.Select(definition => definition.Name).Distinct(StringComparer.Ordinal));
}
