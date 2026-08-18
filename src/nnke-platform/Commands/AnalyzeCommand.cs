using Ananke.Design;
using Ananke.Federation.Monitoring;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Platform.Commands;

/// <summary>
/// Handles <c>nnke-platform analyze &lt;file&gt; [--deployment-id &lt;id&gt;]</c> —
/// compares manifest structure against runtime trends and recommends division
/// or platform placement changes.
/// </summary>
/// <remarks>
/// <para>
/// This is the design-time counterpart to runtime trend detection. It tells the
/// operator: "your workflow has N tools consuming X% of context window, and
/// metrics show tokens/exec rising — consider dividing."
/// </para>
/// </remarks>
internal static class AnalyzeCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<FileInfo>("file")
        {
            Description = "Path to the .ananke.yml manifest file."
        };

        var deploymentIdOption = new Option<string?>("--deployment-id")
        {
            Description = "Active deployment ID to correlate with runtime metrics trends."
        };

        var command = new Command("analyze", "Analyze a manifest's structural complexity and correlate with runtime metrics.")
        {
            fileArg,
            deploymentIdOption
        };

        command.SetAction(parseResult =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var deploymentId = parseResult.GetValue(deploymentIdOption);
            var json = parseResult.GetValue<bool>("--json");
            return Execute(file, deploymentId, json);
        });

        return command;
    }

    private static int Execute(FileInfo file, string? deploymentId, bool json)
    {
        if (!file.Exists)
        {
            if (json)
                JsonOutput.Write(new { status = "error", message = $"File not found: {file.FullName}" });
            else
                Console.Error.WriteLine($"  File not found: {file.FullName}");
            return 1;
        }

        WorkflowManifest manifest;
        try
        {
            manifest = WorkflowManifest.Load(file.FullName);
        }
        catch (Exception ex)
        {
            if (json)
                JsonOutput.Write(new { status = "error", message = $"Failed to parse manifest: {ex.Message}" });
            else
                Console.Error.WriteLine($"  Failed to parse manifest: {ex.Message}");
            return 1;
        }

        var analysis = AnalyzeManifest(manifest, deploymentId);
        Emit(analysis, json);

        return 0;
    }

    private static AnalysisResult AnalyzeManifest(WorkflowManifest manifest, string? deploymentId)
    {
        var jobCount = manifest.Jobs.Count;

        // Estimate tool count from jobs (heuristic — real count needs toolkit)
        // Each agent job typically has 3-8 tools
        var estimatedToolCount = manifest.Jobs.Values
            .Count(j => string.Equals(j.Type, "agent", StringComparison.OrdinalIgnoreCase)) * 5;

        // Estimate context utilization: ~500 tokens per tool definition average
        const int avgTokensPerTool = 500;
        const int defaultContextWindow = 128_000;
        var estimatedContextUtil = (double)estimatedToolCount * avgTokensPerTool / defaultContextWindow;

        var recommendations = new List<string>();

        if (estimatedToolCount >= 6)
            recommendations.Add($"High tool density ({estimatedToolCount} estimated tools). Consider dividing into specialist cells.");

        if (estimatedContextUtil > 0.3)
            recommendations.Add($"Context utilization ~{estimatedContextUtil:P0}. Tool definitions consume significant context — division would reduce per-cell overhead.");

        if (jobCount == 1 && estimatedToolCount >= 8)
            recommendations.Add("Single-job workflow with many tools. A multi-job topology or division would reduce routing entropy.");

        // Check for trend correlation
        RemoteCellTrend? trend = null;
        if (deploymentId is not null)
        {
            var tracker = ResolveTracker();
            trend = tracker.GetTrend(deploymentId);

            if (trend?.IsStrugglingGeneralist == true)
                recommendations.Add($"⚠ Runtime metrics confirm struggling generalist pattern (tokens/exec slope: {trend.TokensPerExecutionSlope:+0.000}, tool-calls/exec slope: {trend.ToolCallsPerExecutionSlope:+0.000}). Division is strongly recommended.");
            else if (trend?.IsStable == true)
                recommendations.Add("Runtime metrics are stable — no immediate division pressure from execution patterns.");
        }

        if (recommendations.Count == 0)
            recommendations.Add("Workflow structure looks healthy. No division recommended at this time.");

        return new AnalysisResult(
            manifest.Name,
            jobCount,
            estimatedToolCount,
            estimatedContextUtil,
            trend,
            recommendations);
    }

    private static void Emit(AnalysisResult result, bool json)
    {
        if (json)
        {
            JsonOutput.Write(new
            {
                workflow = result.WorkflowName,
                jobCount = result.JobCount,
                estimatedToolCount = result.EstimatedToolCount,
                estimatedContextUtilization = Math.Round(result.EstimatedContextUtil, 3),
                hasTrendData = result.Trend is not null,
                trend = result.Trend is not null ? new
                {
                    tokensPerExecutionSlope = Math.Round(result.Trend.TokensPerExecutionSlope, 4),
                    toolCallsPerExecutionSlope = Math.Round(result.Trend.ToolCallsPerExecutionSlope, 4),
                    isStrugglingGeneralist = result.Trend.IsStrugglingGeneralist
                } : null,
                recommendations = result.Recommendations
            });
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine($"  Workflow: {result.WorkflowName}");
            Console.WriteLine($"  Jobs:    {result.JobCount}");
            Console.WriteLine($"  Tools:   ~{result.EstimatedToolCount} (estimated from manifest)");
            Console.WriteLine($"  Context: ~{result.EstimatedContextUtil:P0} utilization");

            if (result.Trend is not null)
            {
                Console.WriteLine($"  Trend:   tokens/exec {result.Trend.TokensPerExecutionSlope:+0.000;-0.000}  calls/exec {result.Trend.ToolCallsPerExecutionSlope:+0.000;-0.000}");
            }

            Console.WriteLine();
            Console.WriteLine("  Recommendations:");
            foreach (var rec in result.Recommendations)
                Console.WriteLine($"    • {rec}");
            Console.WriteLine();
        }
    }

    private static RemoteMetricsTracker ResolveTracker()
    {
        // TODO: Connect to persistent metrics source (OTEL backend query).
        return new RemoteMetricsTracker();
    }

    private sealed record AnalysisResult(
        string WorkflowName,
        int JobCount,
        int EstimatedToolCount,
        double EstimatedContextUtil,
        RemoteCellTrend? Trend,
        List<string> Recommendations);
}
