namespace Ananke.Orchestration.Planning;

/// <summary>
/// A check that decides named criteria by evaluating a predicate in-process — a file that must
/// exist, a count that must be zero, a byte comparison.
/// </summary>
/// <remarks>
/// <para>
/// <b>The criteria it can rule are declared, never inferred.</b> A check that answered for every
/// criterion it was asked about would make abstention impossible, and abstention is the part of
/// verification that keeps the unverified visible. Anything outside the declared set is refused
/// rather than answered.
/// </para>
/// <para>
/// The predicate must <em>decide</em>, not weigh. If answering needs reading and judgement, it is
/// not one of these — a verdict carries the authority of something that ran, and a guess wearing
/// that authority is worse than no check at all.
/// </para>
/// </remarks>
public sealed class PredicateCheck : IDeterministicCheck
{
    private readonly HashSet<string> _criteria;
    private readonly Func<string, CancellationToken, Task<bool>> _predicate;

    /// <summary>Creates a check over an asynchronous predicate.</summary>
    /// <param name="oracle">
    /// What runs, named so that re-running it later is a concrete instruction.
    /// </param>
    /// <param name="criteria">The criteria this check is prepared to decide.</param>
    /// <param name="predicate">Decides one criterion.</param>
    /// <remarks>
    /// A predicate answers <see langword="true"/> or <see langword="false"/> and nothing else, so a
    /// failing verdict here carries no <see cref="Finding.Detail"/> — there is no "why" a predicate
    /// can offer that is not already in its criterion's own text. A check that has a reason to give
    /// says so through <see cref="Finding"/> directly, which is what <see cref="ProcessCheck"/> and
    /// <see cref="InvocationCheck"/> do.
    /// </remarks>
    public PredicateCheck(
        string oracle,
        IEnumerable<string> criteria,
        Func<string, CancellationToken, Task<bool>> predicate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oracle);
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(predicate);

        Oracle = oracle;
        _criteria = new HashSet<string>(criteria, StringComparer.Ordinal);
        _predicate = predicate;
    }

    /// <summary>Creates a check over a synchronous predicate.</summary>
    /// <param name="oracle">
    /// What runs, named so that re-running it later is a concrete instruction.
    /// </param>
    /// <param name="criteria">The criteria this check is prepared to decide.</param>
    /// <param name="predicate">Decides one criterion.</param>
    public PredicateCheck(string oracle, IEnumerable<string> criteria, Func<string, bool> predicate)
        : this(
            oracle,
            criteria,
            (criterion, _) => Task.FromResult(
                (predicate ?? throw new ArgumentNullException(nameof(predicate)))(criterion)))
    {
    }

    /// <inheritdoc />
    public string Oracle { get; }

    /// <inheritdoc />
    public bool CanRule(string criterion) => _criteria.Contains(criterion);

    /// <inheritdoc />
    public async Task<Finding> RunAsync(string criterion, CancellationToken ct = default)
    {
        if (!CanRule(criterion))
        {
            throw new InvalidOperationException(
                $"'{Oracle}' was asked to rule on a criterion it does not cover: '{criterion}'.");
        }

        return await _predicate(criterion, ct).ConfigureAwait(false) ? Finding.Held : Finding.Not();
    }
}
