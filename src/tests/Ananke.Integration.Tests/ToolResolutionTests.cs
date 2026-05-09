using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Orchestration.Tools;
using Shouldly;
using System.Runtime.CompilerServices;
using System.Text;


namespace Ananke.Integration.Tests;

/// <summary>
/// Integration tests for the browse → adopt tool-resolution flow.
/// Uses a <see cref="ScriptedModel"/> that mimics LLM tool selection
/// by inspecting the conversation and picking the best matching tool.
/// </summary>
[TestFixture]
public class ToolResolutionTests
{
    // ── Knowledge store seeded with pet data ─────────────────────

    private InMemoryKnowledgeStore _store = null!;
    private InMemoryKnowledgeCatalog _catalog = null!;

    private static readonly (string Name, string Category, string Text)[] PetData =
    [
        ("Buddy", "dog", "Meet Buddy. He's a one-year-old golden retriever with more energy than he knows what to do with. Buddy loves fetch, swimming, and running alongside bicycles. Adoption fee: $150."),
        ("Luna", "cat", "Luna is a three-year-old domestic shorthair tabby cat. She's the queen of window perches. Luna is FIV-positive, which means she should stay indoors. Adoption fee: $75."),
        ("Daisy", "rabbit", "Daisy is a five-year-old Holland Lop rabbit. She's litter-trained, loves being petted, and does adorable binkies when she's happy. Adoption fee: $40."),
        ("Captain Flint", "bird", "Captain Flint is a seven-year-old African Grey parrot. He has a vocabulary of about 50 words. He needs an experienced bird owner. Adoption fee: $60."),
        ("Max", "dog", "Max is an eight-year-old beagle mix. He's calm, fully house-trained, and an absolute couch potato. Adoption fee: $50."),
        ("Rocky", "dog", "Rocky is a three-year-old pit bull terrier mix. He's gentle, goofy, and desperate to be someone's best friend. Adoption fee: $100.")
    ];

    [SetUp]
    public async Task SetUp()
    {
        var embedder = new InMemoryEmbedder();
        _store = new InMemoryKnowledgeStore(embedder);
        _catalog = new InMemoryKnowledgeCatalog(embedder);

        var docs = PetData.Select((pet, i) => new KnowledgeDocument
        {
            Id = $"pet:{i}",
            Text = pet.Text,
            Metadata = new Dictionary<string, string>
            {
                ["source"] = "available-pets.md",
                ["pet_name"] = pet.Name,
                ["pet_category"] = pet.Category
            }
        });
        await _store.UpsertAsync(docs);

        foreach (var pet in PetData)
        {
            await _catalog.IndexAsync(new CatalogEntry
            {
                Source = pet.Name,
                Summary = pet.Text.Length > 120 ? pet.Text[..120] + "…" : pet.Text,
                Keywords = [pet.Name, pet.Category],
                Category = pet.Category,
                IndexedAt = DateTimeOffset.UtcNow,
                ChunkCount = 1
            });
        }
    }

    // ── Browse tool using catalog + store (mirrors SearchPhase) ───

    private ToolKit CreateBrowseAndAdoptTools(List<string> adoptionLog)
    {
        return new ToolKit("search")
            .AddTool(new ToolDefinition
            {
                Name = "browse_pets",
                Description = "List available pets, optionally filtered by animal type.",
                Parameters = [new ToolParameter("category", "dog, cat, rabbit, bird, or empty for all")],
                Execute = async (args, _) =>
                {
                    args.TryGetValue("category", out var raw);
                    var category = raw?.ToString() ?? "";

                    var browseOptions = string.IsNullOrWhiteSpace(category)
                        ? new CatalogBrowseOptions()
                        : new CatalogBrowseOptions { Category = category };

                    var entries = (await _catalog.BrowseAsync(browseOptions))
                        .Where(e => e.Category is "dog" or "cat" or "rabbit" or "bird")
                        .ToList();

                    var sb = new StringBuilder();
                    var label = string.IsNullOrWhiteSpace(category) ? "pet" : category;

                    if (entries.Count == 0)
                    {
                        sb.AppendLine($"No {label}s found.");
                        return sb.ToString();
                    }

                    sb.AppendLine($"=== {entries.Count} {label}(s) found ===");
                    if (entries.Count == 1)
                        sb.AppendLine($"(This is the ONLY {label} available.)");
                    sb.AppendLine();

                    foreach (var entry in entries)
                    {
                        var chunks = await _store.SearchAsync(
                            entry.Source,
                            new SearchOptions { TopK = 1, Filter = new KnowledgeFilter { ["pet_name"] = entry.Source } });
                        var fullText = chunks.Count > 0 ? chunks[0].Text : entry.Summary;
                        sb.AppendLine($"**{entry.Source}** ({entry.Category}):");
                        sb.AppendLine(fullText);
                        sb.AppendLine();
                    }
                    return sb.ToString();
                }
            })
            .AddTool(
                name: "start_adoption",
                description: "Begin adoption for a named pet.",
                execute: async petName =>
                {
                    var entry = await _catalog.GetAsync(petName.Trim());
                    if (entry is null)
                        return ToolResult.Error($"Pet '{petName}' not found.");
                    adoptionLog.Add(petName.Trim());
                    return ToolResult.Ok($"✅ Adoption started for {petName.Trim()}!");
                },
                paramName: "pet_name",
                paramDescription: "The name of the pet to adopt");
    }

    // ── Scripted model ───────────────────────────────────────────

    /// <summary>
    /// A deterministic fake LLM that inspects the conversation history and
    /// available tools to decide what to do, mimicking real tool selection.
    /// Each "step" is a <see cref="ScriptStep"/> that matches a condition
    /// and produces a response (text or tool call).
    /// </summary>
    private sealed class ScriptedModel(IReadOnlyList<ScriptStep> steps) : IStreamingAgentModel
    {
        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct) =>
            Task.FromResult(Resolve(request));

        public async IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            var response = Resolve(request);
            if (response.Text is { Length: > 0 })
                yield return new AgentStreamChunk { TextDelta = response.Text };
            yield return new AgentStreamChunk { CompletedResponse = response };
        }

        private AgentResponse Resolve(AgentRequest request)
        {
            foreach (var step in steps)
            {
                if (step.Matches(request))
                    return step.Respond(request);
            }

            // Fallback: just echo
            return new AgentResponse { Text = "[no matching script step]" };
        }
    }

    /// <summary>
    /// A single step in a scripted conversation. Checks a condition against
    /// the request and produces a deterministic response.
    /// </summary>
    private sealed record ScriptStep(
        Func<AgentRequest, bool> Matches,
        Func<AgentRequest, AgentResponse> Respond);

    // ── Helpers to build script steps ────────────────────────────

    /// <summary>Match when the last user message contains the given text.</summary>
    static Func<AgentRequest, bool> UserSays(string contains) => req =>
        req.Messages.LastOrDefault(m => m.Role == AgentRole.User)?.Content
            ?.Contains(contains, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Match when a tool result from the given tool is in the messages.</summary>
    static Func<AgentRequest, bool> HasToolResult(string toolName) => req =>
        req.Messages.Any(m => m.Role == AgentRole.Tool &&
            req.Messages.Any(a => a.ToolCalls?.Any(tc => tc.FunctionName == toolName && tc.Id == m.ToolCallId) == true));

    /// <summary>True when no tool results exist yet in the request.</summary>
    static Func<AgentRequest, bool> NoToolResults() => req =>
        !req.Messages.Any(m => m.Role == AgentRole.Tool);

    /// <summary>Build a response that calls a tool.</summary>
    static Func<AgentRequest, AgentResponse> CallTool(string name, Func<AgentRequest, string> argsJson) =>
        req => new AgentResponse
        {
            ToolCalls = [new AgentToolCall($"tc_{name}", name, argsJson(req))]
        };

    /// <summary>Build a text response.</summary>
    static Func<AgentRequest, AgentResponse> Say(string text) =>
        _ => new AgentResponse { Text = text };

    // ═════════════════════════════════════════════════════════════
    //  Tests
    // ═════════════════════════════════════════════════════════════

    [Test]
    public async Task Browse_rabbits_returns_single_consolidated_entry()
    {
        // The catalog should contain Daisy as a rabbit
        var daisy = await _catalog.GetAsync("Daisy");
        daisy.ShouldNotBeNull();
        daisy.Category.ShouldBe("rabbit");

        var adoptionLog = new List<string>();
        var tools = CreateBrowseAndAdoptTools(adoptionLog);

        // Scripted LLM: user asks for rabbits → call browse_pets(rabbit)
        //               after tool result    → summarize
        var model = new ScriptedModel(
        [
            new(req => HasToolResult("browse_pets")(req),
                Say("We have one rabbit: **Daisy**, a Holland Lop!")),
            new(req => UserSays("rabbit")(req) && NoToolResults()(req),
                CallTool("browse_pets", _ => """{"category":"rabbit"}""")),
        ]);

        var events = new List<ChatSessionEvent>();
        await foreach (var evt in StreamingChatWorkflow.Create("test", model)
            .WithTools(tools)
            .WithMaxToolRounds(3)
            .BuildStream([AgentMessage.User("do you have rabbits?")]))
        {
            events.Add(evt);
        }

        // Tool was called and returned results
        var toolCall = events.OfType<ToolCallEvent>().ShouldHaveSingleItem();
        toolCall.Name.ShouldBe("browse_pets");

        var toolResult = events.OfType<ToolResultEvent>().ShouldHaveSingleItem();
        // Consolidated: should mention exactly 1 rabbit
        toolResult.Result.ShouldContain("1 rabbit(s) found");
        toolResult.Result.ShouldContain("ONLY rabbit");
        toolResult.Result.ShouldContain("Daisy");
        // Should NOT mention dogs/cats by name in the summary
        toolResult.Result.ShouldNotContain("**Buddy**");
        toolResult.Result.ShouldNotContain("**Max**");

        events.OfType<CompletedEvent>().ShouldHaveSingleItem()
            .FullText!.ShouldContain("Daisy");
    }

    [Test]
    public async Task Browse_then_adopt_that_one_resolves_to_pet_name()
    {
        var adoptionLog = new List<string>();
        var tools = CreateBrowseAndAdoptTools(adoptionLog);

        // Scripted LLM conversation:
        // Turn 1: user="do you have rabbits" → browse_pets(rabbit)
        // Turn 1 (after tool): → "We have Daisy"
        // Turn 2: user="adopt that one" → since only 1 rabbit, call start_adoption(Daisy)
        // Turn 2 (after tool): → "Great, adoption started!"
        var model = new ScriptedModel(
        [
            new(req => HasToolResult("start_adoption")(req),
                Say("Great, adoption started for Daisy!")),
            new(req => UserSays("adopt")(req),
                CallTool("start_adoption", _ => """{"pet_name":"Daisy"}""")),
            new(req => HasToolResult("browse_pets")(req),
                Say("We have one rabbit: **Daisy**, a five-year-old Holland Lop!")),
            new(req => UserSays("rabbit")(req) && NoToolResults()(req),
                CallTool("browse_pets", _ => """{"category":"rabbit"}""")),
        ]);

        // ── Turn 1: browse
        var messages = new List<AgentMessage> { AgentMessage.User("do you have rabbits?") };
        var turn1Events = new List<ChatSessionEvent>();
        await foreach (var evt in StreamingChatWorkflow.Create("test", model)
            .WithTools(tools)
            .WithMaxToolRounds(3)
            .BuildStream(messages))
        {
            turn1Events.Add(evt);
        }

        var browseResult = turn1Events.OfType<ToolResultEvent>().ShouldHaveSingleItem();
        browseResult.Result.ShouldContain("1 rabbit(s) found");
        browseResult.Result.ShouldContain("ONLY rabbit");

        // ── Turn 2: "adopt that one" — model sees the single rabbit and resolves to Daisy
        messages.Add(AgentMessage.User("nice, would like to adopt that one"));
        var turn2Events = new List<ChatSessionEvent>();
        await foreach (var evt in StreamingChatWorkflow.Create("test", model)
            .WithTools(tools)
            .WithMaxToolRounds(3)
            .BuildStream(messages))
        {
            turn2Events.Add(evt);
        }

        var adoptCall = turn2Events.OfType<ToolCallEvent>().ShouldHaveSingleItem();
        adoptCall.Name.ShouldBe("start_adoption");

        var adoptResult = turn2Events.OfType<ToolResultEvent>().ShouldHaveSingleItem();
        adoptResult.Result.ShouldContain("Daisy");

        // The tool actually executed — Daisy was adopted
        adoptionLog.ShouldHaveSingleItem().ShouldBe("Daisy");
    }

    [Test]
    public async Task Browse_dogs_returns_multiple_consolidated_entries()
    {
        var adoptionLog = new List<string>();
        var tools = CreateBrowseAndAdoptTools(adoptionLog);

        var model = new ScriptedModel(
        [
            new(req => HasToolResult("browse_pets")(req),
                Say("We have several dogs available!")),
            new(req => UserSays("dog")(req) && NoToolResults()(req),
                CallTool("browse_pets", _ => """{"category":"dog"}""")),
        ]);

        var events = new List<ChatSessionEvent>();
        await foreach (var evt in StreamingChatWorkflow.Create("test", model)
            .WithTools(tools)
            .WithMaxToolRounds(3)
            .BuildStream([AgentMessage.User("show me your dogs")]))
        {
            events.Add(evt);
        }

        var toolResult = events.OfType<ToolResultEvent>().ShouldHaveSingleItem();
        // Should show multiple dogs, each as a consolidated entry
        toolResult.Result.ShouldContain("**Buddy**");
        toolResult.Result.ShouldContain("**Max**");
        toolResult.Result.ShouldContain("**Rocky**");
        // Should NOT contain "ONLY" since there are multiple
        toolResult.Result.ShouldNotContain("ONLY");
        // Should NOT list non-dogs
        toolResult.Result.ShouldNotContain("**Daisy**");
        toolResult.Result.ShouldNotContain("**Luna**");
    }

    [Test]
    public async Task Catalog_contains_all_pets_with_correct_categories()
    {
        var all = await _catalog.BrowseAsync();
        all.Count.ShouldBe(PetData.Length);

        (await _catalog.GetAsync("Buddy"))!.Category.ShouldBe("dog");
        (await _catalog.GetAsync("Luna"))!.Category.ShouldBe("cat");
        (await _catalog.GetAsync("Daisy"))!.Category.ShouldBe("rabbit");
        (await _catalog.GetAsync("Captain Flint"))!.Category.ShouldBe("bird");
        (await _catalog.GetAsync("Max"))!.Category.ShouldBe("dog");
        (await _catalog.GetAsync("Rocky"))!.Category.ShouldBe("dog");

        // Category browse returns only matching entries
        var rabbits = await _catalog.BrowseAsync(new CatalogBrowseOptions { Category = "rabbit" });
        rabbits.ShouldHaveSingleItem().Source.ShouldBe("Daisy");

        var dogs = await _catalog.BrowseAsync(new CatalogBrowseOptions { Category = "dog" });
        dogs.Count.ShouldBe(3);
    }

    [Test]
    public async Task Direct_adopt_by_name_skips_browse()
    {
        var adoptionLog = new List<string>();
        var tools = CreateBrowseAndAdoptTools(adoptionLog);

        // When user says "I want to adopt Buddy", the model should call
        // start_adoption directly without browsing first
        var model = new ScriptedModel(
        [
            new(req => HasToolResult("start_adoption")(req),
                Say("Adoption started for Buddy!")),
            new(req => UserSays("adopt")(req) && UserSays("Buddy")(req) && NoToolResults()(req),
                CallTool("start_adoption", _ => """{"pet_name":"Buddy"}""")),
        ]);

        var events = new List<ChatSessionEvent>();
        await foreach (var evt in StreamingChatWorkflow.Create("test", model)
            .WithTools(tools)
            .WithMaxToolRounds(3)
            .BuildStream([AgentMessage.User("I want to adopt Buddy")]))
        {
            events.Add(evt);
        }

        // No browse_pets call — went straight to start_adoption
        events.OfType<ToolCallEvent>().Select(e => e.Name)
            .ShouldBe(["start_adoption"]);

        adoptionLog.ShouldHaveSingleItem().ShouldBe("Buddy");
    }
}
