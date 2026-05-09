using Ananke.Design;
using Ananke.Orchestration.Tools;

namespace Ananke.Organics.Division;

/// <summary>
/// Cluster strategy that groups tools by their originating <see cref="ToolKit"/>.
/// Each kit becomes one <see cref="ChildSpec"/> with all tools from that kit,
/// a domain derived from the kit name, and the parent's shared jobs.
/// </summary>
/// <remarks>
/// <para>
/// Plug this into <see cref="ThresholdDivisionPolicy"/> via the
/// <c>clusterStrategy</c> parameter:
/// </para>
/// <code>
/// var strategy = new ToolKitClusterStrategy(catalogTools, orderTools);
/// var policy = new ThresholdDivisionPolicy(clusterStrategy: strategy.Split);
/// </code>
/// <para>
/// The strategy produces one child per kit. Each child inherits all jobs from
/// the parent manifest (agent cells typically share the same job topology) and
/// receives only the tools from its kit.
/// </para>
/// </remarks>
public sealed class ToolKitClusterStrategy
{
    private readonly IReadOnlyList<ToolKit> _kits;

    /// <summary>
    /// Creates a strategy that will split tools according to the provided kits.
    /// </summary>
    /// <param name="kits">
    /// The source tool kits. Each kit defines a cluster — its tools will be
    /// assigned to one child cell. At least two kits are required for division.
    /// </param>
    public ToolKitClusterStrategy(params ToolKit[] kits)
    {
        ArgumentNullException.ThrowIfNull(kits);
        _kits = kits;
    }

    /// <summary>
    /// Splits the parent cell into children — one per <see cref="ToolKit"/>.
    /// Suitable for passing as the <c>clusterStrategy</c> delegate to
    /// <see cref="ThresholdDivisionPolicy"/>.
    /// </summary>
    /// <param name="parentName">Name of the parent cell being divided.</param>
    /// <param name="manifest">The parent's workflow manifest (used for shared jobs).</param>
    /// <returns>
    /// A child spec per kit, or an empty list if fewer than two kits are provided.
    /// </returns>
    public IReadOnlyList<ChildSpec> Split(string parentName, WorkflowManifest manifest)
    {
        if (_kits.Count < 2)
            return [];

        var sharedJobs = manifest.Jobs.Keys.ToList();

        var children = new List<ChildSpec>(_kits.Count);

        foreach (var kit in _kits)
        {
            var domain = DeriveDomain(kit.Name);

            children.Add(new ChildSpec
            {
                Name = $"{parentName}-{domain}",
                Domain = domain,
                Tools = kit.Tools.Keys.ToList(),
                Jobs = sharedJobs
            });
        }

        return children;
    }

    /// <summary>
    /// Derives a short domain name from a <see cref="ToolKit.Name"/>.
    /// Strips common suffixes like <c>-tools</c> and <c>-toolkit</c>.
    /// </summary>
    private static string DeriveDomain(string kitName)
    {
        var domain = kitName;

        ReadOnlySpan<string> suffixes = ["-tools", "-toolkit", "_tools", "_toolkit"];
        foreach (var suffix in suffixes)
        {
            if (domain.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                domain = domain[..^suffix.Length];
                break;
            }
        }

        return domain.ToLowerInvariant();
    }
}
