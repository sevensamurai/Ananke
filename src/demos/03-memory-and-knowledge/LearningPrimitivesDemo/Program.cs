// ---------------------------------------------------------------------
//  LearningPrimitivesDemo — demonstrates Ananke Learning and Skills
//
//  Usage:
//    dotnet run -- --scenario skills            # SkillCatalog via cowsay
//    dotnet run -- --scenario routing           # Routing evolution via Qdrant UCB
//    dotnet run -- --scenario knowledge-graph   # Knowledge graph, multi-hop retrieval & PageRank
//    dotnet run                                 # runs skills scenario by default
// ---------------------------------------------------------------------

var scenario = ParseScenario(args);

Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine($"  Ananke — Learning Primitives Demo ({scenario})");
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine();

if (scenario == "routing")
    await LearningPrimitivesDemo.Routing.RoutingScenario.RunAsync(args);
else if (scenario == "knowledge-graph")
    await LearningPrimitivesDemo.Knowledge.KnowledgeGraphScenario.RunAsync();
else
    await LearningPrimitivesDemo.Skills.SkillsScenario.RunAsync(args);

static string ParseScenario(string[] args)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i].Equals("--scenario", StringComparison.OrdinalIgnoreCase))
            return args[i + 1].ToLowerInvariant();
    return "skills";
}
