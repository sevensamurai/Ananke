namespace Ananke.Organics.Division.Review;

/// <summary>
/// A review decision for a <see cref="WorkItem"/>.
/// </summary>
public sealed record WorkReviewDecision
{
    /// <summary>The final review outcome.</summary>
    public required WorkReviewOutcome Outcome { get; init; }

    /// <summary>Reviewer comment explaining the decision.</summary>
    public required string Comment { get; init; }

    /// <summary>Identifier of the reviewer that issued the decision.</summary>
    public required string ReviewerId { get; init; }

    /// <summary>Creates an approved review decision.</summary>
    public static WorkReviewDecision Approve(string comment, string reviewerId) => new()
    {
        Outcome = WorkReviewOutcome.Approved,
        Comment = comment,
        ReviewerId = reviewerId
    };

    /// <summary>Creates a rejected review decision.</summary>
    public static WorkReviewDecision Reject(string comment, string reviewerId) => new()
    {
        Outcome = WorkReviewOutcome.Rejected,
        Comment = comment,
        ReviewerId = reviewerId
    };

    /// <summary>Creates a revised review decision.</summary>
    public static WorkReviewDecision Revise(string comment, string reviewerId) => new()
    {
        Outcome = WorkReviewOutcome.Revised,
        Comment = comment,
        ReviewerId = reviewerId
    };
}
