using System.Text.Json;

namespace ItineraryDemo.Model;

/// <summary>One stay a place offers, and when it is free.</summary>
internal sealed record Stay
{
    public required string Id { get; init; }

    /// <summary>How the service ranks it against the others in the same place. Lower is better.</summary>
    public required int Rank { get; init; }

    /// <summary>The nights it is free. <see langword="null"/> means free every night.</summary>
    public IReadOnlyList<DateOnly>? FreeNights { get; init; }
}

/// <summary><c>service.json</c> as written: the route, then each place's stays.</summary>
internal sealed record ServiceFile
{
    public List<string>? Route { get; init; }

    public Dictionary<string, List<Stay>>? Stays { get; init; }
}

/// <summary>A stay <see cref="TripService.Search"/> found, and the nights it could be checked into.</summary>
internal sealed record StayOption(string Stay, int Rank, IReadOnlyList<DateOnly> CheckIns);

/// <summary>
/// The mock availability service: the route places lie along, what each place offers, and the nights
/// each stay is free.
/// </summary>
/// <remarks>
/// It only answers questions. A plan is made against what it says is free, and nothing asks it to
/// hold anything.
/// </remarks>
internal sealed class TripService(IReadOnlyDictionary<string, IReadOnlyList<Stay>> stays, IReadOnlyList<string> route)
{
    public IEnumerable<string> Places => stays.Keys;

    /// <summary>The places in the order they lie along the way, first to last.</summary>
    public IReadOnlyList<string> Route => route;

    /// <summary>Where <paramref name="place"/> lies on the route, or -1 when it is not on it.</summary>
    public int RouteIndex(string place)
    {
        for (var i = 0; i < route.Count; i++)
        {
            if (string.Equals(route[i], place, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    public bool Knows(string place) => stays.ContainsKey(place);

    /// <summary>
    /// The stays in <paramref name="place"/> that can hold <paramref name="nights"/> nights in a row
    /// somewhere inside <c>[from, to)</c>, best first, each with every check-in that would work.
    /// </summary>
    public IReadOnlyList<StayOption> Search(string place, DateOnly from, DateOnly to, int nights)
    {
        if (!stays.TryGetValue(place, out var offered))
            return [];

        var found = new List<StayOption>();

        foreach (var stay in offered.OrderBy(s => s.Rank))
        {
            var checkIns = new List<DateOnly>();

            for (var checkIn = from; checkIn.AddDays(nights) <= to; checkIn = checkIn.AddDays(1))
            {
                if (IsFreeThroughout(stay, checkIn, nights))
                    checkIns.Add(checkIn);
            }

            if (checkIns.Count > 0)
                found.Add(new StayOption(stay.Id, stay.Rank, checkIns));
        }

        return found;
    }

    /// <summary>
    /// Each of the place's stays, best first, with its free nights inside <c>[from, to)</c>.
    /// <see langword="null"/> means every night in the range is free.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<DateOnly>?> FreeNights(string place, DateOnly from, DateOnly to)
    {
        if (!stays.TryGetValue(place, out var offered))
            return new Dictionary<string, IReadOnlyList<DateOnly>?>();

        return offered
            .OrderBy(s => s.Rank)
            .ToDictionary(
                s => s.Id,
                IReadOnlyList<DateOnly>? (s) => s.FreeNights is null
                    ? null
                    : [.. s.FreeNights.Where(d => d >= from && d < to)]);
    }

    private static bool IsFreeThroughout(Stay stay, DateOnly checkIn, int nights)
    {
        for (var night = checkIn; night < checkIn.AddDays(nights); night = night.AddDays(1))
        {
            if (!IsFree(stay, night))
                return false;
        }

        return true;
    }

    private static bool IsFree(Stay stay, DateOnly night) =>
        stay.FreeNights is null || stay.FreeNights.Contains(night);

    /// <summary>Reads the facts, and says what is wrong with them rather than throwing at a reader.</summary>
    /// <remarks>
    /// A demo that ends in a stack trace has taught somebody about <c>System.Text.Json</c> rather
    /// than about plans.
    /// </remarks>
    public static TripService Load(string planFolder)
    {
        var path = Path.Combine(planFolder, "service.json");

        try
        {
            var raw = JsonSerializer.Deserialize<ServiceFile>(
                File.ReadAllText(path),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                })
                ?? throw new InvalidOperationException($"{path} is empty.");

            if (raw.Stays is not { Count: > 0 } offered)
                throw new InvalidOperationException($"{path} lists no stays.");

            var stays = offered.ToDictionary(
                p => p.Key,
                IReadOnlyList<Stay> (p) => p.Value,
                StringComparer.OrdinalIgnoreCase);

            var route = raw.Route ?? [];

            if (route.FirstOrDefault(place => !stays.ContainsKey(place)) is { } unknown)
                throw new InvalidOperationException($"{path}: the route names '{unknown}', which has no stays.");

            return new TripService(stays, route);
        }
        catch (JsonException malformed)
        {
            throw new InvalidOperationException(
                $"{path} could not be read: {malformed.Message}", malformed);
        }
    }
}
