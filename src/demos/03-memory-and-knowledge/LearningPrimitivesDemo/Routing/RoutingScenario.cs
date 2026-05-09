using System.Text;
using Ananke.Abstractions.Agents;
using Ananke.Learning.Exploration;
using Ananke.Organics.Division;
using Ananke.Organics.Sensing;
using Ananke.Qdrant;
using Qdrant.Client;

namespace LearningPrimitivesDemo.Routing;

// ═══════════════════════════════════════════════════════════════════════
//  Routing scenario — Hybrid Routing (Option D)
//
//  Demonstrates post-division routing evolution:
//    Phase 1: Division emits routing artifact → Qdrant indexes tool
//             descriptions → prompts classified by vector similarity
//    Phase 2: RoutingAffinityTracker observes outcomes, refines routing
//             via UCB explore/exploit — neural pathway formation
//
//  Prerequisites:
//    docker run -p 6334:6334 qdrant/qdrant
//
//  No API keys required — all embeddings are deterministic fakes.
// ═══════════════════════════════════════════════════════════════════════

internal static class RoutingScenario
{
    internal static async Task RunAsync(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        Print("═══════════════════════════════════════════════════════════════", ConsoleColor.DarkCyan);
        Print("  🧬 Post-Division Routing Evolution Demo (Hybrid Routing, Option D)", ConsoleColor.Cyan);
        Print("  Phase 1: Qdrant vector routing  →  Phase 2: Adaptive UCB", ConsoleColor.DarkCyan);
        Print("═══════════════════════════════════════════════════════════════", ConsoleColor.DarkCyan);
        Console.WriteLine();

        var toolDescriptions = new Dictionary<string, string>
        {
            ["search_catalog"] = "Search books by title, author, or genre in the catalog",
            ["get_book_details"] = "Get full details for a book by ISBN including price and rating",
            ["check_inventory"] = "Check stock levels and warehouse availability for a book",
            ["get_recommendations"] = "Get personalized book recommendations by genre or reading history",
            ["process_payment"] = "Process a customer payment transaction with amount and method",
            ["create_order"] = "Create a new customer order with line items",
            ["track_shipment"] = "Track shipment status and delivery ETA for an order",
            ["apply_discount"] = "Apply a promotional discount code to an order",
            ["manage_returns"] = "Process a product return with RMA generation",
            ["customer_lookup"] = "Look up customer profile by email or customer ID"
        };

        var children = new List<ChildSpec>
        {
            new()
            {
                Name = "bookstore-catalog",
                Domain = "catalog",
                Tools = ["search_catalog", "get_book_details", "check_inventory", "get_recommendations"],
                Jobs = ["handle-catalog-request", "respond"]
            },
            new()
            {
                Name = "bookstore-orders",
                Domain = "orders",
                Tools = ["process_payment", "create_order", "track_shipment", "apply_discount", "manage_returns", "customer_lookup"],
                Jobs = ["handle-order-request", "respond"]
            }
        };

        PrintPhase(1, "Qdrant Vector Routing (Division Artifact → Semantic Classification)");
        Print("  Simulated division: bookstore-general → catalog + orders", ConsoleColor.Yellow);
        foreach (var child in children)
            Print($"    {child.Name} [{child.Domain}]: {string.Join(", ", child.Tools)}", ConsoleColor.DarkYellow);
        Console.WriteLine();

        Print("  Connecting to Qdrant (localhost:6334)...", ConsoleColor.Gray);
        var qdrantClient = new QdrantClient("localhost", 6334);
        var embedder = new FakeEmbeddingModel();

        var qdrantRouter = new QdrantDomainRouter(
            qdrantClient, embedder,
            collectionName: "routing_demo",
            vectorSize: 16);

        Print("  Indexing child cells in Qdrant...", ConsoleColor.Gray);
        await qdrantRouter.IndexAsync(children, toolDescriptions);
        Print("  ✅ Indexed 2 cells with tool-description embeddings", ConsoleColor.Green);
        Console.WriteLine();

        PrintSubPhase("Phase 1: Vector-based routing (no learning yet)");

        string[] testPrompts =
        [
            "Can you search for books about machine learning?",
            "I want to create an order for ISBN 978-0-201-63361-0",
            "Process payment of $34.99 for my purchase",
            "What sci-fi books do you recommend?",
            "Track my order #ORD-4521",
            "Check if 'Design Patterns' is in stock",
            "Apply discount code SUMMER25 to my cart",
            "Look up customer alice@bookstore.com",
            "Get details for ISBN 978-0-7432-7356-5",
            "I need to return order #ORD-3190",
            "Recommend me some fantasy novels",
            "How much does shipping cost for bulk orders?",
        ];

        var phase1Results = new List<(string Prompt, string Cell, bool Correct)>();

        foreach (var prompt in testPrompts)
        {
            var cell = await qdrantRouter.RouteAsync(prompt);
            var expected = IsOrderPrompt(prompt) ? "bookstore-orders" : "bookstore-catalog";
            var correct = cell == expected;
            phase1Results.Add((prompt, cell, correct));

            var icon = correct ? "✅" : "❌";
            var color = correct ? ConsoleColor.Green : ConsoleColor.Red;
            Print($"  {icon} → {cell,-22} \"{prompt[..Math.Min(55, prompt.Length)]}\"", color);
        }

        var phase1Accuracy = phase1Results.Count(r => r.Correct) / (float)phase1Results.Count;
        Console.WriteLine();
        Print($"  Phase 1 accuracy: {phase1Accuracy:P0} ({phase1Results.Count(r => r.Correct)}/{phase1Results.Count})",
            phase1Accuracy >= 0.8f ? ConsoleColor.Green : ConsoleColor.Yellow);

        PrintPhase(2, "Adaptive Discovery (UCB Explore/Exploit → Neural Pathway Formation)");

        var tracker = new RoutingAffinityTracker(
            qdrantRouter,
            new UcbExplorationStrategy(new ExplorationOptions
            {
                ExplorationCoefficient = 1.4f,
                UseVarianceBonus = true,
                VarianceBonusWeight = 0.5f
            }));

        await tracker.IndexAsync(children, toolDescriptions);
        Print("  RoutingAffinityTracker wrapping QdrantDomainRouter", ConsoleColor.Gray);
        Print("  UCB exploration: c=1.4, variance bonus=0.5", ConsoleColor.DarkGray);
        Console.WriteLine();

        string[] trainingPrompts =
        [
            "Search for Python programming books",
            "What's in stock for data science?",
            "Recommend me some history books",
            "Get book details for ISBN 978-0-13-468599-1",
            "Do you have any new fantasy releases?",
            "Check inventory for the latest bestsellers",
            "Process my payment of $52.00",
            "Create an order for three textbooks",
            "Where is my shipment? Order #ORD-8877",
            "Apply my loyalty discount to the order",
            "I want to return a damaged book",
            "Look up my account by email john@example.com",
            "How much does this book cost with shipping?",
            "Can I get a refund for the wrong edition?",
            "What's the status of my book reservation?",
        ];

        Print("  Training: routing 15 prompts with outcome feedback...", ConsoleColor.White);
        Console.WriteLine();

        var rounds = 3;
        for (var round = 1; round <= rounds; round++)
        {
            PrintSubPhase($"Round {round}/{rounds}");

            foreach (var prompt in trainingPrompts)
            {
                var cell = await tracker.RouteAsync(prompt);
                var expected = IsOrderPrompt(prompt) ? "bookstore-orders" : "bookstore-catalog";
                var correct = cell == expected;
                var reward = correct ? 1.0f : -0.5f;
                tracker.RecordOutcome(cell, reward);

                var icon = correct ? "✓" : "✗";
                var color = correct ? ConsoleColor.DarkGreen : ConsoleColor.DarkRed;
                Print($"    {icon} {cell,-22} reward={reward,5:F1}  \"{prompt[..Math.Min(45, prompt.Length)]}\"", color);
            }

            Console.WriteLine();
            var affinities = tracker.GetAffinities();
            Print("  Affinity scores:", ConsoleColor.Cyan);
            foreach (var (name, (selections, mean, variance)) in affinities)
                Print($"    {name,-22} selections={selections,3}  mean={mean,6:F3}  var={variance,6:F3}", ConsoleColor.Gray);
        }

        Console.WriteLine();
        PrintPhase(3, "Final Evaluation — Phase 2 Routing After Learning");

        var phase2Results = new List<(string Prompt, string Cell, bool Correct)>();

        foreach (var prompt in testPrompts)
        {
            var cell = await tracker.RouteAsync(prompt);
            var expected = IsOrderPrompt(prompt) ? "bookstore-orders" : "bookstore-catalog";
            var correct = cell == expected;
            phase2Results.Add((prompt, cell, correct));

            var icon = correct ? "✅" : "❌";
            var color = correct ? ConsoleColor.Green : ConsoleColor.Red;
            Print($"  {icon} → {cell,-22} \"{prompt[..Math.Min(55, prompt.Length)]}\"", color);
        }

        var phase2Accuracy = phase2Results.Count(r => r.Correct) / (float)phase2Results.Count;
        Console.WriteLine();

        Print("═══════════════════════════════════════════════════════════════", ConsoleColor.DarkCyan);
        Print("  📊 Results", ConsoleColor.Cyan);
        Print("═══════════════════════════════════════════════════════════════", ConsoleColor.DarkCyan);
        Print($"  Phase 1 (Qdrant only):      {phase1Accuracy:P0}", phase1Accuracy >= 0.8f ? ConsoleColor.Green : ConsoleColor.Yellow);
        Print($"  Phase 2 (Qdrant + UCB):     {phase2Accuracy:P0}", phase2Accuracy >= 0.8f ? ConsoleColor.Green : ConsoleColor.Yellow);

        var affinitiesFinal = tracker.GetAffinities();
        Console.WriteLine();
        Print("  Final affinity scores:", ConsoleColor.Cyan);
        foreach (var (name, (selections, mean, variance)) in affinitiesFinal)
            Print($"    {name,-22} selections={selections,3}  mean={mean,6:F3}  var={variance,6:F3}",
                mean > 0.5f ? ConsoleColor.Green : mean > 0 ? ConsoleColor.Yellow : ConsoleColor.Red);

        Console.WriteLine();
        Print("  🧬 Hybrid Routing (Option D) demonstrated:", ConsoleColor.White);
        Print("     Phase 1: Division artifact → Qdrant semantic index → vector routing", ConsoleColor.DarkGray);
        Print("     Phase 2: UCB explore/exploit → affinity scores converge → pathways form", ConsoleColor.DarkGray);
        Print("     No LLM needed — fake embeddings + outcome feedback drive learning", ConsoleColor.DarkGray);
        Console.WriteLine();
    }

    private static bool IsOrderPrompt(string prompt)
    {
        string[] orderKeywords =
            ["order", "payment", "pay", "ship", "track", "discount", "return",
             "customer", "lookup", "refund", "account", "cart", "loyalty"];
        return orderKeywords.Any(k => prompt.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static void Print(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    private static void PrintPhase(int number, string title)
    {
        Console.WriteLine();
        Print("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━", ConsoleColor.DarkGray);
        Print($"  Phase {number}: {title}", ConsoleColor.White);
        Print("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━", ConsoleColor.DarkGray);
        Console.WriteLine();
    }

    private static void PrintSubPhase(string title)
    {
        Console.WriteLine();
        Print($"  ── {title} ──", ConsoleColor.DarkCyan);
        Console.WriteLine();
    }
}
