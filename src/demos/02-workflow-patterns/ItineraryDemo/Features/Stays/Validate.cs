using Ananke.Orchestration.Planning;
using ItineraryDemo.Model;

namespace ItineraryDemo.Features.Stays;

/// <summary>
/// The program that rules on a plan: can a place's stay be had, and does the week add up?
/// </summary>
/// <remarks>
/// <para>
/// <b>Criteria are invocations.</b> <c>stay(hakone, 2027-04-01, 2027-04-08, 2)</c> is decidable
/// whoever wrote it, because every fact it needs is an argument — so a Planner rewriting the week
/// writes a new invocation and the same program keeps ruling on it.
/// </para>
/// <para>
/// <b>It reads the service and the plan, never a step's word.</b> <c>stay</c> asks the service whether
/// such a stay is free; <c>week_planned</c> reads the stays the plan's steps ask for.
/// </para>
/// </remarks>
internal static class Validate
{
    public static InvocationCheck Checks(TripService service, Func<CancellationToken, Task<PlanTree?>> plan) => new(
        "itinerary",
        [
            CheckDefinition.Of(
                "stay", 4,
                "a stay in arg1 is free for arg4 nights in a row, checking in on or after arg2 "
                    + "and checking out by arg3",
                c => Stay(service, c),
                c => Arguments(service, c, withPlace: true)),

            CheckDefinition.Of(
                "stay_on", 3,
                "a stay in arg1 is free every night from check-in arg2 to check-out arg3",
                c => StayOn(service, c),
                c => SetDates(service, c)),

            new CheckDefinition
            {
                Name = "week_planned",
                Arity = 3,
                Description = "the plan's stay_on steps give arg3 nights in a row between arg1 and arg2, none of "
                    + "them twice",
                Decide = (c, ct) => WeekPlannedAsync(plan, c, ct),
                Validate = c => Arguments(service, c, withPlace: false)
            },

            new CheckDefinition
            {
                Name = "follows_route",
                Arity = 0,
                Description = "the plan's stay_on steps, taken by check-in date, go out along the route and may "
                    + "come back along it once, never going out again",
                Decide = (_, ct) => FollowsRouteAsync(service, plan, ct)
            }
        ]);

    private static async Task<Finding> FollowsRouteAsync(
        TripService service, Func<CancellationToken, Task<PlanTree?>> plan, CancellationToken ct)
    {
        var tree = await plan(ct).ConfigureAwait(false);
        var stays = tree is null ? [] : PlannedStay.In(tree).Where(s => s.OnSetDates).OrderBy(s => s.CheckIn).ToList();
        var route = string.Join(" → ", service.Route);

        PlannedStay? previous = null;

        // Out along the route, then back along it: once a stay is earlier on the route than the one
        // before it, the trip is coming back and no later stay may be further out again.
        var comingBack = false;

        foreach (var stay in stays)
        {
            var at = service.RouteIndex(stay.Place);

            if (at < 0)
                return Finding.Not($"{stay.Step} is in {stay.Place}, which is not on the route ({route}).");

            if (previous is not null)
            {
                var before = service.RouteIndex(previous.Place);

                if (at < before)
                {
                    comingBack = true;
                }
                else if (at > before && comingBack)
                {
                    return Finding.Not(
                        $"{stay.Step} goes out again to {stay.Place} after {previous.Step} came back to "
                        + $"{previous.Place}; a trip goes out along the route ({route}) and comes back along it once.");
                }
            }

            previous = stay;
        }

        return Finding.Held;
    }

    private static Finding Stay(TripService service, Criterion c)
    {
        var place = c.Argument(0)!;
        var from = DateOnly.Parse(c.Argument(1)!);
        var to = DateOnly.Parse(c.Argument(2)!);
        var nights = int.Parse(c.Argument(3)!);

        if (service.Search(place, from, to, nights).Count > 0)
            return Finding.Held;

        var free = string.Join(
            "; ", service.FreeNights(place, from, to).Select(p => $"{p.Key} {FreeNightsText(p.Value)}"));

        return Finding.Not(
            $"{place}: no stay is free for {nights} night(s) in a row between {from:yyyy-MM-dd} and "
            + $"{to:yyyy-MM-dd}. Free nights in that range: {free}.");
    }

    private static Finding StayOn(TripService service, Criterion c)
    {
        var place = c.Argument(0)!;
        var checkIn = DateOnly.Parse(c.Argument(1)!);
        var checkOut = DateOnly.Parse(c.Argument(2)!);

        if (service.Search(place, checkIn, checkOut, checkOut.DayNumber - checkIn.DayNumber).Count > 0)
            return Finding.Held;

        var free = string.Join(
            "; ", service.FreeNights(place, checkIn, checkOut).Select(p => $"{p.Key} {FreeNightsText(p.Value)}"));

        return Finding.Not(
            $"{place}: no stay is free every night from {checkIn:yyyy-MM-dd} to {checkOut:yyyy-MM-dd}. "
            + $"Free nights in that range: {free}.");
    }

    private static string? SetDates(TripService service, Criterion c)
    {
        if (c.Argument(0) is not { } place || !service.Knows(place))
            return $"there is nowhere called '{c.Argument(0)}'";

        if (c.Argument(1) is not { } inText || !DateOnly.TryParseExact(inText, "yyyy-MM-dd", out var checkIn))
            return $"'{c.Argument(1)}' is not a date written yyyy-MM-dd";

        if (c.Argument(2) is not { } outText || !DateOnly.TryParseExact(outText, "yyyy-MM-dd", out var checkOut))
            return $"'{c.Argument(2)}' is not a date written yyyy-MM-dd";

        return checkOut <= checkIn ? "the check-out must be after the check-in" : null;
    }

    private static async Task<Finding> WeekPlannedAsync(
        Func<CancellationToken, Task<PlanTree?>> plan, Criterion c, CancellationToken ct)
    {
        var from = DateOnly.Parse(c.Argument(0)!);
        var to = DateOnly.Parse(c.Argument(1)!);
        var nights = int.Parse(c.Argument(2)!);

        var tree = await plan(ct).ConfigureAwait(false);
        var stays = tree is null ? [] : PlannedStay.In(tree);

        var owners = new Dictionary<DateOnly, List<string>>();

        foreach (var stay in stays.Where(s => s.OnSetDates))
        {
            for (var night = stay.CheckIn; night < stay.CheckOut; night = night.AddDays(1))
            {
                if (!owners.TryGetValue(night, out var list))
                    owners[night] = list = [];

                list.Add(stay.Step);
            }
        }

        var inRange = owners.Where(p => p.Key >= from && p.Key < to).ToList();
        var doubled = inRange.Where(p => p.Value.Count > 1).Select(p => p.Key).Order().ToList();
        var planned = inRange.Select(p => p.Key).Order().ToList();

        var oneRun = doubled.Count == 0
            && planned.Count == nights
            && planned.Select((d, i) => d == planned[0].AddDays(i)).All(ok => ok);

        if (oneRun)
            return Finding.Held;

        var listed = stays.Count == 0
            ? "no step asks for a stay"
            : string.Join(
                "; ", stays.Select(s => $"{s.Step} {s.Place} {s.CheckIn:yyyy-MM-dd} to {s.CheckOut:yyyy-MM-dd}, {s.Nights} night(s)"));

        var detail = $"{planned.Count} night(s) are planned between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}, "
            + $"and {nights} in a row are needed: {listed}.";

        var gaps = new List<DateOnly>();

        for (var night = planned.FirstOrDefault(); planned.Count > 0 && night < planned[^1]; night = night.AddDays(1))
        {
            if (!owners.ContainsKey(night))
                gaps.Add(night);
        }

        if (gaps.Count > 0)
            detail += $" Not in a row: nothing is planned on {string.Join(", ", gaps.Select(d => d.ToString("yyyy-MM-dd")))}.";

        if (doubled.Count > 0)
            detail += $" Planned twice: {string.Join(", ", doubled.Select(d => d.ToString("yyyy-MM-dd")))}.";

        if (stays.Where(s => !s.OnSetDates).Select(s => s.Step).ToList() is { Count: > 0 } open)
            detail += $" Not on set dates yet: {string.Join(", ", open)}.";

        return Finding.Not(detail);
    }

    private static string FreeNightsText(IReadOnlyList<DateOnly>? nights) => nights switch
    {
        null => "every night",
        { Count: 0 } => "none",
        _ => string.Join(", ", nights.Select(d => d.ToString("yyyy-MM-dd")))
    };

    private static string? Arguments(TripService service, Criterion c, bool withPlace)
    {
        var offset = withPlace ? 1 : 0;

        if (withPlace)
        {
            var place = c.Argument(0);

            if (place is null || !service.Knows(place))
                return $"there is nowhere called '{place}'";
        }

        if (c.Argument(offset) is not { } fromText || !DateOnly.TryParse(fromText, out var from))
            return $"'{c.Argument(offset)}' is not a date written yyyy-MM-dd";

        if (c.Argument(offset + 1) is not { } toText || !DateOnly.TryParse(toText, out var to))
            return $"'{c.Argument(offset + 1)}' is not a date written yyyy-MM-dd";

        if (to <= from)
            return "the check-out must be after the check-in";

        var span = to.DayNumber - from.DayNumber;

        if (!int.TryParse(c.Argument(offset + 2), out var nights) || nights < 1 || nights > span)
            return $"nights must be between 1 and {span}";

        return null;
    }
}
