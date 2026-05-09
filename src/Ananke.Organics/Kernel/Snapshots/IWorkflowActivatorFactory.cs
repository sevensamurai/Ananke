using Ananke.Learning.EmpiricalMemory;
using Ananke.Organics.Division;

namespace Ananke.Organics.Kernel.Snapshots;

/// <summary>
/// Factory that creates runnable workflow loops from <see cref="WorkflowSnapshot"/>s.
/// Bridges the gap between the untyped <see cref="IWorkflowDivider"/> and the
/// generic <see cref="WorkflowActivator{TState}"/>.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="IWorkflowDivider"/> operates on <see cref="WorkflowSnapshot"/>
/// and <see cref="DivisionPlan"/> — it is not generic over workflow state. This
/// factory lets the divider create runnable loops without knowing <c>TState</c>.
/// </para>
/// <para>
/// Implementations may auto-join the created workflow to an <see cref="OrganicHost"/>
/// for recursive division monitoring, and wrap the shared
/// <see cref="Ananke.Learning.EmpiricalMemory.IEmpiricalMemory"/> with a
/// <see cref="DomainAffinityMemory"/> based on the provided
/// <see cref="MemoryProfile"/>.
/// </para>
/// </remarks>
public interface IWorkflowActivatorFactory
{
    /// <summary>
    /// Creates a workflow loop from a snapshot and memory profile. The returned
    /// delegate can be passed directly to <see cref="IWorkflowHost.StartAsync"/>.
    /// </summary>
    /// <param name="snapshot">The cell snapshot describing topology, tools, models, and jobs.</param>
    /// <param name="memoryProfile">
    /// Domain affinity profile for the new cell. When provided, the factory
    /// wraps the shared memory with a <see cref="DomainAffinityMemory"/>
    /// decorator scoped to the profile's domains.
    /// </param>
    /// <returns>
    /// A loop function suitable for <see cref="IWorkflowHost.StartAsync"/>. The loop
    /// runs the workflow repeatedly (or once, depending on implementation) until
    /// the cancellation token is triggered.
    /// </returns>
    Func<CancellationToken, Task> CreateLoop(
        WorkflowSnapshot snapshot,
        MemoryProfile? memoryProfile = null);
}
