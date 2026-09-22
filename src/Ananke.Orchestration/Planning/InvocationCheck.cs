namespace Ananke.Orchestration.Planning;

/// <summary>
/// One check a plan may invoke: what it is called, how many arguments it takes, and what it means.
/// </summary>
/// <remarks>
/// <b>The arity is part of the identity.</b> <c>covers(day, stop)</c> and <c>covers(day)</c> are not
/// the same question, and a check that accepted either would decide one of them wrongly rather than
/// abstaining.
/// </remarks>
public sealed record CheckDefinition
{
    /// <summary>What a criterion names to invoke it.</summary>
    public required string Name { get; init; }

    /// <summary>How many arguments it takes.</summary>
    public required int Arity { get; init; }

    /// <summary>What it decides, in a sentence — for a reader, and for a model authoring criteria.</summary>
    public required string Description { get; init; }

    /// <summary>Decides one invocation.</summary>
    public required Func<Criterion, CancellationToken, Task<Finding>> Decide { get; init; }

    /// <summary>
    /// Why these arguments are not ones this check can be asked about, or <see langword="null"/>
    /// when they are.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Resolving is not the same as being answerable.</b> A supervisor authoring criteria wrote
    /// <c>covers(day-3b, return)</c> — a real check, the right arity, and a stop that does not
    /// exist. It resolves, it runs, and it answers <em>no</em> for ever. The step then disputes a
    /// contract that was impossible the moment it was written, and the run spends its budget on it.
    /// </para>
    /// <para>
    /// <b>Only for arguments that are wrong in themselves</b>, never for ones that are merely not
    /// satisfied yet. A day that has not been planned is not an invalid argument; a stop the world
    /// has never heard of is.
    /// </para>
    /// </remarks>
    public Func<Criterion, string?>? Validate { get; init; }

    /// <summary>
    /// Creates a definition over a synchronous predicate, with nothing further to say when it fails.
    /// </summary>
    public static CheckDefinition Of(
        string name,
        int arity,
        string description,
        Func<Criterion, bool> decide,
        Func<Criterion, string?>? validate = null)
    {
        ArgumentNullException.ThrowIfNull(decide);

        return Of(name, arity, description, c => decide(c) ? Finding.Held : Finding.Not(), validate);
    }

    /// <summary>
    /// Creates a definition over a synchronous check that can explain a failure — the version with
    /// something to say.
    /// </summary>
    public static CheckDefinition Of(
        string name,
        int arity,
        string description,
        Func<Criterion, Finding> decide,
        Func<Criterion, string?>? validate = null)
    {
        ArgumentNullException.ThrowIfNull(decide);

        return new CheckDefinition
        {
            Name = name,
            Arity = arity,
            Description = description,
            Decide = (criterion, _) => Task.FromResult(decide(criterion)),
            Validate = validate
        };
    }
}

/// <summary>
/// A check that decides criteria by <b>resolving them</b> — name and arity — rather than by
/// recognising their text.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is what lets a plan change and stay checked.</b> A text-matched check has to be handed
/// every criterion the plan can ever contain, so the first re-ruling that renames a step, or says
/// the same thing in different words, produces a plan nothing can rule on — and the run reports
/// abstention rather than failure, which reads like caution and is actually blindness.
/// </para>
/// <para>
/// <b>It refuses what it does not have.</b> An unknown name, or a known name with the wrong number
/// of arguments, abstains exactly as before. Resolution removes the accidental gap, not the
/// deliberate one.
/// </para>
/// </remarks>
public sealed class InvocationCheck : IDeterministicCheck
{
    private readonly Dictionary<(string Name, int Arity), CheckDefinition> _definitions;

    /// <summary>Creates a check over <paramref name="definitions"/>.</summary>
    /// <param name="oracle">What runs, named so that re-running it later is a concrete instruction.</param>
    /// <param name="definitions">The checks a plan may invoke.</param>
    public InvocationCheck(string oracle, IEnumerable<CheckDefinition> definitions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oracle);
        ArgumentNullException.ThrowIfNull(definitions);

        Oracle = oracle;
        _definitions = definitions.ToDictionary(
            d => (d.Name, d.Arity),
            StringTupleComparer.Instance);
    }

    /// <inheritdoc />
    public string Oracle { get; }

    /// <summary>The checks a plan may invoke, for admission and for telling an author what exists.</summary>
    public IReadOnlyCollection<CheckDefinition> Definitions => _definitions.Values;

    /// <inheritdoc />
    public bool CanRule(string criterion) => Resolve(criterion) is not null;

    /// <summary>
    /// Why <paramref name="criterion"/> could never hold, or <see langword="null"/> if it could.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="CanRule"/> because they are different questions. <em>Can you decide
    /// this?</em> is about the check; <em>is this answerable at all?</em> is about the arguments, and
    /// a criterion that resolves and is nonsense is worse than one that does not resolve — it is
    /// ruled on, and it always fails.
    /// </remarks>
    public string? Nonsense(string criterion)
    {
        if (Resolve(criterion) is not var (parsed, definition) || definition?.Validate is null)
            return null;

        return definition.Validate(parsed!);
    }

    /// <inheritdoc />
    public Task<Finding> RunAsync(string criterion, CancellationToken ct = default)
    {
        if (Resolve(criterion) is not var (parsed, definition) || definition is null)
            throw new InvalidOperationException(
                $"'{Oracle}' was asked to decide '{criterion}', which it cannot: no check of that "
                + "name and arity is registered. Ask CanRule first — a check that answered anyway "
                + "would make abstention impossible.");

        return definition.Decide(parsed!, ct);
    }

    /// <summary>A legend of what may be invoked, for whoever is authoring criteria.</summary>
    /// <remarks>
    /// <b>A model authoring criteria has to be told what exists.</b> Left to guess it writes true,
    /// decidable-sounding statements that nothing is prepared to decide — which is the failure this
    /// type removes for renames and cannot remove for invention.
    /// </remarks>
    public string Legend() =>
        string.Join(
            "\n",
            _definitions.Values
                .OrderBy(d => d.Name, StringComparer.Ordinal)
                .Select(d => $"  {d.Name}({string.Join(", ", Enumerable.Range(1, d.Arity)
                    .Select(i => $"arg{i}"))}) — {d.Description}"));

    private (Criterion? Parsed, CheckDefinition? Definition)? Resolve(string criterion)
    {
        if (Criterion.Parse(criterion) is not { } parsed)
            return null;

        return _definitions.TryGetValue((parsed.Name, parsed.Arguments.Count), out var definition)
            ? (parsed, definition)
            : null;
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string Name, int Arity)>
    {
        public static readonly StringTupleComparer Instance = new();

        public bool Equals((string Name, int Arity) x, (string Name, int Arity) y) =>
            x.Arity == y.Arity && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Name, int Arity) obj) =>
            HashCode.Combine(obj.Name.ToLowerInvariant(), obj.Arity);
    }
}
