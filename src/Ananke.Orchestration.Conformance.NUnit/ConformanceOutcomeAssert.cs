using System.Runtime.ExceptionServices;
using Ananke.Orchestration.Conformance;

namespace Ananke.Orchestration.Conformance.NUnit;

/// <summary>
/// Maps a <see cref="ConformanceOutcome"/> onto NUnit's pass/skip/fail vocabulary.
/// </summary>
/// <remarks>
/// Public because a consumer writing their own fixture over
/// <see cref="ConformanceScenario{TSubject}"/> — rather than inheriting one of the fixtures here —
/// still wants the same mapping.
/// </remarks>
public static class ConformanceOutcomeAssert
{
    /// <summary>
    /// Reports <paramref name="outcome"/> to NUnit: passing returns, skipping calls
    /// <c>Assert.Pass</c>, and failing throws.
    /// </summary>
    /// <param name="outcome">The result of <see cref="ConformanceScenario{TSubject}.RunAsync"/>.</param>
    public static void Apply(ConformanceOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        switch (outcome.Status)
        {
            case ConformanceStatus.Passed:
                return;

            case ConformanceStatus.Skipped:
                // The scenario does not apply to this subject, so the runner should report it as
                // skipped — not as passed.
                //
                // The conformance contract says "the NUnit shim maps it back to Assert.Pass; other
                // runners map it to their own skip". This deviates from the first clause to honour
                // the second: NUnit's skip *is* Assert.Ignore, and Assert.Pass was carried over from
                // the fixtures this suite grew out of rather than chosen. Reporting a skip as a pass
                // means a subclass that skipped every scenario reads as fully green, which is the
                // exact failure this package exists to prevent one level up. See the tracker's §4 Q5.
                Assert.Ignore(outcome.Reason!);
                return;

            case ConformanceStatus.Failed:
                // Rethrow the original assertion so Shouldly's message and stack survive; a
                // summarised failure would lose both.
                if (outcome.Exception is not null)
                    ExceptionDispatchInfo.Capture(outcome.Exception).Throw();

                // A scenario that returned Fail without attaching an assertion. Throwing NUnit's
                // own failure type rather than calling Assert.Fail is deliberate: Assert.Fail also
                // records into the ambient result, which makes the mapping untestable and would
                // taint an outer test that legitimately expects this throw.
                throw new AssertionException(outcome.Reason!);

            default:
                throw new AssertionException($"Unknown conformance status '{outcome.Status}'");
        }
    }
}
