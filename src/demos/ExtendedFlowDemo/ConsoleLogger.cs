using Ananke.Orchestration;

namespace ExtendedFlowDemo;

internal static class ConsoleLogger<T>
{
    public static void PrintResults(WorkflowExecution<T> result, string diagram, Func<T, string>? summarize = null)
    {
        Console.WriteLine();
        Console.WriteLine($"  Status:  {result.Status}");
        if (summarize is not null)
            Console.WriteLine($"  Summary: {summarize(result.State)}");
        Console.WriteLine($"  Jobs:    {result.History.Count} executed");
        foreach (var job in result.History)
            Console.WriteLine($"           • {job.JobName} ({job.Duration.TotalMilliseconds:F0}ms, {(job.Success ? "✓" : "✗")})");
        Console.WriteLine();
        Console.WriteLine($"  Workflow Diagram:");
        Console.WriteLine();
        Console.WriteLine(diagram);
        Console.WriteLine();
    }
}
