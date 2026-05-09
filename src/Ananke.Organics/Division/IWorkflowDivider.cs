using Ananke.Design;
using Ananke.Learning;

using Ananke.Learning.EmpiricalMemory;

namespace Ananke.Organics.Division;

/// <summary>
/// Executes a cell division: derives child manifests, seeds children via
/// <c>ISkillPackager</c>, spawns child cells, and kills the parent.
/// The parent workflow MUST be stopped after this returns.
/// </summary>
/// <remarks>
/// <para>
/// This is the mitosis engine. The parent <b>dies</b> — it does not become a
/// coordinator or triage layer. The new workflows are independent peers.
/// </para>
/// </remarks>
public interface IWorkflowDivider
{
    /// <summary>
    /// Execute the division described by <paramref name="plan"/>.
    /// </summary>
    /// <param name="plan">The division plan (which children to create, tools each gets).</param>
    /// <param name="parentManifest">The parent cell's workflow manifest.</param>
    /// <param name="parentMemory">The parent cell's empirical memory (shared substrate).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<DivisionResult> DivideAsync(
        DivisionPlan plan,
        WorkflowManifest parentManifest,
        IEmpiricalMemory parentMemory,
        CancellationToken ct = default);
}
