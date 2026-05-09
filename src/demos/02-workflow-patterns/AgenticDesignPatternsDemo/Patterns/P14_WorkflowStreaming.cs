using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Workflows;

namespace AgenticDesignPatternsDemo.Patterns;

internal static class P14_WorkflowStreaming
{
    internal static async Task RunAsync()
    {
        PatternRunner.PrintHeader("14. Workflow Streaming (orchestration events)");

        var workflow = new Workflow<StreamState>("event-demo")
            .Job("step-1", async (state, ct) =>
            {
                await Task.Delay(50, ct);
                return state with { Progress = "Step 1 done" };
            })
            .Job("step-2", async (state, ct) =>
            {
                await Task.Delay(50, ct);
                return state with { Progress = "Step 2 done" };
            })
            .Chain("step-1", "step-2", Workflow.End);

        await foreach (var evt in workflow.StreamAsync(new StreamState()))
        {
            switch (evt)
            {
                case JobStarted<StreamState> js:
                    Console.WriteLine($"  ▶ Job started:   {js.JobName}");
                    break;
                case JobCompleted<StreamState> jc:
                    Console.WriteLine($"  ✔ Job completed: {jc.JobName} ({jc.Duration.TotalMilliseconds:F0}ms)");
                    break;
                case WorkflowCompleted<StreamState> wc:
                    Console.WriteLine($"  ✅ Workflow completed! Success: {wc.Result.Success}");
                    break;
            }
        }
        Console.WriteLine();
    }
}

internal record StreamState
{
    public string Progress { get; init; } = "";
}
