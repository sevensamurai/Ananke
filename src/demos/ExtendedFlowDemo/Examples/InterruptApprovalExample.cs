using Ananke.Design;
using Ananke.Orchestration;
using Ananke.Orchestration.Checkpointing;

namespace ExtendedFlowDemo.Examples;

/// <summary>
/// Interrupt / Human-in-the-Loop.
/// draft ──► review ──► [INTERRUPT] ──► publish ──► End
///
/// Execution pauses before "publish" so a human can approve.
/// The demo simulates the human injecting approval into state,
/// then resumes from the checkpoint.
/// </summary>
public static class InterruptApprovalExample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("━━━ 5 · Interrupt / Human-in-the-Loop ━━━");
        Console.WriteLine();

        var checkpointStore = new InMemoryCheckpointStore();

        var workflow = new Workflow<State>("content-approval")
            .Job("draft", async (state, ct) =>
            {
                Console.WriteLine("  [draft] Writing initial draft...");
                await Task.Delay(100, ct);
                return state with { Draft = "Here is the article draft about Ananke." };
            })
            .Job("review", async (state, ct) =>
            {
                Console.WriteLine("  [review] Auto-reviewing draft...");
                await Task.Delay(100, ct);
                return state with { ReviewNotes = "Looks good — pending human approval." };
            })
            .Job("publish", async (state, ct) =>
            {
                Console.WriteLine("  [publish] Publishing approved content...");
                await Task.Delay(100, ct);
                return state with { Published = true };
            })
            .Chain("draft", "review", "publish")
            .Then("publish", Workflow.End)
            .InterruptBefore("publish")
            .UseCheckpointing(checkpointStore);

        // ── First run: executes draft → review → pauses before publish ──
        var execution = await workflow.RunAsync(new State());

        Console.WriteLine("  → paused before 'publish' — awaiting human approval");
        ConsoleLogger<State>.PrintResults(execution, workflow.ToMermaid(),
            s => $"Draft ready | Review: {s.ReviewNotes} | Approved: {s.Approved}");

        // ── Simulate human approval ──
        Console.WriteLine("  ... human reviews and approves ...");
        Console.WriteLine();

        // ── Resume with human input injected into state ──
        var resumed = await workflow.ResumeAsync(
            execution.Id,
            state => state with { Approved = true });

        ConsoleLogger<State>.PrintResults(resumed, workflow.ToMermaid(),
            s => $"Published: {s.Published} | Approved: {s.Approved}");
    }

    record State
    {
        public string Draft { get; init; } = "";
        public string ReviewNotes { get; init; } = "";
        public bool Approved { get; init; }
        public bool Published { get; init; }
    }
}
