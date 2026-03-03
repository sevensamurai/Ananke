using Ananke.Design;
using Ananke.Orchestration;

namespace ExtendedFlowDemo.Examples;

/// <summary>
/// Fork/Join with FailFast (default).
/// plan ──► fork(search_web, search_db) ──► synthesize ──► End
/// </summary>
public static class ParallelResearchExample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("━━━ 1 · Parallel Research (Fork/Join FailFast) ━━━");
        Console.WriteLine();

        var workflow = new Workflow<State>("parallel-research")
            .Job("plan", async (state, ct) =>
            {
                Console.WriteLine("  [plan] Breaking query into sub-searches...");
                await Task.Delay(100, ct);
                return state with { Plan = "Search web + database" };
            })
            .Job("search_web", async (state, ct) =>
            {
                Console.WriteLine("  [search_web] Searching the web...");
                await Task.Delay(500, ct);
                return state with { WebResults = ["Web result A", "Web result B"] };
            })
            .Job("search_db", async (state, ct) =>
            {
                Console.WriteLine("  [search_db] Querying internal database...");
                await Task.Delay(300, ct);
                return state with { DbResults = ["DB record 1", "DB record 2", "DB record 3"] };
            })
            .Job("synthesize", async (state, ct) =>
            {
                Console.WriteLine("  [synthesize] Merging results...");
                await Task.Delay(100, ct);
                var combined = state.WebResults.Concat(state.DbResults).ToList();
                return state with { Summary = $"Found {combined.Count} results for '{state.Query}'" };
            })
            .Then("plan", Workflow.Fork("search_web", "search_db"))
            .Join(["search_web", "search_db"], "synthesize", branches =>
            {
                var web = branches.FirstOrDefault(b => b.WebResults.Count > 0);
                var db = branches.FirstOrDefault(b => b.DbResults.Count > 0);
                return new State
                {
                    Query = branches[0].Query,
                    Plan = branches[0].Plan,
                    WebResults = web?.WebResults ?? [],
                    DbResults = db?.DbResults ?? []
                };
            })
            .Then("synthesize", Workflow.End);

        var result = await workflow.RunAsync(new State { Query = "distributed state machines" });

        ConsoleLogger<State>.PrintResults(result, workflow.ToMermaid(), s => s.Summary);
    }

    record State
    {
        public string Query { get; init; } = "";
        public string Plan { get; init; } = "";
        public List<string> WebResults { get; init; } = [];
        public List<string> DbResults { get; init; } = [];
        public string Summary { get; init; } = "";
    }
}
