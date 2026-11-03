using System.Globalization;
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

    /// <summary>
    /// What the whole run may spend — planning and the proposal's review — in the currency the rates
    /// are declared in.
    /// </summary>
    public decimal Budget { get; init; } = BudgetByDefault;

    /// <summary>The ceiling when <c>--budget</c> is not given.</summary>
    /// <remarks>
    /// <b>Set here because nothing invents one.</b> What a run may spend is the judgement of whoever
    /// pays for it; this is the demo's, and <c>--budget</c> is the reader's.
    /// </remarks>
    public const decimal BudgetByDefault = 1.00m;

    /// <summary>The run takes the supervisor's advice inline when nobody is watching.</summary>
    public bool Autopilot => !Attended;

    /// <summary>Every flag this demo answers to, so an unknown one can be named as unknown.</summary>
    private static readonly string[] Known =
        ["--hitl", "--auto", "--autopilot", "--verify", "--plan", "--budget"];

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

        if (!Value(args, "--plan", "a file path", out var plan, out var planValueIndex, out complaint)
            || !Value(args, "--budget", "an amount", out var budgetText, out var budgetValueIndex, out complaint))
        {
            return null;
        }

        var budget = BudgetByDefault;

        if (budgetText is not null
            && (!decimal.TryParse(budgetText, NumberStyles.Number, CultureInfo.InvariantCulture, out budget)
                || budget <= 0))
        {
            complaint = $"--budget needs a positive amount, such as 0.50; got '{budgetText}'.";
            return null;
        }

        // The provider is the first bare argument that is neither a flag nor the value a flag
        // just consumed — so a plan file's path is never mistaken for it.
        var provider = (args
            .Select((a, i) => (Arg: a, Index: i))
            .Where(x => !x.Arg.StartsWith('-') && x.Index != planValueIndex && x.Index != budgetValueIndex)
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
            Plan = plan ?? "plan/trip.yml",
            Budget = budget
        };
    }

    /// <summary>The value after <paramref name="flag"/>, if the flag is there.</summary>
    private static bool Value(
        string[] args, string flag, string what, out string? value, out int index, out string? complaint)
    {
        value = null;
        index = -1;
        complaint = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                continue;

            if (i + 1 >= args.Length)
            {
                complaint = $"{flag} needs {what}.";
                return false;
            }

            value = args[i + 1];
            index = i + 1;
            return true;
        }

        return true;
    }

    private static bool Has(string[] args, string flag) =>
        args.Contains(flag, StringComparer.OrdinalIgnoreCase);
}
