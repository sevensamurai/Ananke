using Ananke.Orchestration.Budget;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// ADR-arch-028 D14. The period is part of the storage key, so these boundaries decide when a
/// budget resets — there is no scheduled job to get wrong, but there is arithmetic to get wrong.
/// </summary>
[TestFixture]
public class BudgetPeriodTests
{
    private static DateTimeOffset Utc(int y, int m, int d, int h = 12) =>
        new(y, m, d, h, 0, 0, TimeSpan.Zero);

    // -- Calendar months (the default) --------------------------------

    [Test]
    public void CalendarMonth_StartsOnTheFirst()
    {
        BudgetPeriod.StartOfPeriod(Utc(2026, 8, 20)).ShouldBe(Utc(2026, 8, 1, 0));
    }

    [Test]
    public void CalendarMonth_OnTheFirstItself_IsAlreadyTheNewPeriod()
    {
        BudgetPeriod.StartOfPeriod(Utc(2026, 8, 1, 0)).ShouldBe(Utc(2026, 8, 1, 0));
    }

    [Test]
    public void CalendarMonth_LastInstantOfAMonth_StillBelongsToThatMonth()
    {
        var lastInstant = new DateTimeOffset(2026, 8, 31, 23, 59, 59, TimeSpan.Zero);
        BudgetPeriod.StartOfPeriod(lastInstant).ShouldBe(Utc(2026, 8, 1, 0));
    }

    [Test]
    public void CalendarMonth_RollsOverAtTheYearBoundary()
    {
        BudgetPeriod.StartOfPeriod(Utc(2027, 1, 1, 0)).ShouldBe(Utc(2027, 1, 1, 0));
        BudgetPeriod.StartOfPeriod(new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.Zero))
            .ShouldBe(Utc(2026, 12, 1, 0));
    }

    // -- Anchor days --------------------------------------------------

    [Test]
    public void AnchorDay_AfterTheAnchor_UsesThisMonth()
    {
        BudgetPeriod.StartOfPeriod(Utc(2026, 8, 20), anchorDay: 15).ShouldBe(Utc(2026, 8, 15, 0));
    }

    [Test]
    public void AnchorDay_BeforeTheAnchor_UsesLastMonth()
    {
        BudgetPeriod.StartOfPeriod(Utc(2026, 8, 10), anchorDay: 15).ShouldBe(Utc(2026, 7, 15, 0));
    }

    /// <summary>
    /// The case that motivated the ruling: February has no 31st. The period start rolls forward
    /// to the next 1st rather than clamping back to the 28th, so a reader can predict the
    /// boundary without knowing month lengths.
    /// </summary>
    [Test]
    public void AnchorDay31_InFebruary_RollsForwardToTheNextFirst()
    {
        // Early March: the period that started on 1 March (rolled from "31 February").
        BudgetPeriod.StartOfPeriod(Utc(2026, 3, 5), anchorDay: 31).ShouldBe(Utc(2026, 3, 1, 0));
    }

    [Test]
    public void AnchorDay31_MidFebruary_StillInTheJanuaryPeriod()
    {
        // 31 Jan started a period; February contributes no start, so it runs on until 1 March.
        BudgetPeriod.StartOfPeriod(Utc(2026, 2, 14), anchorDay: 31).ShouldBe(Utc(2026, 1, 31, 0));
    }

    [Test]
    public void AnchorDay31_InAMonthThatHasOne_UsesIt()
    {
        BudgetPeriod.StartOfPeriod(Utc(2026, 8, 31, 6), anchorDay: 31).ShouldBe(Utc(2026, 8, 31, 0));
    }

    [Test]
    public void AnchorDay30_InFebruaryOfALeapYear_StillRollsForward()
    {
        // 2028 is a leap year: February has 29 days, so a 30th still does not exist.
        BudgetPeriod.StartOfPeriod(Utc(2028, 2, 20), anchorDay: 30).ShouldBe(Utc(2028, 1, 30, 0));
    }

    [Test]
    public void AnchorDay29_InALeapFebruary_Exists()
    {
        BudgetPeriod.StartOfPeriod(Utc(2028, 2, 29, 6), anchorDay: 29).ShouldBe(Utc(2028, 2, 29, 0));
    }

    // -- Time zone ----------------------------------------------------

    [Test]
    public void Boundaries_AreUtc_NotTheHostsLocalTime()
    {
        // 1 Aug 00:30 in UTC+13 is still 31 July in UTC, so it belongs to the July period.
        var justAfterLocalMidnight = new DateTimeOffset(2026, 8, 1, 0, 30, 0, TimeSpan.FromHours(13));

        BudgetPeriod.StartOfPeriod(justAfterLocalMidnight).ShouldBe(Utc(2026, 7, 1, 0),
            "a budget window must not shift under the host's timezone");
    }

    // -- Keys ---------------------------------------------------------

    [Test]
    public void KeyFor_CombinesIdAndPeriodStart()
    {
        BudgetPeriod.KeyFor(new BudgetId("acme-api"), Utc(2026, 8, 20))
            .ShouldBe("acme-api_2026-08-01");
    }

    [Test]
    public void KeyFor_ChangesOnRollover_WhichIsWhatResetsTheBudget()
    {
        var id = new BudgetId("acme-api");

        BudgetPeriod.KeyFor(id, Utc(2026, 8, 31, 23))
            .ShouldNotBe(BudgetPeriod.KeyFor(id, Utc(2026, 9, 1, 0)));
    }

    [Test]
    public void KeyFor_DistinctIds_DoNotCollide()
    {
        BudgetPeriod.KeyFor(new BudgetId("a"), Utc(2026, 8, 20))
            .ShouldNotBe(BudgetPeriod.KeyFor(new BudgetId("b"), Utc(2026, 8, 20)));
    }

    [Test]
    public void AnchorDay_OutOfRange_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => BudgetPeriod.StartOfPeriod(Utc(2026, 8, 1), 0));
        Should.Throw<ArgumentOutOfRangeException>(() => BudgetPeriod.StartOfPeriod(Utc(2026, 8, 1), 32));
    }
}

[TestFixture]
public class BudgetIdTests
{
    // A BudgetId is a storage key, meant to be handed to Redis/RedLock directly, so natural
    // key shapes are allowed. Making a value safe for a *file* is FileUsageRecorder's job.
    [TestCase("acme-api")]
    [TestCase("team_1.prod")]
    [TestCase("acme/prod:api")]
    [TestCase("tenant 42")]
    [TestCase("A")]
    public void Accepts_AnyNonEmptyKeyShape(string value) =>
        new BudgetId(value).Value.ShouldBe(value);

    [TestCase("")]
    [TestCase("   ")]
    public void Rejects_Empty(string value) =>
        Should.Throw<ArgumentException>(() => new BudgetId(value));

    [Test]
    public void Rejects_OverlyLong() =>
        Should.Throw<ArgumentException>(() => new BudgetId(new string('a', BudgetId.MaxLength + 1)));

    [Test]
    public void Default_HasNoValue() =>
        Should.Throw<InvalidOperationException>(() => _ = default(BudgetId).Value);
}
