using Ananke.Design;
using Ananke.Orchestration;
using Ananke.Orchestration.Routing;

namespace ExtendedFlowDemo.Examples;

/// <summary>
/// Fork/Join with BestEffort — one branch fails, workflow continues.
/// start ──► fork(reliable, flaky) ──► report ──► End
/// </summary>
public static class BestEffortIngestExample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("━━━ 2 · BestEffort (partial failure) ━━━");
        Console.WriteLine();

        var workflow = new Workflow<State>("resilient-ingest")
            .Job("start", async (state, ct) =>
            {
                Console.WriteLine("  [start] Initiating parallel ingest...");
                await Task.Delay(50, ct);
                return state;
            })
            .Job("reliable", async (state, ct) =>
            {
                Console.WriteLine("  [reliable] Fetching from primary source...");
                await Task.Delay(200, ct);
                return state with { PrimaryData = "Primary data OK" };
            })
            .Job("flaky", async (state, ct) =>
            {
                Console.WriteLine("  [flaky] Fetching from unreliable source...");
                await Task.Delay(100, ct);
                throw new HttpRequestException("503 Service Unavailable");
            })
            .Job("report", async (state, ct) =>
            {
                Console.WriteLine("  [report] Generating report...");
                await Task.Delay(50, ct);
                var sources = new List<string>();
                if (state.PrimaryData is not null) sources.Add("primary");
                if (state.SecondaryData is not null) sources.Add("secondary");
                return state with { Report = $"Report from {sources.Count} source(s): {string.Join(", ", sources)}" };
            })
            .Then("start", Workflow.Fork(ForkMode.BestEffort, "reliable", "flaky"))
            .Join(["reliable", "flaky"], "report", branches =>
            {
                var primary = branches.FirstOrDefault(b => b.PrimaryData is not null);
                var secondary = branches.FirstOrDefault(b => b.SecondaryData is not null);
                return new State
                {
                    PrimaryData = primary?.PrimaryData,
                    SecondaryData = secondary?.SecondaryData
                };
            })
            .Then("report", Workflow.End);

        var result = await workflow.RunAsync(new State());

        ConsoleLogger<State>.PrintResults(result, workflow.ToMermaid(), s => s.Report ?? "");
    }

    record State
    {
        public string? PrimaryData { get; init; }
        public string? SecondaryData { get; init; }
        public string? Report { get; init; }
    }
}
