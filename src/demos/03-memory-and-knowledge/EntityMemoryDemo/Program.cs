using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Learning.EntityMemory;
using Ananke.Learning.Episodes;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Knowledge;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Orchestration.Memory;

// -------------------------------------------------------------------
//  EntityMemoryDemo — Per-Entity Long-Term Memory (Shopping Companion)
//
//  A furniture shop with two customers. A workflow handles each visit:
//
//    load_profile → browse → recommend → learn
//
//  Visit 1: Customer-8472 browses for the first time.
//           No profile exists → cold-start recommendations.
//           The "learn" step observes browsing signals and builds a profile.
//
//  Visit 2: Customer-9999 browses. Different preferences → separate profile.
//
//  Visit 3: Customer-8472 returns. Profile is loaded and used to make
//           personalized recommendations. New preferences are learned.
//
//  All memory lives in shared stores. Entity isolation is handled by
//  EntityMemoryProvider — no separate databases per customer.
//
//  No LLM required. Uses InMemoryEmbedder for deterministic vectors.
// -------------------------------------------------------------------

Console.OutputEncoding = System.Text.Encoding.UTF8;

// ── Shared infrastructure ────────────────────────────────────────
var embedder = new InMemoryEmbedder();
var conversations = new InMemoryConversationMemory();
var empirical = new InMemoryEmpiricalMemory(embedder);
var knowledge = new InMemoryKnowledgeStore(embedder);
var episodes = new InMemoryEpisodeStore();

var provider = new EntityMemoryProvider(conversations, empirical, knowledge, episodes);

// ── Simulated product catalog ────────────────────────────────────
var catalog = new Dictionary<string, string[]>
{
    ["minimalist"] = ["Walnut slab desk", "Floating shelf set", "Linen sofa — oatmeal"],
    ["baroque"]    = ["Gilt-frame mirror", "Carved mahogany armchair", "Velvet chaise lounge"],
    ["art-deco"]   = ["Brass arc floor lamp", "Geometric side table", "Emerald velvet chair"],
    ["sustainable"] = ["Reclaimed oak dining table", "Bamboo bookshelf", "Cork stool set"],
    ["general"]    = ["Cotton throw blanket", "Ceramic table lamp", "Woven rug — neutral"]
};

// ── Build the shopping workflow ──────────────────────────────────
//
//   load_profile → browse → recommend → learn → END
//

var workflow = new Workflow<ShopState>("shopping-visit")

    // 1. Load the customer's learned profile from entity memory
    .Job("load_profile", async (state, ct) =>
    {
        var memory = provider.GetOrCreate(state.CustomerId);
        var profile = await memory.Empirical.RecallAsync(
            "furniture style preferences",
            new RecallOptions { TopK = 5, IncludeGlobal = true }, ct);

        var knowledgeDocs = await memory.Knowledge.SearchAsync(
            "customer style profile", ct: ct);

        return state with
        {
            IsReturning = profile.Count > 0,
            RecalledPatterns = profile.Select(m => m.Entry.Description.ToString()!).ToList(),
            KnowledgeSummary = knowledgeDocs.Count > 0
                ? knowledgeDocs[0].Text
                : null
        };
    })

    // 2. Simulate browsing (items vary by customer taste)
    .Job("browse", async (state, ct) =>
    {
        await Task.CompletedTask;
        return state with { BrowsedItems = state.BrowseSimulation };
    })

    // 3. Generate recommendations based on profile + browsing
    .Job("recommend", async (state, ct) =>
    {
        await Task.CompletedTask;

        List<string> recommendations;
        if (state.IsReturning && state.RecalledPatterns.Count > 0)
        {
            // Personalized: use learned patterns to select relevant items
            var styles = new List<string>();
            foreach (var pattern in state.RecalledPatterns)
            {
                if (pattern.Contains("minimalist", StringComparison.OrdinalIgnoreCase))
                    styles.Add("minimalist");
                if (pattern.Contains("sustainable", StringComparison.OrdinalIgnoreCase))
                    styles.Add("sustainable");
                if (pattern.Contains("baroque", StringComparison.OrdinalIgnoreCase))
                    styles.Add("baroque");
                if (pattern.Contains("art deco", StringComparison.OrdinalIgnoreCase))
                    styles.Add("art-deco");
            }

            recommendations = styles
                .Distinct()
                .Where(catalog.ContainsKey)
                .SelectMany(s => catalog[s].Take(2))
                .Distinct()
                .ToList();

            if (recommendations.Count == 0)
                recommendations = [.. catalog["general"]];
        }
        else
        {
            // Cold start: generic bestsellers
            recommendations = [.. catalog["general"]];
        }

        return state with
        {
            Recommendations = recommendations,
            RecommendationType = state.IsReturning ? "personalized" : "cold-start"
        };
    })

    // 4. Learn from this session — observe browsing and commit patterns
    .Job("learn", async (state, ct) =>
    {
        var memory = provider.GetOrCreate(state.CustomerId);

        foreach (var learning in state.Learnings)
        {
            var shortId = Guid.NewGuid().ToString("N")[..8];
            await memory.Empirical.CommitAsync(new EmpiricalEntry
            {
                Id = $"{learning.Kind.ToString().ToLowerInvariant()[0]}-{state.CustomerId}-{shortId}",
                Kind = learning.Kind,
                Tags = learning.Tags,
                Source = "session-analysis",
                Description = SemanticDescription.FromText(learning.Description),
                Condition = learning.Condition,
                Effect = learning.Effect,
                Situation = learning.Situation,
                PreferredApproach = learning.PreferredApproach,
                Confidence = learning.Confidence,
                ObservationCount = 1,
                Evidence = [$"session:{state.SessionId}"],
                FirstObserved = DateTimeOffset.UtcNow,
                LastObserved = DateTimeOffset.UtcNow
            }, ct);
        }

        if (state.StyleProfile is not null)
        {
            await memory.Knowledge.UpsertAsync([new KnowledgeDocument
            {
                Id = $"kb-{state.CustomerId}-style",
                Text = state.StyleProfile
            }], ct);
        }

        return state with { LearnedCount = state.Learnings.Count };
    })

    .Chain("load_profile", "browse", "recommend", "learn")
    .Then("learn", Workflow.End);

// ═════════════════════════════════════════════════════════════════
//  Visit 1: Customer-8472's first visit
// ═════════════════════════════════════════════════════════════════

PrintPhase("1", "Customer-8472 visits for the first time");

var visit1 = await workflow.RunAsync(new ShopState
{
    CustomerId = "customer-8472",
    SessionId = "sess-001",
    BrowseSimulation = ["Walnut slab desk", "Floating shelf set", "Bamboo bookshelf"],
    Learnings =
    [
        new LearningInput
        {
            Kind = EmpiricalKind.Pattern,
            Tags = ["category:furniture", "signal:dwell-time"],
            Description = "Customer prefers minimalist furniture; average dwell time on minimalist items 12s vs 2s for ornate",
            Condition = "Browsing furniture category",
            Effect = "Strong preference signal for minimalist style",
            Confidence = 0.6f
        },
        new LearningInput
        {
            Kind = EmpiricalKind.Heuristic,
            Tags = ["category:furniture", "sustainability"],
            Description = "Prioritize sustainable materials when recommending furniture to this customer",
            Situation = "Furniture recommendations",
            PreferredApproach = "Lead with sustainability certifications and eco-friendly materials",
            Confidence = 0.55f
        }
    ],
    StyleProfile = "Customer-8472 style profile: minimalist aesthetic, sustainable materials, walnut wood preferred, avoids ornate and baroque designs"
});

PrintVisitResult(visit1.State);

// ═════════════════════════════════════════════════════════════════
//  Visit 2: Customer-9999 visits (different preferences)
// ═════════════════════════════════════════════════════════════════

PrintPhase("2", "Customer-9999 visits (different preferences)");

var visit2 = await workflow.RunAsync(new ShopState
{
    CustomerId = "customer-9999",
    SessionId = "sess-099",
    BrowseSimulation = ["Gilt-frame mirror", "Carved mahogany armchair", "Velvet chaise lounge"],
    Learnings =
    [
        new LearningInput
        {
            Kind = EmpiricalKind.Pattern,
            Tags = ["category:furniture", "signal:dwell-time"],
            Description = "Customer prefers ornate baroque furniture; spends 15s on gilt-frame mirrors and carved mahogany pieces",
            Condition = "Browsing furniture category",
            Effect = "Strong preference signal for baroque/ornate style",
            Confidence = 0.65f
        }
    ],
    StyleProfile = "Customer-9999 style profile: ornate baroque aesthetic, carved mahogany, gilt frames, classical European influence"
});

PrintVisitResult(visit2.State);

// ═════════════════════════════════════════════════════════════════
//  Visit 3: Customer-8472 returns — profile loaded, personalized!
// ═════════════════════════════════════════════════════════════════

PrintPhase("3", "Customer-8472 returns — profile loaded and used for recommendations");

var visit3 = await workflow.RunAsync(new ShopState
{
    CustomerId = "customer-8472",
    SessionId = "sess-042",
    BrowseSimulation = ["Brass arc floor lamp", "Geometric side table", "Reclaimed oak dining table"],
    Learnings =
    [
        new LearningInput
        {
            Kind = EmpiricalKind.Heuristic,
            Tags = ["category:furniture", "style:art-deco"],
            Description = "Customer also appreciates art deco style, not just minimalist",
            Situation = "Furniture recommendations",
            PreferredApproach = "Include select art deco pieces alongside minimalist",
            Confidence = 0.7f
        }
    ]
});

PrintVisitResult(visit3.State);

// ═════════════════════════════════════════════════════════════════
//  Customer-8472 learned profile
// ═════════════════════════════════════════════════════════════════

PrintPhase("4", "Everything we learned about Customer-8472");

var mem8472 = provider.GetOrCreate("customer-8472");
var allPatterns = await mem8472.Empirical.BrowseAsync(0, 100, EmpiricalKind.Pattern);
var allHeuristics = await mem8472.Empirical.BrowseAsync(0, 100, EmpiricalKind.Heuristic);
var allSkills = await mem8472.Empirical.BrowseAsync(0, 100, EmpiricalKind.Skill);
var allKnowledge = await mem8472.Knowledge.SearchAsync("customer preferences style",
    new SearchOptions { TopK = 20, ScoreThreshold = 0f });

Console.ForegroundColor = ConsoleColor.White;
Console.WriteLine("     ┌─────────────────────────────────────────────────────────┐");
Console.WriteLine("     │  CUSTOMER-8472 — LEARNED PROFILE                        │");
Console.WriteLine("     └─────────────────────────────────────────────────────────┘");
Console.ResetColor();

if (allPatterns.Count > 0)
{
    Console.WriteLine();
    PrintSectionHeader("Patterns", "observed behavioral signals");
    foreach (var p in allPatterns)
        PrintLearningEntry(p);
}

if (allHeuristics.Count > 0)
{
    Console.WriteLine();
    PrintSectionHeader("Heuristics", "rules of thumb for serving this customer");
    foreach (var h in allHeuristics)
        PrintLearningEntry(h);
}

if (allSkills.Count > 0)
{
    Console.WriteLine();
    PrintSectionHeader("Skills", "learned procedures for this customer");
    foreach (var s in allSkills)
        PrintLearningEntry(s);
}

if (allKnowledge.Count > 0)
{
    Console.WriteLine();
    PrintSectionHeader("Knowledge", "semantic documents about this customer");
    foreach (var doc in allKnowledge)
    {
        Console.Write("       📄 ");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(doc.Text);
        Console.ResetColor();
    }
}

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine("     ──────────────────────────────────────────────────────────");
Console.Write("     Totals: ");
Console.ResetColor();
Console.WriteLine($"{allPatterns.Count} pattern(s), {allHeuristics.Count} heuristic(s), {allSkills.Count} skill(s), {allKnowledge.Count} doc(s)");

// ── Summary ─────────────────────────────────────────────────────
PrintPhase("✓", "Demo complete");

var count9999 = (await provider.GetOrCreate("customer-9999").Empirical.BrowseAsync(0, 100)).Count;
Console.WriteLine($"  Shared empirical store: {empirical.Count} total entries");
Console.WriteLine($"  Customer-8472: {allPatterns.Count + allHeuristics.Count + allSkills.Count} empirical + {allKnowledge.Count} knowledge");
Console.WriteLine($"  Customer-9999: {count9999} empirical");
Console.WriteLine();
Console.WriteLine("  Key takeaways:");
Console.WriteLine("  • The workflow is the same for every customer — entity memory provides the personalization");
Console.WriteLine("  • First visit: cold-start (no profile) → generic recommendations");
Console.WriteLine("  • Return visit: profile loaded → personalized recommendations");
Console.WriteLine("  • Entity-scoped entries are isolated — no cross-customer leakage");
Console.WriteLine("  • All customers share a single store — no physical partitioning needed");

// ═════════════════════════════════════════════════════════════════
//  State & helpers
// ═════════════════════════════════════════════════════════════════

static void PrintPhase(string num, string title)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"  ── {num}. {title}");
    Console.ResetColor();
}

static void PrintVisitResult(ShopState state)
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write("     Customer: ");
    Console.ResetColor();
    Console.WriteLine(state.CustomerId);

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write("     Returning: ");
    Console.ResetColor();
    Console.WriteLine(state.IsReturning ? "yes — profile loaded" : "no — first visit");

    if (state.IsReturning && state.RecalledPatterns.Count > 0)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("     Recalled patterns:");
        Console.ResetColor();
        foreach (var p in state.RecalledPatterns)
            Console.WriteLine($"       • {p}");
    }

    if (state.KnowledgeSummary is not null)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("     Knowledge: ");
        Console.ResetColor();
        Console.WriteLine(state.KnowledgeSummary);
    }

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write("     Browsed: ");
    Console.ResetColor();
    Console.WriteLine(string.Join(", ", state.BrowsedItems));

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write($"     Recommendations ({state.RecommendationType}): ");
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine(string.Join(", ", state.Recommendations));
    Console.ResetColor();

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write("     Learned: ");
    Console.ResetColor();
    Console.WriteLine($"{state.LearnedCount} new entries committed to entity memory");
}

static void PrintSectionHeader(string kind, string subtitle)
{
    Console.Write("     ");
    Console.ForegroundColor = ConsoleColor.Magenta;
    Console.Write($"▸ {kind}");
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($" — {subtitle}");
    Console.ResetColor();
}

static void PrintLearningEntry(EmpiricalEntry entry)
{
    Console.Write("       ");
    var icon = entry.Kind switch
    {
        EmpiricalKind.Pattern => "🔍",
        EmpiricalKind.Heuristic => "💡",
        EmpiricalKind.Skill => "🛠️",
        _ => "•"
    };
    Console.Write($"{icon} ");

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine(entry.Description);
    Console.ResetColor();

    Console.ForegroundColor = ConsoleColor.DarkGray;
    switch (entry.Kind)
    {
        case EmpiricalKind.Pattern:
            if (entry.Condition is not null) Console.WriteLine($"          condition: {entry.Condition}");
            if (entry.Effect is not null) Console.WriteLine($"          effect:    {entry.Effect}");
            break;
        case EmpiricalKind.Heuristic:
            if (entry.Situation is not null) Console.WriteLine($"          when:   {entry.Situation}");
            if (entry.PreferredApproach is not null) Console.WriteLine($"          do:     {entry.PreferredApproach}");
            if (entry.AvoidedApproach is not null) Console.WriteLine($"          avoid:  {entry.AvoidedApproach}");
            break;
        case EmpiricalKind.Skill:
            if (entry.Goal is not null) Console.WriteLine($"          goal:  {entry.Goal}");
            if (entry.Steps is { Count: > 0 })
                for (var i = 0; i < entry.Steps.Count; i++)
                    Console.WriteLine($"          step {i + 1}: {entry.Steps[i]}");
            break;
    }

    Console.Write("          ");
    Console.Write($"confidence: {entry.Confidence:F2}");
    Console.Write($"  observations: {entry.ObservationCount}");
    Console.Write($"  source: {entry.Source}");
    if (entry.Evidence.Count > 0)
        Console.Write($"  evidence: {entry.Evidence.Count} item(s)");
    Console.WriteLine();
    Console.ResetColor();
}

// ── Workflow state ───────────────────────────────────────────────

/// <summary>State flowing through the shopping-visit workflow.</summary>
record ShopState
{
    // ── Input (set before workflow runs) ─────────────────────────
    public required string CustomerId { get; init; }
    public required string SessionId { get; init; }
    public IReadOnlyList<string> BrowseSimulation { get; init; } = [];
    public IReadOnlyList<LearningInput> Learnings { get; init; } = [];
    public string? StyleProfile { get; init; }

    // ── Populated by load_profile ───────────────────────────────
    public bool IsReturning { get; init; }
    public IReadOnlyList<string> RecalledPatterns { get; init; } = [];
    public string? KnowledgeSummary { get; init; }

    // ── Populated by browse ─────────────────────────────────────
    public IReadOnlyList<string> BrowsedItems { get; init; } = [];

    // ── Populated by recommend ──────────────────────────────────
    public IReadOnlyList<string> Recommendations { get; init; } = [];
    public string RecommendationType { get; init; } = "none";

    // ── Populated by learn ──────────────────────────────────────
    public int LearnedCount { get; init; }
}

/// <summary>Describes one thing the system learned during a browsing session.</summary>
record LearningInput
{
    public required EmpiricalKind Kind { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required string Description { get; init; }
    public required float Confidence { get; init; }
    public string? Condition { get; init; }
    public string? Effect { get; init; }
    public string? Situation { get; init; }
    public string? PreferredApproach { get; init; }
}
