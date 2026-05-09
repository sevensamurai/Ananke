using Ananke.Organics.Division;
using Ananke.Organics.Division.Approval;

namespace Ananke.Federation.Division;

/// <summary>
/// <see cref="IDivisionApprovalGate"/> that always requires human approval
/// when a <see cref="DivisionPlan"/> targets remote platforms. Local-only
/// divisions are delegated to an inner gate (which may auto-approve).
/// </summary>
/// <remarks>
/// <para>
/// This is a v1 safety constraint: deploying to platforms has cost, latency,
/// and security implications that require human oversight. Future versions
/// may add policy-based auto-approval for trusted platforms.
/// </para>
/// </remarks>
public sealed class PlatformDivisionApprovalGate : IDivisionApprovalGate
{
    private readonly IDivisionApprovalGate _localGate;
    private readonly Func<DivisionPlan, ComplexitySnapshot, CancellationToken, Task<DivisionApproval>>? _humanCallback;

    /// <summary>
    /// Creates a platform-aware approval gate.
    /// </summary>
    /// <param name="localGate">
    /// Gate used for local-only divisions (e.g. <see cref="AutoApprovalGate"/>).
    /// </param>
    /// <param name="humanCallback">
    /// Callback invoked for platform-targeted divisions. When <see langword="null"/>,
    /// platform divisions are always rejected with a message requesting manual approval.
    /// </param>
    public PlatformDivisionApprovalGate(
        IDivisionApprovalGate localGate,
        Func<DivisionPlan, ComplexitySnapshot, CancellationToken, Task<DivisionApproval>>? humanCallback = null)
    {
        ArgumentNullException.ThrowIfNull(localGate);
        _localGate = localGate;
        _humanCallback = humanCallback;
    }

    /// <inheritdoc />
    public async Task<DivisionApproval> ReviewAsync(
        DivisionPlan plan,
        ComplexitySnapshot snapshot,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(snapshot);

        var hasRemoteTargets = plan.Children.Any(c => c.TargetPlatform is not null);

        if (!hasRemoteTargets)
            return await _localGate.ReviewAsync(plan, snapshot, ct);

        // Platform-targeted division requires human approval
        if (_humanCallback is not null)
            return await _humanCallback(plan, snapshot, ct);

        // No callback — reject with guidance
        var platforms = plan.Children
            .Where(c => c.TargetPlatform is not null)
            .Select(c => c.TargetPlatform!)
            .Distinct()
            .ToList();

        return DivisionApproval.Reject(
            $"Platform division requires human approval. Targets: {string.Join(", ", platforms)}. " +
            "Configure a humanCallback or use nnke-platform deploy manually.",
            reviewedBy: "PlatformDivisionApprovalGate");
    }
}
