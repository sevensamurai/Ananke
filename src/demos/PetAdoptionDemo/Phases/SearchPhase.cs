using System.Text.RegularExpressions;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Tools;

/// <summary>
/// Searching phase — browse pets, ask questions, start an adoption.
/// The <c>start_adoption</c> tool fires <see cref="AdoptionAction.StartPaperwork"/>
/// to transition the session into the Paperwork phase.
/// </summary>
internal static class SearchPhase
{
    const string Prompt = """
        You are a friendly and knowledgeable pet adoption assistant at Happy Tails Shelter.
        You help people find their perfect companion animal.

        You have access to the following tools:
        - search: Search the shelter's entire knowledge base — available pets, adoption process,
          pet care tips, policies, and fees. Works with any natural-language query. Results are
          tagged with their source section (e.g. "pets" for pet listings, "knowledge" for
          general information) so you can see where each result came from.
        - start_adoption: Begin an adoption application for a pet THE USER HAS NAMED.
          Use this immediately when the user expresses intent to adopt a specific pet
          (e.g. "I want to adopt Ziggy", "can I adopt Luna?", "let's adopt Buddy").

        Tool selection rules:
        1. User names a pet + wants to adopt → start_adoption (do NOT search first).
        2. User refers to a pet with a pronoun or demonstrative ("that one", "her", "the rabbit")
           and wants to adopt → resolve the reference to the most recent matching pet from the
           conversation, then call start_adoption with that pet's name.
        3. Any other question → search.

        Guidelines:
        - Always be warm and encouraging — adopting a pet is exciting!
        - When describing pets, highlight their personality and what makes them special.
        - Be honest about special needs (e.g., Luna's FIV status, Max's joint supplements).
        - If someone asks about a pet that doesn't exist, let them know and suggest alternatives.
        - Use Markdown formatting for clarity (bold names, bullet points for lists).
        - When you receive a user message that is marked as an interruption or refinement,
          treat it as an addition to the previous request — combine the original intent with
          the new input rather than discarding what came before.
        - When the user says "that one", "this one", "her", "him", etc., look at the conversation
          history to identify which pet was most recently discussed. If exactly one pet was
          mentioned or returned, use that pet's name. If multiple were discussed, ask which one.
        """;

    const int SimulatedDelayMs = 3500;

    internal static void Register(AdoptionSession session)
    {
        session.Machine.OnEnter(AdoptionPhase.Searching, async ct =>
        {
            session.Logger.LogInformation("🔍 Entering Searching phase");
            session.Logger.LogInformation("🔍 Message history ({Count} messages):", session.Messages.Count);
            foreach (var msg in session.Messages)
            {
                var preview = msg.Content?.ReplaceLineEndings(" ") ?? "(no content)";
                if (preview.Length > 100) preview = preview[..100] + "…";
                var toolInfo = msg.ToolCalls is { Count: > 0 }
                    ? $" [tools: {string.Join(", ", msg.ToolCalls.Select(t => t.FunctionName))}]"
                    : msg.ToolCallId is not null ? $" [tool_call_id: {msg.ToolCallId}]" : "";
                session.Logger.LogInformation("🔍   [{Role}]{ToolInfo}: {Preview}", msg.Role, toolInfo, preview);
            }

            var tools = CreateTools(session);

            await session.EmitAsync("phase", new { phase = "searching" });

            await session.StreamAsync(
                StreamingChatWorkflow.Create("search", session.Model)
                    .WithSystemPrompt(Prompt)
                    .WithTools(tools)
                    .WithMaxToolRounds(10)
                    .BuildStream(session.Messages, ct));

            session.Logger.LogInformation("🔍 After workflow: {Count} messages in history", session.Messages.Count);
            foreach (var msg in session.Messages)
            {
                var preview = msg.Content?.ReplaceLineEndings(" ") ?? "(no content)";
                if (preview.Length > 60) preview = preview[..60] + "…";
                session.Logger.LogInformation("🔍   [{Role}]: {Preview}", msg.Role, preview);
            }
        });
    }

    private static ToolKit CreateTools(AdoptionSession session) => new ToolKit("search")
        .AddTool(
            name: "search",
            description:
                "Search the shelter's entire knowledge base — available pets, adoption process, " +
                "pet care tips, policies, and fees. Accepts any natural-language query. " +
                "Results come from all knowledge sections, ranked by relevance.",
            execute: async query =>
            {
                session.Logger.LogInformation("🔍 search called: {Query}", query);
                var results = await session.Knowledge.SearchAsync(query, new SearchOptions { TopK = 5 });
                await Task.Delay(SimulatedDelayMs);
                session.Logger.LogInformation("🔍 search → {Count} results", results.Count);
                return ToolResult.Json(results);
            },
            paramName: "query",
            paramDescription: "Natural language search query")
        .AddTool(
            name: "start_adoption",
            description:
                "Begin an adoption application for a named pet. Call this when the user " +
                "expresses intent to adopt a specific pet by name (e.g. 'I want to adopt Ziggy'). " +
                "This moves the conversation to the paperwork phase.",
            execute: async petName =>
            {
                var entry = await session.Knowledge.Catalog.GetAsync(petName.Trim());
                if (entry is null)
                {
                    var available = await session.Knowledge.Catalog.BrowseAsync();
                    var suggestions = string.Join(", ", available
                        .Where(e => e.Category is "dog" or "cat" or "rabbit" or "bird")
                        .Take(5).Select(e => e.Source));
                    return ToolResult.Error(
                        $"Pet '{petName}' not found. Available: {suggestions}.");
                }

                // Look up the full pet listing to extract the adoption fee
                var ctx = session.GetContext<AdoptionContext>();
                ctx.PetName = petName.Trim();
                var petResults = await session.Knowledge[ShelterKnowledge.Pets].Store
                    .SearchAsync(petName.Trim(), new SearchOptions { TopK = 1 });
                var petText = petResults.FirstOrDefault()?.Text ?? "";
                var feeMatch = Regex.Match(petText, @"Adoption fee:\s*\$(\d+)");
                if (feeMatch.Success)
                    ctx.AdoptionFee = decimal.Parse(feeMatch.Groups[1].Value);

                var appId = $"APP-{Random.Shared.Next(10000, 99999)}";
                session.Logger.LogInformation("🐾 Adoption started: {Pet} ({AppId}), fee: ${Fee}",
                    petName.Trim(), appId, ctx.AdoptionFee?.ToString("F0") ?? "unknown");

                // Fire-and-forget: let the streaming workflow finish this tool round
                // before PaperworkPhase OnEnter runs. RunSseLoopAsync picks up the new phase.
                _ = session.Machine.FireAsync(AdoptionAction.StartPaperwork);

                return ToolResult.Ok(
                    $"✅ Application **{appId}** created for **{petName.Trim()}**! " +
                    $"Moving to paperwork.");
            },
            paramName: "pet_name",
            paramDescription: "The name of the pet to start an adoption application for");
}
