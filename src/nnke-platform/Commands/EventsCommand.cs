using Ananke.Federation.Deployment;
using Ananke.Federation.Monitoring;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Platform.Commands;

/// <summary>
/// Handles <c>nnke-platform events [--follow]</c> — polls colony and deployment
/// signals on a 2-second interval, printing division, stress, and deploy events.
/// In v0.8.0 polling is finite; a persistent SSE transport is v0.9.
/// </summary>
internal static class EventsCommand
{
    public static Command Create()
    {
        var followOption = new Option<bool>("--follow")
        {
            Description = "Keep streaming until Ctrl+C (polls every 2 seconds). Without this flag prints one snapshot."
        };

        var command = new Command("events",
            "Stream colony events: division signals, stress changes, deploy/teardown. " +
            "Polls every 2 seconds; persistent SSE streaming is a v0.9 upgrade.")
        {
            followOption
        };

        command.SetAction(async parseResult =>
        {
            var follow = parseResult.GetValue(followOption);
            var json = parseResult.GetValue<bool>("--json");
            await ExecuteAsync(follow, json);
        });

        return command;
    }

    private static async Task ExecuteAsync(bool follow, bool json)
    {
        var tracker = new RemoteMetricsTracker();
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        if (!json)
        {
            Console.WriteLine();
            Console.WriteLine("  Listening for colony events... (Ctrl+C to stop)");
            Console.WriteLine();
        }

        do
        {
            var deployments = tracker.GetTrackableDeployments();
            var timestamp = DateTimeOffset.UtcNow;

            var events = deployments.Select(d =>
            {
                var trend = tracker.GetTrend(d);
                return new
                {
                    deploymentId = d,
                    timestamp,
                    tokensPerExecSlope = trend?.TokensPerExecutionSlope,
                    errorRateSlope = trend?.ErrorRateSlope,
                    sampleCount = trend?.SampleCount ?? 0
                };
            }).ToList();

            if (json)
            {
                JsonOutput.Write(new { status = "ok", timestamp, events });
            }
            else if (events.Count == 0)
            {
                Console.WriteLine($"  [{timestamp:HH:mm:ss}]  No trackable deployments. Deploy workflows and record metrics first.");
            }
            else
            {
                foreach (var e in events)
                {
                    var tokens = e.tokensPerExecSlope.HasValue ? $"{e.tokensPerExecSlope:+0.000;-0.000}" : "—";
                    var error = e.errorRateSlope.HasValue ? $"{e.errorRateSlope:+0.000;-0.000}" : "—";
                    Console.WriteLine($"  [{timestamp:HH:mm:ss}]  {e.deploymentId,-30}  tokens_slope={tokens}  error_slope={error}");
                }
            }

            if (!follow) break;

            try { await Task.Delay(2_000, cts.Token); }
            catch (OperationCanceledException) { break; }

        } while (!cts.Token.IsCancellationRequested);

        if (!json)
        {
            Console.WriteLine();
            Console.WriteLine("  Note: Live SSE streaming and DivisionSignal events require v0.9.");
            Console.WriteLine();
        }
    }
}
