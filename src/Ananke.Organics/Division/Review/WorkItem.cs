namespace Ananke.Organics.Division.Review;

/// <summary>
/// A work product submitted for review.
/// </summary>
public sealed record WorkItem
{
    /// <summary>Stable identifier for the work item.</summary>
    public required string Id { get; init; }

    /// <summary>Short title shown to reviewers.</summary>
    public required string Title { get; init; }

    /// <summary>Category of work under review.</summary>
    public required WorkItemKind Kind { get; init; }

    /// <summary>Primary review payload, such as diff text, markdown, or a summary.</summary>
    public required string Payload { get; init; }
}
