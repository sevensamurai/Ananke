using System.ComponentModel;
using System.Text;
using System.Text.Json.Serialization;

namespace Ananke.Orchestration.Planning;

/// <summary>
/// A change a step proposes, written as something that resolves: an operation's name and its
/// arguments.
/// </summary>
/// <remarks>
/// <para>
/// <b>The symmetry with <see cref="Criterion"/> is the point.</b> A plan's assertions were already
/// invocations — <c>carried(hakone)</c>, resolved by name and arity against a registry that can say
/// what exists. Its <em>actions</em> were a sentence, "the operation to apply, in the domain's own
/// words", so every domain implementing an applier had to write a parser for English. The one that
/// did wrote a regex, and lost two runs to <c>carry hakone, staying from the 2nd</c> and
/// <c>carry hakone, noting its onsen</c> — both sensible, both unmatchable, and neither a fact about
/// the trip.
/// </para>
/// <para>
/// <b>An operation is not a criterion, though they share a grammar.</b> One asserts, the other acts,
/// and a type that let either stand where the other was meant would make the distinction
/// unenforceable. The grammar is shared deliberately: an invocation is an invocation, and a domain
/// that has learned to read one has learned to read both.
/// </para>
/// <para>
/// <b>There is no <c>Parse</c>, deliberately.</b> A criterion has one because
/// <see cref="Ananke.Orchestration.Agents.Context.AgentContract.AcceptanceCriteria"/> stores criteria
/// as text, so anything wanting the structure has to recover it. An operation is never text: a step
/// fills this record, the applier receives this record, and nothing in between renders it to a string
/// to read back. Adding a parser here would reintroduce, one type over, the thing this type removed —
/// <see cref="ToString"/> exists for a reader and has no inverse on purpose.
/// </para>
/// </remarks>
public sealed record Operation
{
    /// <summary>The operation to perform.</summary>
    [Description("The operation to perform, named exactly as the legend gives it.")]
    public required string Name { get; init; }

    /// <summary>What it is being asked to act on, in order.</summary>
    [Description("What it acts on, in order, one entry per argument the operation takes.")]
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>
    /// Whether this names something at all — as opposed to a step that tried and produced nothing.
    /// </summary>
    /// <remarks>
    /// The two are different facts and the shape gate treats them differently: a step with nothing
    /// to propose is ordinary, and a step that proposed a nameless operation is one to ask again.
    /// <para>
    /// Kept out of a generated response schema: it is computed from <see cref="Name"/>, and a
    /// generator that emits a property also marks it required — which asks a model to supply a value
    /// this type has no way to accept.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public bool IsNamed => !string.IsNullOrWhiteSpace(Name);

    /// <summary>The argument at <paramref name="index"/>, or <see langword="null"/> if there is none.</summary>
    public string? Argument(int index) =>
        index >= 0 && index < Arguments.Count ? Arguments[index] : null;

    /// <inheritdoc />
    public override string ToString()
    {
        var text = new StringBuilder(Name).Append('(');

        for (var i = 0; i < Arguments.Count; i++)
        {
            if (i > 0)
                text.Append(", ");

            text.Append(Arguments[i]);
        }

        return text.Append(')').ToString();
    }
}

/// <summary>
/// One operation a plan's steps may propose: what it is called, how many arguments it takes, and
/// what it does.
/// </summary>
/// <remarks>
/// <b>The arity is part of the identity</b>, exactly as it is for a check. <c>carry(place)</c> and
/// <c>carry(place, way)</c> are different operations, and a catalog that accepted either would apply
/// one of them wrongly rather than refusing.
/// </remarks>
public sealed record OperationDefinition
{
    /// <summary>What a step names to propose it.</summary>
    public required string Name { get; init; }

    /// <summary>How many arguments it takes.</summary>
    public required int Arity { get; init; }

    /// <summary>What it does, in a sentence — for a reader, and for the step choosing between them.</summary>
    public required string Description { get; init; }

    /// <summary>
    /// Why these arguments are not ones this operation can be asked for, or <see langword="null"/>
    /// when they are.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Shape only, never referents.</b> "That is not one of the two windows" is this delegate's
    /// business, because the set of windows is part of the operation's form. "There is nowhere called
    /// atlantis" is not: the candidate reaches the world regardless of whether its target is real,
    /// and what the world says becomes ordinary evidence for the acceptance gate to find. An applier
    /// that refuses on referents has decided the work's outcome by declining to attempt it.
    /// </para>
    /// <para>
    /// Optional. An operation with nothing to say about its arguments leaves this null.
    /// </para>
    /// </remarks>
    public Func<Operation, string?>? Validate { get; init; }

    /// <summary>
    /// One worked instance of this operation, shown with the legend, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <b>Rendered verbatim, at the left margin, and never re-indented.</b> An example whose
    /// whitespace the legend adjusts is an example a step can copy into an argument where the
    /// whitespace is part of the value — a diff hunk being the case that matters. A definition
    /// without one renders exactly as it always has.
    /// </remarks>
    public string? Example { get; init; }
}

/// <summary>
/// The operations a plan's steps may propose, and what each one means.
/// </summary>
/// <remarks>
/// <para>
/// <b>Enumerable, so a step chooses rather than composes.</b> A step told to describe a change "in
/// its own words" is being asked to invent a vocabulary, and it will — the run that produced this
/// type saw a supervisor author <c>onsen_booked(hakone)</c>, <c>carried(nikko-overlook)</c> and a
/// date range the scenario had never mentioned. The catalog is what a step is shown instead.
/// </para>
/// <para>
/// <b>What it is not is a gate on the work.</b> It rules on whether a proposal is an operation, and
/// stops there. Whether the operation succeeds is the world's to say.
/// </para>
/// </remarks>
public sealed class OperationCatalog
{
    private readonly Dictionary<(string Name, int Arity), OperationDefinition> _definitions;

    /// <summary>Creates a catalog over <paramref name="definitions"/>.</summary>
    public OperationCatalog(IEnumerable<OperationDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        _definitions = definitions.ToDictionary(
            d => (d.Name, d.Arity),
            OperationKeyComparer.Instance);
    }

    /// <summary>The operations a step may propose.</summary>
    public IReadOnlyCollection<OperationDefinition> Definitions => _definitions.Values;

    /// <summary>
    /// Why <paramref name="operation"/> is not something a step may propose, or <see langword="null"/>
    /// when it is.
    /// </summary>
    /// <remarks>
    /// A shape finding and never a verdict: what comes back here is carried verbatim into the next
    /// attempt so the step can correct itself, which only works while it describes the proposal
    /// rather than the work.
    /// </remarks>
    public string? Admits(Operation? operation)
    {
        if (operation is null || !operation.IsNamed)
            return "no operation was proposed.";

        if (!_definitions.TryGetValue((operation.Name, operation.Arguments.Count), out var definition))
        {
            var known = _definitions.Values
                .Where(d => string.Equals(d.Name, operation.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return known.Count > 0
                ? $"'{operation.Name}' takes {string.Join(" or ", known.Select(d => d.Arity))} "
                  + $"argument(s), and {operation.Arguments.Count} were given."
                : $"there is no operation called '{operation.Name}'. What there is:\n{Legend()}";
        }

        return definition.Validate?.Invoke(operation);
    }

    /// <summary>What may be proposed, for whoever is choosing between them.</summary>
    /// <remarks>
    /// The same answer <see cref="InvocationCheck.Legend"/> gives an author writing criteria. A step
    /// left to guess writes something plausible and undecidable, which is the failure this removes
    /// for the operations it knows and cannot remove for invention.
    /// </remarks>
    public string Legend() =>
        string.Join(
            "\n",
            _definitions.Values
                .OrderBy(d => d.Name, StringComparer.Ordinal)
                .ThenBy(d => d.Arity)
                .Select(Describe));

    /// <summary>One operation as the legend shows it: its shape, what it does, and one instance.</summary>
    private static string Describe(OperationDefinition definition)
    {
        var line = $"  {definition.Name}({string.Join(", ", Enumerable.Range(1, definition.Arity)
            .Select(i => $"arg{i}"))}) — {definition.Description}";

        return definition.Example is { Length: > 0 } example
            ? $"{line}\n  One that would be accepted, exactly as written:\n{example}"
            : line;
    }

    private sealed class OperationKeyComparer : IEqualityComparer<(string Name, int Arity)>
    {
        public static readonly OperationKeyComparer Instance = new();

        public bool Equals((string Name, int Arity) x, (string Name, int Arity) y) =>
            x.Arity == y.Arity && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Name, int Arity) obj) =>
            HashCode.Combine(obj.Name.ToLowerInvariant(), obj.Arity);
    }
}
