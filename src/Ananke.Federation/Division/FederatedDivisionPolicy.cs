using Ananke.Design;
using Ananke.Federation.Deployment;
using Ananke.Federation.Monitoring;
using Ananke.Organics.Division;

namespace Ananke.Federation.Division;

/// <summary>
/// <see cref="IDivisionPolicy"/> decorator that enriches an inner policy's
/// <see cref="DivisionPlan"/> with platform targeting. Consults deployment
/// profiles and metrics trends to decide where each child should run.
/// </summary>
/// <remarks>
/// <para>
/// When the inner policy proposes a division, this policy:
/// </para>
/// <list type="number">
///   <item>Evaluates each child's tools against available deployment profiles.</item>
///   <item>Checks <see cref="RemoteMetricsTracker"/> trends — children with
///     platform-native tools are placed on that platform.</item>
///   <item>Sets <see cref="ChildSpec.TargetPlatform"/> on each child spec.</item>
/// </list>
/// <para>
/// If no profile matches a child's tools, it stays local (<c>TargetPlatform = null</c>).
/// </para>
/// </remarks>
public sealed class FederatedDivisionPolicy : IDivisionPolicy
{
    private readonly IDivisionPolicy _inner;
    private readonly IReadOnlyDictionary<string, DeploymentProfile> _profiles;
    private readonly RemoteMetricsTracker? _metricsTracker;

    /// <summary>
    /// Creates a federated division policy.
    /// </summary>
    /// <param name="inner">The underlying division policy (threshold or experience-driven).</param>
    /// <param name="profiles">
    /// Available deployment profiles keyed by platform identifier.
    /// Used to match child tools to platforms with native support.
    /// </param>
    /// <param name="metricsTracker">
    /// Optional metrics tracker. When provided, children targeting a platform
    /// with a struggling trend may be kept local instead.
    /// </param>
    public FederatedDivisionPolicy(
        IDivisionPolicy inner,
        IReadOnlyDictionary<string, DeploymentProfile> profiles,
        RemoteMetricsTracker? metricsTracker = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(profiles);

        _inner = inner;
        _profiles = profiles;
        _metricsTracker = metricsTracker;
    }

    /// <inheritdoc />
    public async Task<DivisionPlan?> EvaluateAsync(
        ComplexitySnapshot snapshot,
        WorkflowManifest manifest,
        CancellationToken ct = default)
    {
        var plan = await _inner.EvaluateAsync(snapshot, manifest, ct);
        if (plan is null)
            return null;

        var enrichedChildren = plan.Children
            .Select(child => child with { TargetPlatform = ResolvePlatform(child) })
            .ToList();

        return plan with { Children = enrichedChildren };
    }

    private string? ResolvePlatform(ChildSpec child)
    {
        // Find the profile that provides the best native coverage for this child's tools
        string? bestPlatform = null;
        var bestCoverage = 0;

        foreach (var (platform, profile) in _profiles)
        {
            var coverage = child.Tools.Count(tool =>
                profile.Tools.TryGetValue(tool, out var binding) &&
                string.Equals(binding.Execute, "platform", StringComparison.OrdinalIgnoreCase));

            if (coverage > bestCoverage)
            {
                bestCoverage = coverage;
                bestPlatform = platform;
            }
        }

        // If the target platform is currently struggling, keep local
        if (bestPlatform is not null && _metricsTracker is not null)
        {
            var trackable = _metricsTracker.GetTrackableDeployments();
            var platformDeployment = trackable.FirstOrDefault(id =>
                id.Contains(bestPlatform, StringComparison.OrdinalIgnoreCase));

            if (platformDeployment is not null)
            {
                var trend = _metricsTracker.GetTrend(platformDeployment);
                if (trend?.IsStrugglingGeneralist == true)
                    return null; // Don't send more work to a struggling platform
            }
        }

        return bestPlatform;
    }
}
