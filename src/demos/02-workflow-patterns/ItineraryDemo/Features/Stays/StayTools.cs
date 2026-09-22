using System.Globalization;
using System.Text.Json;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Tools;
using ItineraryDemo.Model;

namespace ItineraryDemo.Features.Stays;

/// <summary>
/// What a step and the Planner look up: which stays are free, and whether a criterion can be met.
/// </summary>
/// <remarks>
/// Every tool here only reads the service. A step reports the stays it found, and nothing is held for
/// anybody.
/// </remarks>
internal sealed class StayTools(TripService service)
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>The one tool a step has: search the service for a stay.</summary>
    public ToolKit Search() => new ToolKit("stays")
        .AddTool(
            "search_stays",
            "The stays in a place free for that many nights in a row between two dates, best first, "
                + "each with the dates it can be checked into. Dates are yyyy-MM-dd; 'to' is the "
                + "latest check-out.",
            tool => tool
                .Param("place", "The place to search.")
                .Param("from", "The earliest check-in, yyyy-MM-dd.")
                .Param("to", "The latest check-out, yyyy-MM-dd.")
                .Param("nights", "How many nights in a row.")
                .OnExecute(args => Task.FromResult(Run(args))));

    /// <summary>
    /// What the Planner consults: the search, the route, each stay's free nights, and whether a stay on
    /// set dates is free.
    /// </summary>
    public ToolKit Planning() => Search()
        .AddTool(
            "route",
            "The places in the order they lie along the way, first to last. A trip goes out along them "
                + "and may come back along them once, but does not go out again.",
            tool => tool.OnExecute(_ => Task.FromResult(ToolResult.Ok(Json(service.Route)))))
        .AddTool(
            "free_nights",
            "The nights each stay in a place is free between two dates, best stay first. Dates are "
                + "yyyy-MM-dd; 'to' is the latest check-out.",
            tool => tool
                .Param("place", "The place.")
                .Param("from", "The first night, yyyy-MM-dd.")
                .Param("to", "The latest check-out, yyyy-MM-dd.")
                .OnExecute(args => Task.FromResult(FreeNights(args))))
        .AddTool(
            "check_stay",
            "Whether a stay in a place is free every night from check_in to check_out. Dates are yyyy-MM-dd.",
            tool => tool
                .Param("place", "The place.")
                .Param("check_in", "The first night, yyyy-MM-dd.")
                .Param("check_out", "The morning of leaving, yyyy-MM-dd.")
                .OnExecute(args => Task.FromResult(CheckStay(args.Get("place"), args.Get("check_in"), args.Get("check_out")))));

    private ToolResult Run(ToolArgs args)
    {
        var place = args.Get("place");

        if (Unknown(place) is { } unknown)
            return unknown;

        if (!TryDate(args.Get("from"), out var from))
            return ToolResult.Error("'from' must be a date written yyyy-MM-dd.");

        if (!TryDate(args.Get("to"), out var to))
            return ToolResult.Error("'to' must be a date written yyyy-MM-dd.");

        if (!int.TryParse(args.Get("nights"), out var nights) || nights < 1)
            return ToolResult.Error("'nights' must be a whole number of at least 1.");

        var found = service.Search(place, from, to, nights);

        if (found.Count > 0)
        {
            return ToolResult.Ok(Json(new
            {
                stays = found.Select(s => new
                {
                    stay = s.Stay,
                    rank = s.Rank,
                    checkIns = s.CheckIns.Select(Text)
                })
            }));
        }

        return ToolResult.Ok(Json(new
        {
            stays = Array.Empty<object>(),
            freeNights = FreeNightsIn(place, from, to)
        }));
    }

    private ToolResult FreeNights(ToolArgs args)
    {
        if (Unknown(args.Get("place")) is { } unknown)
            return unknown;

        if (!TryDate(args.Get("from"), out var from))
            return ToolResult.Error("'from' must be a date written yyyy-MM-dd.");

        if (!TryDate(args.Get("to"), out var to))
            return ToolResult.Error("'to' must be a date written yyyy-MM-dd.");

        return ToolResult.Ok(Json(FreeNightsIn(args.Get("place"), from, to)));
    }

    /// <summary>An error naming the places there are, when <paramref name="place"/> is not one of them.</summary>
    /// <remarks>An empty search for a place that does not exist reads as a place with nothing free.</remarks>
    private ToolResult? Unknown(string place) =>
        service.Knows(place)
            ? null
            : (ToolResult?)ToolResult.Error($"there is nowhere called '{place}'. 'place' is one of: {string.Join(", ", service.Route)}.");

    /// <summary>Each stay's free nights, with "every night" written out rather than left as null.</summary>
    /// <remarks>A model reads a null as nothing free.</remarks>
    private Dictionary<string, object> FreeNightsIn(string place, DateOnly from, DateOnly to) =>
        service.FreeNights(place, from, to).ToDictionary(
            p => p.Key,
            p => p.Value is null ? "every night" : (object)p.Value.Select(Text).ToArray());

    private ToolResult CheckStay(string place, string checkInText, string checkOutText)
    {
        if (Unknown(place) is { } unknown)
            return unknown;

        if (!TryDate(checkInText, out var checkIn))
            return ToolResult.Error("'check_in' must be a date written yyyy-MM-dd.");

        if (!TryDate(checkOutText, out var checkOut) || checkOut <= checkIn)
            return ToolResult.Error("'check_out' must be a date written yyyy-MM-dd, after 'check_in'.");

        return service.Search(place, checkIn, checkOut, checkOut.DayNumber - checkIn.DayNumber).FirstOrDefault() is { } free
            ? ToolResult.Ok($"{place} from {Text(checkIn)} to {Text(checkOut)} is free at {free.Stay}.")
            : ToolResult.Ok(Json(new
            {
                free = false,
                freeNights = FreeNightsIn(place, checkIn, checkOut)
            }));
    }

    private static string Text(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static bool TryDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
}
