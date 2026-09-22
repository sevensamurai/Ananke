using Ananke.Orchestration.Conformance.NUnit;

namespace Ananke.Orchestration.Tests.Conformance;

/// <summary>
/// A shipped adapter with no conformance fixture fails the build.
/// </summary>
/// <remarks>
/// The situation the ADR documents is that Ananke had a provider contract and a suite testing it and
/// nothing shipped was subject to the suite, because nobody got around to it. Fixtures fix that once;
/// this keeps it fixed.
/// </remarks>
[TestFixture]
public sealed class ConformanceCoverageTests
{
    [Test]
    public void EveryShippedAdapter_IsSubjectToTheConformanceSuite() =>
        ConformanceCoverage.AssertEveryAdapterHasAFixture(typeof(ConformanceCoverageTests).Assembly);
}
