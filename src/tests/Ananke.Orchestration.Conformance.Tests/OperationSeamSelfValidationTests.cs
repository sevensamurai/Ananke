using Ananke.Orchestration.Planning.Conformance;
using Ananke.Orchestration.Planning.Conformance.NUnit;

namespace Ananke.Orchestration.Conformance.Tests;

/// <summary>
/// Runs the operation-seam suite against its reference seam.
/// </summary>
/// <remarks>
/// <b>This is the seam's only consumer inside this repository.</b> The coding agent that used to be
/// its non-test user now lives in a product repository, which is private and is nobody's test
/// fixture — so what keeps the seam honest here is this suite, and a downstream consumer runs the
/// same list to find out whether an upgrade moved it.
/// </remarks>
[TestFixture]
public sealed class ReferenceOperationSeamTests : OperationSeamConformanceTests
{
    protected override PlanOperationSeam CreateSeam() => OperationSeamConformance.ReferenceSeam();
}
