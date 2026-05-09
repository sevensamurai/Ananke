using Ananke.Federation.Deployment;
using Ananke.Federation.Monitoring;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Platform.Commands;

/// <summary>
/// Handles <c>nnke-platform trends [--deployment-id &lt;id&gt;]</c> —
/// shows metrics trends for active deployments. Uses the
/// <see cref="RemoteMetricsTracker"/> to detect struggling generalist patterns.
/// </summary>
internal static class TrendsCommand
{
    public static Command Create()
    {
        var deploymentIdOption = new Option<string?>("--deployment-id")
        {
            Description = "Specific deployment ID to show trends for. Omit to show all trackable deployments."
        };

        var command = new Command("trends", "Show metrics trends (tokens/exec, tool-calls/exec) for remote deployments.")
        {
            deploymentIdOption
        };

        command.SetAction(parseResult =>
        {
            var deploymentId = parseResult.GetValue(deploymentIdOption);
            var json = parseResult.GetValue<bool>("--json");
            Execute(deploymentId, json);
        });

        return command;
    }

    private static void Execute(string? deploymentId, bool json)
    {
        // In a real scenario, this would load from a persistent store or connect to
        // an OTEL backend. For now, demonstrate the shape by connecting to an
        // in-memory tracker (useful when embedded in a long-running host process).
        var tracker = ResolveTracker();

        if (deploymentId is not null)
        {
            var trend = tracker.GetTrend(deploymentId);
            if (trend is null)
            {
                EmitNoData(deploymentId, json);
                return;
            }
            EmitTrend(trend, json);
        }
        else
        {
            var trackable = tracker.GetTrackableDeployments();
            if (trackable.Count == 0)
            {
                EmitEmpty(json);
                return;
            }

            var trends = trackable
                .Select(id => tracker.GetTrend(id))
                .Where(t => t is not null)
                .ToList();

            EmitTrends(trends!, json);
        }
    }

    private static void EmitTrend(RemoteCellTrend trend, bool json)
    {
        if (json)
        {
            JsonOutput.Write(new
            {
                deploymentId = trend.DeploymentId,
                tokensPerExecutionSlope = Math.Round(trend.TokensPerExecutionSlope, 4),
                toolCallsPerExecutionSlope = Math.Round(trend.ToolCallsPerExecutionSlope, 4),
                errorRateSlope = Math.Round(trend.ErrorRateSlope, 4),
                sampleCount = trend.SampleCount,
                isStrugglingGeneralist = trend.IsStrugglingGeneralist,
                isStable = trend.IsStable,
                computedAt = trend.ComputedAt
            });
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine($"  Deployment: {trend.DeploymentId}");
            Console.WriteLine($"  Samples:    {trend.SampleCount}");
            Console.WriteLine($"  Tokens/exec slope:     {trend.TokensPerExecutionSlope:+0.000;-0.000} {Indicator(trend.TokensPerExecutionSlope)}");
            Console.WriteLine($"  Tool-calls/exec slope: {trend.ToolCallsPerExecutionSlope:+0.000;-0.000} {Indicator(trend.ToolCallsPerExecutionSlope)}");
            Console.WriteLine($"  Error rate slope:      {trend.ErrorRateSlope:+0.000;-0.000} {Indicator(trend.ErrorRateSlope)}");
            Console.WriteLine($"  Status: {(trend.IsStrugglingGeneralist ? "⚠ STRUGGLING GENERALIST — consider division" : trend.IsStable ? "✓ Stable" : "~ Trending")}");
            Console.WriteLine();
        }
    }

    private static void EmitTrends(List<RemoteCellTrend> trends, bool json)
    {
        if (json)
        {
            JsonOutput.Write(new
            {
                deployments = trends.Select(t => new
                {
                    deploymentId = t.DeploymentId,
                    tokensPerExecutionSlope = Math.Round(t.TokensPerExecutionSlope, 4),
                    toolCallsPerExecutionSlope = Math.Round(t.ToolCallsPerExecutionSlope, 4),
                    isStrugglingGeneralist = t.IsStrugglingGeneralist,
                    sampleCount = t.SampleCount
                })
            });
        }
        else
        {
            Console.WriteLine();
            foreach (var t in trends)
            {
                var status = t.IsStrugglingGeneralist ? "⚠ STRUGGLING" : t.IsStable ? "✓ Stable" : "~ Trending";
                Console.WriteLine($"  {t.DeploymentId,-30} tokens/exec: {t.TokensPerExecutionSlope:+0.000;-0.000}  calls/exec: {t.ToolCallsPerExecutionSlope:+0.000;-0.000}  [{status}]");
            }
            Console.WriteLine();
        }
    }

    private static void EmitNoData(string deploymentId, bool json)
    {
        if (json)
            JsonOutput.Write(new { status = "no-data", message = $"Insufficient samples for deployment '{deploymentId}'. Need at least 5 polling intervals.", deploymentId });
        else
        {
            Console.WriteLine();
            Console.WriteLine($"  No trend data for '{deploymentId}'. Need at least 5 polling intervals.");
            Console.WriteLine();
        }
    }

    private static void EmitEmpty(bool json)
    {
        if (json)
            JsonOutput.Write(new { status = "no-data", message = "No deployments with enough samples for trend analysis." });
        else
        {
            Console.WriteLine();
            Console.WriteLine("  No deployments with enough samples for trend analysis.");
            Console.WriteLine("  Trends require at least 5 polling intervals of data.");
            Console.WriteLine();
        }
    }

    private static string Indicator(double slope) =>
        slope > 0.05 ? "↑ worse" : slope < -0.05 ? "↓ better" : "— stable";

    /// <summary>
    /// Resolves the metrics tracker. In a CLI context, this would connect to
    /// a persistent store or OTEL backend query. For now, returns an empty
    /// tracker (useful for testing the command shape).
    /// </summary>
    private static RemoteMetricsTracker ResolveTracker()
    {
        // TODO: Connect to persistent metrics source (OTEL backend query,
        // or shared state file from a running OrganicHost process).
        return new RemoteMetricsTracker();
    }
}
