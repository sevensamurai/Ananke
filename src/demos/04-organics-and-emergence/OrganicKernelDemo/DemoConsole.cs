using Ananke.Orchestration.Workflows;
using Ananke.Organics.Division;
using Ananke.Organics.Sensing;

namespace OrganicKernelDemo;

/// <summary>Console formatting helpers. All demo output goes through here.</summary>
static class DemoConsole
{
    public static void Print(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    public static void PrintPhase(int number, string title)
    {
        Console.WriteLine();
        Print("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━", ConsoleColor.DarkGray);
        Print($"  Phase {number}: {title}", ConsoleColor.White);
        Print("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━", ConsoleColor.DarkGray);
        Console.WriteLine();
    }

    public static void PrintSubPhase(string title)
    {
        Console.WriteLine();
        Print($"  ── {title} ──", ConsoleColor.DarkCyan);
        Console.WriteLine();
    }

    public static void PrintSnapshot(ComplexitySnapshot snapshot)
    {
        Print($"  📊 Complexity: {snapshot.WorkflowName}", ConsoleColor.White);
        Print($"     Tools: {snapshot.ToolCount}  |  Jobs: {snapshot.JobCount}  |  " +
              $"Clusters: {snapshot.TagClusterCount}  |  Entropy: {snapshot.RoutingEntropy:F2}", ConsoleColor.DarkGray);
        Print($"     Resource span: {snapshot.ResourceSpan}  |  Context util: {snapshot.ContextUtilization:P0}  |  " +
              $"Avg latency: {snapshot.AvgLatencyMs:F0}ms", ConsoleColor.DarkGray);
    }

    public static void PrintYamlBlock(string yaml)
    {
        foreach (var line in yaml.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            var color = trimmed.TrimStart().StartsWith('#')
                ? ConsoleColor.DarkGray
                : ConsoleColor.Gray;
            Print($"  {trimmed}", color);
        }
        Console.WriteLine();
    }

    public static void PrintLandscape(ICapabilityMap landscape)
    {
        var sensed = landscape.DiscoverAll();
        Print($"  Capability map: {sensed.Count} workflow(s), capabilities: " +
              $"[{string.Join(", ", sensed.SelectMany(s => s.Capabilities).Distinct())}]", ConsoleColor.DarkGray);
    }

    public static string RouteByCapability(ICapabilityMap landscape, string request)
    {
        var alive = landscape.DiscoverAll();
        if (alive.Count == 0) return "no-workflow";

        string[] orderKeywords = ["order", "payment", "pay", "ship", "track", "discount", "return", "customer", "lookup"];
        var isOrderRequest = orderKeywords.Any(k => request.Contains(k, StringComparison.OrdinalIgnoreCase));

        var target = alive.FirstOrDefault(c =>
            isOrderRequest ? c.Domain == "orders" : c.Domain == "catalog");

        return target?.WorkflowName ?? alive[0].WorkflowName;
    }
}
