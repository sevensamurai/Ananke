using Ananke.Federation.Monitoring;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Platform.Commands;

/// <summary>
/// Handles <c>nnke-platform compare &lt;cell&gt; --across &lt;platforms&gt;</c> —
/// reads <see cref="RemoteMetricsTracker"/> trend data per platform for the
/// named cell and outputs a comparison table highlighting cheapest/fastest.
/// </summary>
internal static class CompareCommand
{
    public static Command Create()
    {
        var cellArg = new Argument<string>("cell")
        {
            Description = "Cell / workflow name to compare across platforms."
        };

        var acrossOption = new Option<string[]>("--across")
        {
            Description = "Comma-separated list of platforms to compare (azure, google, anthropic).",
            DefaultValueFactory = _ => ["azure", "google", "anthropic"],
            AllowMultipleArgumentsPerToken = true
        };

        var command = new Command("compare",
            "Compare token/latency/error metrics across platforms for a named cell.")
        {
            cellArg,
            acrossOption
        };

        command.SetAction(parseResult =>
        {
            var cell = parseResult.GetValue(cellArg)!;
            var platforms = SplitPlatforms(parseResult.GetValue(acrossOption)!);
            var json = parseResult.GetValue<bool>("--json");
            return Execute(cell, platforms, json);
        });

        return command;
    }

    /// <summary>
    /// Flattens the parsed <c>--across</c> values into individual platform names.
    /// System.CommandLine splits on whitespace only, so the documented comma-separated
    /// form (<c>--across azure-ai,claude</c>) arrives as a single token and has to be
    /// split here — otherwise the comma-joined string is treated as one platform name.
    /// </summary>
    private static string[] SplitPlatforms(string[] values) =>
        [.. values
            .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static int Execute(string cellName, string[] platforms, bool json)
    {
        // Resolve in-memory tracker (in a real host this would be injected).
        var tracker = new RemoteMetricsTracker();

        var rows = platforms.Select(platform =>
        {
            var deploymentId = $"{cellName}@{platform}";
            var trend = tracker.GetTrend(deploymentId);

            return new
            {
                platform,
                deploymentId,
                tokensPerExecSlope = trend?.TokensPerExecutionSlope,
                errorRateSlope = trend?.ErrorRateSlope,
                dataPoints = trend?.SampleCount ?? 0
            };
        }).ToList();

        // Flag best candidates (where data is available)
        var withData = rows.Where(r => r.tokensPerExecSlope.HasValue).ToList();
        var bestTrend = withData.OrderBy(r => r.tokensPerExecSlope).FirstOrDefault()?.platform;

        if (json)
        {
            JsonOutput.Write(new
            {
                status = "ok",
                cell = cellName,
                comparison = rows,
                bestTrend,
                note = "Slopes show relative change per sample interval (+/- = rising/falling). Connect to OTEL for persistent history (v0.9)."
            });
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"  Platform comparison for: {cellName}");
        Console.WriteLine();
        Console.WriteLine($"  {"Platform",-12} {"Tokens slope",-16} {"Error slope",-14} {"Data pts"}");
        Console.WriteLine($"  {new string('─', 60)}");

        foreach (var r in rows)
        {
            var tokens = r.tokensPerExecSlope.HasValue ? $"{r.tokensPerExecSlope:+0.000;-0.000}" : "—";
            var error = r.errorRateSlope.HasValue ? $"{r.errorRateSlope:+0.000;-0.000}" : "—";
            var best = r.platform == bestTrend ? " ★" : "";
            Console.WriteLine($"  {r.platform,-12} {tokens,-16} {error,-14} {r.dataPoints}{best}");
        }

        if (withData.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("  No trend data available. Deploy to platforms and record metrics first.");
        }

        Console.WriteLine();
        Console.WriteLine("  Note: Connect to an OTEL backend for persistent trend history (v0.9).");
        Console.WriteLine();

        return 0;
    }
}
