using Ananke.Learning;
using Ananke.Learning.Offline;
using Ananke.Orchestration.Knowledge.Embeddings;
using LogEventsDemo;

// -------------------------------------------------------------------
//  LogEventsDemo � Empirical Memory from Simulated Operations Logs
//
//  A simulated distributed system produces structured log events.
//  Rule-based pattern detection finds cascading failures. The REPL
//  lets you investigate, recall past incidents, and confirm/reject
//  detected patterns. Offline learning discovers correlations you
//  haven't explored.
//
//  No LLM required. All detection is structural/tag-based.
//
//  Usage:
//    LogEventsDemo                 � run simulation + interactive REPL
//    LogEventsDemo --ticks 500     � generate 500 ticks of log data
//    LogEventsDemo --auto          � run simulation, detect, learn, print report
// -------------------------------------------------------------------

Console.OutputEncoding = System.Text.Encoding.UTF8;

// -- Parse arguments ----------------------------------------------
var ticks = 200;
var autoMode = false;

for (var i = 0; i < args.Length; i++)
{
    if (args[i].Equals("--ticks", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
    {
        if (int.TryParse(args[++i], out var n) && n > 0)
            ticks = n;
    }
    else if (args[i].Equals("--auto", StringComparison.OrdinalIgnoreCase))
    {
        autoMode = true;
    }
}

// -- Memory setup ------------------------------------------------
var embedder = new InMemoryEmbedder();
var predictionSource = new TagOverlapPredictionSource(neighborCount: 5);
var affectOptions = new AffectOptions
{
    // Log entries represent operational incidents � persist longer than game moves
    BaseDecayRate = 0.995f,
    DeletionThreshold = 0.03f,
    ReinforcementCooldownHours = 0.01f
};
var memory = new InMemoryEmpiricalMemory(
    embedder,
    dedupThreshold: 0.80f,
    affectOptions: affectOptions,
    predictionSource: predictionSource);

var simulationSource = new LogSimulationSource();
var learner = new OfflineLearner(
    memory, embedder,
    simulator: simulationSource,
    options: new OfflineLearnerOptions
    {
        ExplorationBatchSize = 8,
        CuriosityThreshold = 0.4f,
        MaxSimulationEpisodes = 15,
        SimulationEvidenceWeight = 0.4f
    });

// -- Phase 1: Seed knowledge base --------------------------------
Console.WriteLine("\n  ?? Seeding knowledge base with architectural heuristics...");
await KnowledgeSeeder.SeedAsync(memory);
var heuristics = await memory.BrowseAsync(0, 100, EmpiricalKind.Heuristic);
Console.WriteLine($"     Loaded {heuristics.Count} heuristic entries from wiki/architecture.");

// -- Phase 2: Run log simulation ---------------------------------
Console.WriteLine($"\n  ?? Running log simulation ({ticks} ticks)...");
var simulator = new LogSimulator();
await simulator.RunAsync(ticks);
Console.WriteLine($"     Generated {simulator.History.Count} log events.");
Console.WriteLine($"     Simulated time: {simulator.History[0].Timestamp:HH:mm:ss} � {simulator.CurrentTime:HH:mm:ss}");

// -- Phase 3: Pattern detection ----------------------------------
Console.WriteLine("\n  ?? Running rule-based pattern detection...");
var detector = new RuleBasedPatternDetector(memory);
var patternsDetected = await detector.DetectAsync(simulator.History);
Console.WriteLine($"     Detected {patternsDetected} patterns.");

var allPatterns = await memory.BrowseAsync(0, 1000, EmpiricalKind.Pattern);
Console.WriteLine($"     Memory: {allPatterns.Count} patterns (after dedup).");

// -- Phase 4: Initial offline learning cycle ---------------------
Console.WriteLine("\n  ?? Running initial offline learning cycle...");
var learnResult = await learner.LearnAsync();
Console.WriteLine($"     Explored: {learnResult.Explored}, Reinforced: {learnResult.Reinforced}, "
    + $"Contradicted: {learnResult.Contradicted}, Decayed: {learnResult.Decayed}");
if (learnResult.Discoveries.Count > 0)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("     ?? Initial discoveries:");
    foreach (var d in learnResult.Discoveries)
        Console.WriteLine($"        � {d}");
    Console.ResetColor();
}

if (autoMode)
{
    // -- Auto mode: print summary report and exit -----------------
    Console.WriteLine("\n  -----------------------------------------------------------");
    Console.WriteLine("  ?? Auto Analysis Report");
    Console.WriteLine("  -----------------------------------------------------------");

    // Run 3 more learning cycles
    for (var cycle = 2; cycle <= 4; cycle++)
    {
        var result = await learner.LearnAsync();
        Console.WriteLine($"  Cycle {cycle}: explored={result.Explored} reinforced={result.Reinforced} "
            + $"contradicted={result.Contradicted} decayed={result.Decayed} discoveries={result.Discoveries.Count}");
    }

    var patterns = await memory.BrowseAsync(0, 1000, EmpiricalKind.Pattern);
    var heur = await memory.BrowseAsync(0, 1000, EmpiricalKind.Heuristic);

    Console.WriteLine($"\n  Final memory: {patterns.Count} patterns, {heur.Count} heuristics");
    Console.WriteLine("\n  Top 10 patterns by confidence:");
    foreach (var p in patterns.OrderByDescending(e => e.Confidence).Take(10))
    {
        Console.WriteLine($"    [{p.Id}] conf={p.Confidence:F3} str={p.Strength:F3} obs={p.ObservationCount}");
        Console.WriteLine($"      {p.Description}");
        if (p.Condition is not null) Console.WriteLine($"      IF: {p.Condition}");
        if (p.Effect is not null) Console.WriteLine($"      THEN: {p.Effect}");
    }

    // Show error events summary
    var errorsByService = simulator.History
        .Where(e => e.Level >= LogLevel.Error)
        .GroupBy(e => e.Service)
        .Select(g => new { Service = g.Key, Count = g.Count() })
        .OrderByDescending(x => x.Count)
        .ToList();

    Console.WriteLine("\n  Error distribution:");
    foreach (var g in errorsByService)
        Console.WriteLine($"    {g.Service,-25} {g.Count} errors");

    return;
}

// -- Interactive REPL ---------------------------------------------
Console.WriteLine("\n  -----------------------------------------------------------");
Console.WriteLine("  ???  Starting interactive log explorer...");
Console.WriteLine("  -----------------------------------------------------------");

var explorer = new Explorer(simulator, memory, learner, detector);
await explorer.RunAsync();
