namespace Ananke.Organics.Division.Review;

/// <summary>
/// Quorum requirements for combining multiple work review gates.
/// </summary>
public sealed record WorkReviewQuorum
{
    /// <summary>Reviewer identifiers that must all return a non-rejected decision.</summary>
    public required IReadOnlyList<string> AllOf { get; init; }

    /// <summary>Reviewer identifiers where at least one must return a non-rejected decision.</summary>
    public IReadOnlyList<string> AnyOf { get; init; } = [];

    /// <summary>
    /// Creates a quorum that requires all specified reviewers to participate successfully.
    /// </summary>
    public static WorkReviewQuorum RequireAllOf(params string[] reviewerIds) => new()
    {
        AllOf = Normalize(reviewerIds)
    };

    /// <summary>
    /// Extends the quorum so that at least one of the specified reviewers must also succeed.
    /// </summary>
    public WorkReviewQuorum AndAnyOf(params string[] reviewerIds) => this with
    {
        AnyOf = Normalize(reviewerIds)
    };

    private static IReadOnlyList<string> Normalize(IEnumerable<string> reviewerIds)
    {
        ArgumentNullException.ThrowIfNull(reviewerIds);

        var values = reviewerIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (values.Length == 0)
            throw new ArgumentException("At least one reviewer ID is required.", nameof(reviewerIds));

        return values;
    }
}
