namespace Ananke.Federation.Recommendation;

/// <summary>
/// The direction of a fit reason — positive signal, negative signal, or a hard blocker.
/// </summary>
public enum FitReasonKind
{
    /// <summary>A positive signal that increases the platform's fit score.</summary>
    Plus,

    /// <summary>A negative signal that decreases the platform's fit score.</summary>
    Minus,

    /// <summary>A hard blocker that zeros the platform's total score regardless of other axes.</summary>
    Block
}
