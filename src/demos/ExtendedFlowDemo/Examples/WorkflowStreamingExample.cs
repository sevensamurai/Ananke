using Ananke.Orchestration;
using Ananke.Orchestration.Streaming;

namespace ExtendedFlowDemo.Examples;

/// <summary>
/// Workflow-Level Streaming.
/// plan ──► research ──► write ──► End
///
/// Demonstrates consuming orchestration progress events via StreamAsync.
/// </summary>
public static class WorkflowStreamingExample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("━━━ 7 · Workflow-Level Streaming ━━━");
        Console.WriteLine();

        var workflow = new Workflow<State>("streaming-demo")
            .Job("plan", async (state, ct) =>
            {
                await Task.Delay(100, ct);
                return state with { Plan = "Research then write" };
            })
            .Job("research", async (state, ct) =>
            {
                await Task.Delay(300, ct);
                return state with { Notes = "Found 5 relevant sources" };
            })
            .Job("write", async (state, ct) =>
            {
                await Task.Delay(200, ct);
                return state with { Output = $"Article based on: {state.Notes}" };
            })
            .Chain("plan", "research", "write")
            .Then("write", Workflow.End);

        await foreach (var evt in workflow.StreamAsync(new State()))
        {
            switch (evt)
            {
                case JobStarted<State> js:
                    Console.WriteLine($"  ▶ {js.JobName} starting");
                    break;
                case JobCompleted<State> jc:
                    Console.WriteLine($"  ✓ {jc.JobName} completed ({jc.Duration.TotalMilliseconds:F0}ms)");
                    break;
                case StateUpdated<State> su:
                    Console.WriteLine($"    state updated — plan=\"{su.State.Plan}\", notes=\"{su.State.Notes}\"");
                    break;
                case WorkflowCompleted<State> wc:
                    Console.WriteLine($"  ✅ Done: {wc.Result.FinalState.Output}");
                    break;
                case WorkflowFaulted<State> wf:
                    Console.WriteLine($"  ❌ Faulted: {wf.Exception.Message}");
                    break;
            }
        }

        Console.WriteLine();
    }

    record State
    {
        public string Plan { get; init; } = "";
        public string Notes { get; init; } = "";
        public string Output { get; init; } = "";
    }
}
