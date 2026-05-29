namespace Ananke.Organics.Division.Review;

/// <summary>
/// Describes the kind of work product being reviewed.
/// </summary>
public enum WorkItemKind
{
    /// <summary>A pull request or patch review.</summary>
    PullRequest,

    /// <summary>A design or architecture document review.</summary>
    DesignDoc,

    /// <summary>A wireframe or visual design review.</summary>
    Wireframe,

    /// <summary>Any other work product type.</summary>
    Other
}
