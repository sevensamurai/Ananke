namespace Ananke.Orchestration.Budget;

/// <summary>
/// Identifies what is being budgeted — an application, or a group of applications sharing one
/// ceiling. Caller-supplied, so a host can run several independent budgets without them
/// interfering.
/// </summary>
/// <remarks>
/// This is a <em>storage key</em>, meant to be used directly by a Redis, RedLock or similar
/// backend, so natural key shapes like <c>acme/prod:api</c> are allowed. Making a value safe for
/// a particular medium belongs to the store that has the constraint — the file-backed recorder
/// derives a filename from a hash of this value rather than using it verbatim.
/// </remarks>
public readonly record struct BudgetId
{
    /// <summary>Longest accepted key. Well under any realistic backend limit.</summary>
    public const int MaxLength = 512;

    private readonly string? _value;

    /// <summary>Creates a budget key.</summary>
    /// <exception cref="ArgumentException">
    /// The value is empty, whitespace, or longer than <see cref="MaxLength"/>.
    /// </exception>
    public BudgetId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length > MaxLength)
            throw new ArgumentException(
                $"Budget id is {value.Length} characters; the maximum is {MaxLength}.", nameof(value));

        _value = value;
    }

    /// <summary>The key.</summary>
    public string Value => _value ?? throw new InvalidOperationException(
        "This BudgetId was default-constructed and carries no value.");

    /// <inheritdoc />
    public override string ToString() => Value;
}
