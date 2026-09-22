using Ananke.Design;
using Ananke.Federation.Monitoring;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Platform.Commands;

/// <summary>
/// Handles <c>nnke-platform analyze &lt;file&gt; [--deployment-id &lt;id&gt;]</c> —
/// examines manifest structure and recommends division
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

        // --deployment-id and the runtime-trend correlation are withdrawn for 1.0.
        // RemoteMetricsTracker keeps samples in memory and ResolveTracker() built a fresh, empty one
        // on every invocation, so the correlation could never fire — it silently degraded to
        // "no trend data" regardless of what a deployment had actually done. Restoring it needs a
        // metrics store that survives a process; doing so is additive.
        var command = new Command("analyze", "Analyze a manifest's structural complexity.")
        {
            fileArg
        };

        command.SetAction(parseResult =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var json = parseResult.GetValue<bool>("--json");
            return Execute(file, json);
        });

        return command;
    }

    private static int Execute(FileInfo file, bool json)
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

        var analysis = AnalyzeManifest(manifest);
        Emit(analysis, json);

        return 0;
    }

    private static AnalysisResult AnalyzeManifest(WorkflowManifest manifest)
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

        if (recommendations.Count == 0)
            recommendations.Add("Workflow structure looks healthy. No division recommended at this time.");

        return new AnalysisResult(
            manifest.Name,
            jobCount,
            estimatedToolCount,
            estimatedContextUtil,
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

            Console.WriteLine();
            Console.WriteLine("  Recommendations:");
            foreach (var rec in result.Recommendations)
                Console.WriteLine($"    • {rec}");
            Console.WriteLine();
        }
    }

    private sealed record AnalysisResult(
        string WorkflowName,
        int JobCount,
        int EstimatedToolCount,
        double EstimatedContextUtil,
        List<string> Recommendations);
}
