namespace Ananke.Organics.Division.Review;

/// <summary>
/// Composite <see cref="IWorkReviewGate"/> that evaluates multiple reviewers against a quorum.
/// </summary>
public sealed class QuorumWorkReviewGate(
    IReadOnlyList<IWorkReviewGate> gates,
    WorkReviewQuorum quorum) : IWorkReviewGate
{
    /// <inheritdoc />
    public async Task<WorkReviewDecision> ReviewAsync(WorkItem item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(gates);
        ArgumentNullException.ThrowIfNull(quorum);

        if (gates.Count == 0)
            throw new ArgumentException("At least one review gate is required.", nameof(gates));

        var decisions = new Dictionary<string, WorkReviewDecision>(StringComparer.OrdinalIgnoreCase);

        foreach (var gate in gates)
        {
            var decision = await gate.ReviewAsync(item, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(decision.ReviewerId))
            {
                return WorkReviewDecision.Reject(
                    comment: "Quorum review requires reviewer IDs from inner gates.",
                    reviewerId: "quorum");
            }

            if (!decisions.TryAdd(decision.ReviewerId, decision))
            {
                return WorkReviewDecision.Reject(
                    comment: $"Duplicate reviewer ID '{decision.ReviewerId}' was returned during quorum evaluation.",
                    reviewerId: "quorum");
            }
        }

        var relevantReviewerIds = quorum.AllOf
            .Concat(quorum.AnyOf)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var relevantDecisions = relevantReviewerIds
            .Where(decisions.ContainsKey)
            .Select(id => decisions[id])
            .ToArray();

        var requiredRejection = quorum.AllOf
            .Where(id => decisions.TryGetValue(id, out var decision) && decision.Outcome == WorkReviewOutcome.Rejected)
            .Select(id => decisions[id])
            .FirstOrDefault();
        if (requiredRejection is not null)
        {
            return WorkReviewDecision.Reject(
                comment: requiredRejection.Comment,
                reviewerId: requiredRejection.ReviewerId);
        }

        var missingRequired = quorum.AllOf
            .Where(id => !decisions.TryGetValue(id, out var decision) || decision.Outcome == WorkReviewOutcome.Rejected)
            .ToArray();
        if (missingRequired.Length > 0)
        {
            return WorkReviewDecision.Reject(
                comment: $"Missing required approvals from: {string.Join(", ", missingRequired)}",
                reviewerId: "quorum");
        }

        if (quorum.AnyOf.Count > 0)
        {
            var anySatisfied = quorum.AnyOf.Any(id =>
                decisions.TryGetValue(id, out var decision) && decision.Outcome != WorkReviewOutcome.Rejected);

            if (!anySatisfied)
            {
                var anyComments = quorum.AnyOf
                    .Where(decisions.ContainsKey)
                    .Select(id => decisions[id].Comment)
                    .Distinct()
                    .ToArray();

                return WorkReviewDecision.Reject(
                    comment: anyComments.Length == 0
                        ? $"At least one of the following reviewers must approve or revise: {string.Join(", ", quorum.AnyOf)}"
                        : string.Join(" | ", anyComments),
                    reviewerId: "quorum");
            }
        }

        var combinedComment = string.Join(" | ", relevantDecisions.Select(decision => decision.Comment).Distinct());
        var outcome = relevantDecisions.Any(decision => decision.Outcome == WorkReviewOutcome.Revised)
            ? WorkReviewOutcome.Revised
            : WorkReviewOutcome.Approved;

        return new WorkReviewDecision
        {
            Outcome = outcome,
            Comment = string.IsNullOrWhiteSpace(combinedComment) ? "Quorum satisfied" : combinedComment,
            ReviewerId = "quorum"
        };
    }
}
