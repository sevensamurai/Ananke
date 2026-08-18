namespace Ananke.Orchestration.Budget;

/// <summary>
/// The window a period budget accumulates over — by default a UTC calendar month.
/// </summary>
/// <remarks>
/// <para>
/// <b>The period is part of the storage key.</b> On rollover the key simply changes and the new
/// period starts at zero, so there is no scheduled task, no background timer, and no rollover
/// race. Clearing a period is therefore only ever manual intervention.
/// </para>
/// <para>
/// <b>Boundaries are UTC.</b> A budget window must not shift under a host's local timezone or
/// move twice a year with daylight saving; the same id must mean the same window on every
/// machine that touches it.
/// </para>
/// </remarks>
public static class BudgetPeriod
{
    /// <summary>The default anchor: periods run from the 1st, i.e. calendar months.</summary>
    public const int CalendarMonthAnchor = 1;

    /// <summary>
    /// The start of the period containing <paramref name="utcNow"/>.
    /// </summary>
    /// <param name="utcNow">The current instant; converted to UTC before any arithmetic.</param>
    /// <param name="anchorDay">
    /// Day of month the period starts on, 1–31. Where a month has no such day — the 31st in
    /// February — that month's period start rolls forward to the <em>next</em> 1st rather than
    /// clamping back to the month's last day, which keeps boundaries predictable.
    /// </param>
    public static DateTimeOffset StartOfPeriod(DateTimeOffset utcNow, int anchorDay = CalendarMonthAnchor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(anchorDay, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(anchorDay, 31);

        var now = utcNow.ToUniversalTime();

        // The period that begins in this month may not have begun yet — step back until one has.
        var candidate = AnchorInMonth(now.Year, now.Month, anchorDay);
        if (candidate <= now)
            return candidate;

        var previous = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(-1);
        return AnchorInMonth(previous.Year, previous.Month, anchorDay);
    }

    /// <summary>
    /// The storage key for <paramref name="id"/>'s current period — the id and the period start,
    /// e.g. <c>acme-api_2026-08-01</c>. Safe to use directly as a file name.
    /// </summary>
    public static string KeyFor(BudgetId id, DateTimeOffset utcNow, int anchorDay = CalendarMonthAnchor) =>
        $"{id.Value}_{StartOfPeriod(utcNow, anchorDay):yyyy-MM-dd}";

    /// <summary>
    /// The anchor day within a given month, rolled to the next month's 1st when that month is
    /// too short to contain it.
    /// </summary>
    private static DateTimeOffset AnchorInMonth(int year, int month, int anchorDay)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);

        return anchorDay <= daysInMonth
            ? new DateTimeOffset(year, month, anchorDay, 0, 0, 0, TimeSpan.Zero)
            : new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
    }
}
