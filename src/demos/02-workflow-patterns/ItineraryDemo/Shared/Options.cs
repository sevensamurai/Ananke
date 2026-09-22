using ItineraryDemo.Model;

namespace ItineraryDemo.Shared;

/// <summary>
/// What the command line asked for.
/// </summary>
/// <remarks>
/// <b>Parsed once, into names, before anything reads a flag.</b> The flags used to be tested where
/// they were needed — <c>args.Contains("--hitl")</c> three files apart — which is how
/// <c>--flexible</c> came to be advertised in the usage text and read by nothing at all.
/// </remarks>
internal sealed record Options
{
    /// <summary>Which provider's models to run on.</summary>
    public required string Provider { get; init; }

    /// <summary>Whether a person answers what the plan cannot.</summary>
    public required bool Attended { get; init; }

    /// <summary>Whether this is a premise check rather than a run.</summary>
    public bool Verify { get; init; }

    /// <summary>Which plan file to load, relative to the binary.</summary>
    public string Plan { get; init; } = "plan/trip.yml";

    /// <summary>The run takes the supervisor's advice inline when nobody is watching.</summary>
    public bool Autopilot => !Attended;

    /// <summary>Every flag this demo answers to, so an unknown one can be named as unknown.</summary>
    private static readonly string[] Known =
        ["--hitl", "--auto", "--autopilot", "--verify", "--plan"];

    /// <summary>
    /// Reads <paramref name="args"/>, or says which flag it does not know.
    /// </summary>
    /// <remarks>
    /// <b>An unknown flag is an error, not a shrug.</b> Silently ignoring one is how a run reports
    /// having done something it was never asked to do — and the reader believes it, because they
    /// typed the flag.
    /// </remarks>
    public static Options? Read(string[] args, out string? complaint)
    {
        complaint = null;

        var flags = args.Where(a => a.StartsWith('-')).ToList();

        if (flags.FirstOrDefault(f => !Known.Contains(f, StringComparer.OrdinalIgnoreCase)) is { } stray)
        {
            complaint = $"Unknown option '{stray}'. Known options: {string.Join(", ", Known)}.";
            return null;
        }

        string? plan = null;
        var planValueIndex = -1;

        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--plan", StringComparison.OrdinalIgnoreCase))
                continue;

            if (i + 1 >= args.Length)
            {
                complaint = "--plan needs a file path.";
                return null;
            }

            plan = args[i + 1];
            planValueIndex = i + 1;
            break;
        }

        // The provider is the first bare argument that is neither a flag nor the value a flag
        // just consumed — so a plan file's path is never mistaken for it.
        var provider = (args
            .Select((a, i) => (Arg: a, Index: i))
            .Where(x => !x.Arg.StartsWith('-') && x.Index != planValueIndex)
            .Select(x => x.Arg)
            .FirstOrDefault()
            ?? EnvKeys.Get("ANANKE_DEMO_PROVIDER")
            ?? "auto").TrimStart('-');

        if (!TripModels.Providers.Contains(provider, StringComparer.OrdinalIgnoreCase))
        {
            complaint =
                $"Unknown provider '{provider}'. Use one of: {string.Join(", ", TripModels.Providers)}.";
            return null;
        }

        // `--hitl` puts a person at the pause; everything else lets the run take the supervisor's
        // recommendation inline. They are one axis, not two lanes.
        return new Options
        {
            Provider = provider,
            Attended = Has(args, "--hitl") && !Has(args, "--auto") && !Has(args, "--autopilot"),
            Verify = Has(args, "--verify"),
            Plan = plan ?? "plan/trip.yml"
        };
    }

    private static bool Has(string[] args, string flag) =>
        args.Contains(flag, StringComparer.OrdinalIgnoreCase);
}
