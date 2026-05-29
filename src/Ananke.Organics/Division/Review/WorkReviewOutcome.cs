namespace Ananke.Organics.Division.Review;

/// <summary>
/// Outcome of a work review.
/// </summary>
public enum WorkReviewOutcome
{
    /// <summary>The work is approved as submitted.</summary>
    Approved,

    /// <summary>The work is rejected and should not proceed.</summary>
    Rejected,

    /// <summary>The work can proceed only after revision.</summary>
    Revised,

    /// <summary>
    /// The review has been parked — a decision has not yet been issued.
    /// Returned by <see cref="ParkingCallbackWorkReviewGate"/> while the gate waits for a
    /// human or external system to call <c>ResumeAsync</c>.
    /// </summary>
    Pending
}
