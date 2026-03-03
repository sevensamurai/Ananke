using Ananke.Design;
using Ananke.Orchestration;

namespace ExtendedFlowDemo.Examples;

/// <summary>
/// Fork/Join with multi-step branches.
/// plan ──► fork(fetch_a, fetch_b)
///          fetch_a ──► transform_a ──┐
///          fetch_b ──► transform_b ──┤
///                                    └──► combine ──► End
/// </summary>
public static class MultiStepBranchesExample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("━━━ 3 · Multi-step branches ━━━");
        Console.WriteLine();

        var workflow = new Workflow<State>("etl-pipeline")
            .Job("plan", async (state, ct) =>
            {
                Console.WriteLine("  [plan] Planning ETL pipeline...");
                await Task.Delay(50, ct);
                return state with { Step = "planned" };
            })
            .Job("fetch_a", async (state, ct) =>
            {
                Console.WriteLine("  [fetch_a] Fetching dataset A...");
                await Task.Delay(300, ct);
                return state with { RawA = "raw-data-A" };
            })
            .Job("transform_a", async (state, ct) =>
            {
                Console.WriteLine("  [transform_a] Transforming dataset A...");
                await Task.Delay(200, ct);
                return state with { TransformedA = $"cleaned({state.RawA})" };
            })
            .Job("fetch_b", async (state, ct) =>
            {
                Console.WriteLine("  [fetch_b] Fetching dataset B...");
                await Task.Delay(200, ct);
                return state with { RawB = "raw-data-B" };
            })
            .Job("transform_b", async (state, ct) =>
            {
                Console.WriteLine("  [transform_b] Transforming dataset B...");
                await Task.Delay(150, ct);
                return state with { TransformedB = $"cleaned({state.RawB})" };
            })
            .Job("combine", async (state, ct) =>
            {
                Console.WriteLine("  [combine] Merging transformed datasets...");
                await Task.Delay(50, ct);
                return state with { Output = $"{state.TransformedA} + {state.TransformedB}" };
            })
            .Then("plan", Workflow.Fork("fetch_a", "fetch_b"))
            .Then("fetch_a", "transform_a")
            .Then("fetch_b", "transform_b")
            .Join(["transform_a", "transform_b"], "combine", branches =>
            {
                var a = branches.FirstOrDefault(b => b.TransformedA is not null);
                var b = branches.FirstOrDefault(b2 => b2.TransformedB is not null);
                return new State
                {
                    Step = "joined",
                    TransformedA = a?.TransformedA ?? "",
                    TransformedB = b?.TransformedB ?? ""
                };
            })
            .Then("combine", Workflow.End);

        var result = await workflow.RunAsync(new State());

        ConsoleLogger<State>.PrintResults(result, workflow.ToMermaid(), s => s.Output ?? "");
    }

    record State
    {
        public string Step { get; init; } = "";
        public string? RawA { get; init; }
        public string? RawB { get; init; }
        public string? TransformedA { get; init; }
        public string? TransformedB { get; init; }
        public string? Output { get; init; }
    }
}
