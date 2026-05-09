using AgenticDesignPatternsDemo.Patterns;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// -------------------------------------------------------------------
//  Ananke — Agentic Design Patterns Demo
//
//  Each file under Patterns/ contains one numbered pattern.
//  Run all:           dotnet run
//  Run a subset:      dotnet run -- --only 3 6 13
//  No API keys required — all models are simulated.
// -------------------------------------------------------------------

var only = ParseOnly(args);

var all = new (int Number, string Label, Func<Task> Run)[]
{
    (1,  "Single Agent",             P01_SingleAgent.RunAsync),
    (2,  "Sequential Chain",         P02_SequentialChain.RunAsync),
    (3,  "Parallel Fork/Join",       P03_ParallelForkJoin.RunAsync),
    (4,  "Router / Coordinator",     P04_RouterCoordinator.RunAsync),
    (5,  "Loop Primitive",           P05_LoopPrimitive.RunAsync),
    (6,  "Review & Critique",        P06_ReviewCritique.RunAsync),
    (7,  "Iterative Refinement",     P07_IterativeRefinement.RunAsync),
    (8,  "Human-in-the-Loop",        P08_HumanInTheLoop.RunAsync),
    (9,  "SubFlow Composition",      P09_SubFlowComposition.RunAsync),
    (10, "Agent Middleware",          P10_AgentMiddleware.RunAsync),
    (11, "Context Strategy",         P11_ContextStrategy.RunAsync),
    (12, "Budget Tracking",          P12_BudgetTracking.RunAsync),
    (13, "Streaming Chat",           P13_StreamingChat.RunAsync),
    (14, "Workflow Streaming",       P14_WorkflowStreaming.RunAsync),
};

Console.WriteLine("-----------------------------------------------------------");
Console.WriteLine("  Ananke — Agentic Design Patterns Demo");
Console.WriteLine("-----------------------------------------------------------");
Console.WriteLine();

foreach (var (number, label, run) in all)
{
    if (only.Count > 0 && !only.Contains(number))
        continue;
    await run();
}

Console.WriteLine();
Console.WriteLine("-----------------------------------------------------------");
Console.WriteLine("  All demos complete!");
Console.WriteLine("-----------------------------------------------------------");

static HashSet<int> ParseOnly(string[] args)
{
    var result = new HashSet<int>();
    var i = 0;
    while (i < args.Length)
    {
        if (args[i].Equals("--only", StringComparison.OrdinalIgnoreCase))
        {
            i++;
            while (i < args.Length && int.TryParse(args[i], out var n))
            {
                result.Add(n);
                i++;
            }
        }
        else
        {
            i++;
        }
    }
    return result;
}
