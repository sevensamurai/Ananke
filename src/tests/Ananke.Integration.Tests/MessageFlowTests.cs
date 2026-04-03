using System.Runtime.CompilerServices;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Tools;
using Ananke.StateMachine;
using Shouldly;

using SM = Ananke.StateMachine.StateMachine;

namespace Ananke.Integration.Tests;

/// <summary>
/// Integration tests covering message/context flow across:
///   • Multi-turn conversations (same messages list, sequential workflows)
///   • Phase transitions triggered by tools mid-execution
///   • Interrupt → resume with full history preservation
///   • Orphaned tool-call patching at phase boundaries
///
/// These tests reproduce the exact data-flow patterns used by the PetAdoptionDemo
/// without any HTTP/ASP.NET dependencies, making it easy to diagnose regressions
/// in the state-machine ↔ workflow ↔ tool pipeline.
/// </summary>
[TestFixture]
public class MessageFlowTests
{
    // ─── Protocol (mirrors AdoptionMachine phases) ───────────────

    enum Phase { Searching, Interrupted, Paperwork, Payment, Done }
    enum Action { Start, StartPaperwork, StartPayment, Complete, Interrupt, Resume }

    static StateMachine<Phase, Action> CreateFullMachine() =>
        SM.Create<Phase, Action>(Phase.Searching, b => b
            .From(Phase.Searching).On(Action.Start).To(Phase.Searching)
            .From(Phase.Paperwork).On(Action.Start).To(Phase.Paperwork)
            .From(Phase.Searching).On(Action.StartPaperwork).To(Phase.Paperwork)
            .From(Phase.Paperwork).On(Action.StartPayment).To(Phase.Payment)
            .From(Phase.Payment).On(Action.Complete).To(Phase.Done)
            .From(Phase.Searching).On(Action.Interrupt).ToInterrupt(Phase.Interrupted)
            .From(Phase.Interrupted).On(Action.Resume).ToResume());

    // ─── Fake streaming model ────────────────────────────────────

    sealed class FakeStreamingModel(
        Func<int, AgentRequest, CancellationToken, IAsyncEnumerable<AgentStreamChunk>> generate)
        : IStreamingAgentModel
    {
        private int _callCount;

        public Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct) =>
            throw new NotSupportedException("Use streaming API");

        public IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
            AgentRequest request, CancellationToken ct)
        {
            var call = Interlocked.Increment(ref _callCount);
            return generate(call, request, ct);
        }
    }

    // ─── Chunk generators ────────────────────────────────────────

    static async IAsyncEnumerable<AgentStreamChunk> TextChunks(
        string[] parts,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var full = string.Join("", parts);
        foreach (var part in parts)
        {
            yield return new AgentStreamChunk { TextDelta = part };
            await Task.Delay(5, ct);
        }
        yield return new AgentStreamChunk
        {
            CompletedResponse = new AgentResponse { Text = full }
        };
    }

    static async IAsyncEnumerable<AgentStreamChunk> ToolCallChunks(
        string preamble,
        AgentToolCall toolCall,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(preamble))
        {
            yield return new AgentStreamChunk { TextDelta = preamble };
            await Task.Delay(5, ct);
        }
        yield return new AgentStreamChunk
        {
            CompletedResponse = new AgentResponse
            {
                Text = preamble,
                ToolCalls = [toolCall]
            }
        };
    }

    // ─── Stream consumer (mirrors AdoptionSession.StreamAsync) ───

    static async Task ConsumeStreamAsync(IAsyncEnumerable<ChatSessionEvent> events)
    {
        await foreach (var _ in events) { }
    }

    // ─── SSE loop (mirrors ChatEndpoint / RunSseLoopAsync) ───────

    static async Task RunSseLoop(StateMachine<Phase, Action> machine, Phase terminal)
    {
        while (!EqualityComparer<Phase>.Default.Equals(machine.CurrentState, terminal))
        {
            var work = machine.CurrentWork;
            if (work is null)
            {
                await Task.Delay(150);
                work = machine.CurrentWork;
                if (work is null) break;
            }

            try { await work; }
            catch (OperationCanceledException) { }

            if (machine.CurrentWork == work) break;
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  1. Multi-turn message persistence
    // ═════════════════════════════════════════════════════════════

    [Test]
    public async Task Multi_turn_messages_persist_across_sequential_workflows()
    {
        // Simulates two sequential HTTP requests sharing the same session messages list.
        // Turn 1: user asks question → model calls browse_pets → model replies.
        // Turn 2: user asks follow-up → model replies referencing prior context.
        // Key assertion: messages from turn 1 (including tool calls/results) are
        // still present when turn 2's workflow starts.

        var model = new FakeStreamingModel((call, req, ct) => call switch
        {
            1 => ToolCallChunks("Let me look...",
                    new AgentToolCall("tc_browse", "browse_pets", """{"category":"rabbit"}"""), ct),
            2 => TextChunks(["We have one rabbit: ", "**Daisy**!"], ct),
            3 => TextChunks(["Sure, ", "starting adoption for Daisy!"], ct),
            _ => TextChunks(["fallback"], ct)
        });

        var tools = new ToolKit("search")
            .AddTool(
                name: "browse_pets",
                description: "List pets",
                execute: () => ToolResult.Ok("=== 1 rabbit found ===\n**Daisy** (rabbit): Holland Lop"));

        var messages = new List<AgentMessage>();

        // ── Turn 1
        messages.Add(AgentMessage.User("do you have rabbits?"));
        await ConsumeStreamAsync(
            StreamingChatWorkflow.Create("turn1", model)
                .WithTools(tools)
                .WithMaxToolRounds(3)
                .BuildStream(messages));

        var afterTurn1 = messages.Count;
        afterTurn1.ShouldBeGreaterThanOrEqualTo(4,
            "Turn 1 should produce: user + assistant(tool_calls) + tool_result + assistant(final)");

        // Verify tool-related messages are present
        messages.ShouldContain(m =>
            m.Role == AgentRole.Assistant && m.ToolCalls != null && m.ToolCalls.Count > 0,
            "Should have assistant message with tool_calls");
        messages.ShouldContain(m =>
            m.Role == AgentRole.Tool && m.ToolCallId == "tc_browse",
            "Should have tool result for browse_pets");
        messages.ShouldContain(m =>
            m.Role == AgentRole.Assistant && m.Content != null && m.Content.Contains("Daisy"),
            "Should have assistant reply mentioning Daisy");

        // ── Turn 2: add new user message on the SAME list
        messages.Add(AgentMessage.User("nice, would like to adopt her"));

        // Verify turn-1 messages are still there (not replaced)
        messages.Count.ShouldBe(afterTurn1 + 1);
        messages[0].Role.ShouldBe(AgentRole.User);
        messages[0].Content.ShouldBe("do you have rabbits?");

        await ConsumeStreamAsync(
            StreamingChatWorkflow.Create("turn2", model)
                .WithTools(tools)
                .WithMaxToolRounds(3)
                .BuildStream(messages));

        // Turn 2 should have added the final assistant response
        messages.Count.ShouldBeGreaterThan(afterTurn1 + 1);

        // All turn-1 messages are still intact
        messages.ShouldContain(m =>
            m.Role == AgentRole.Tool && m.ToolCallId == "tc_browse",
            "Tool result from turn 1 must survive into turn 2");
    }

    [Test]
    public async Task Messages_list_is_same_reference_after_workflow_completes()
    {
        var model = new FakeStreamingModel((_, _, ct) =>
            TextChunks(["Hello!"], ct));

        var messages = new List<AgentMessage> { AgentMessage.User("hi") };
        var originalRef = messages;

        await ConsumeStreamAsync(
            StreamingChatWorkflow.Create("ref-test", model)
                .BuildStream(messages));

        // The workflow should mutate the same list, not replace it
        ReferenceEquals(messages, originalRef).ShouldBeTrue(
            "BuildStream must operate on the caller's list in-place");

        messages.Count.ShouldBeGreaterThan(1,
            "Workflow should have appended the assistant response");
    }

    // ═════════════════════════════════════════════════════════════
    //  2. Tool-triggered phase transition (the adoption flow bug)
    // ═════════════════════════════════════════════════════════════

    [Test, CancelAfter(5_000)]
    public async Task Tool_triggered_phase_transition_patches_orphaned_tool_calls()
    {
        // Reproduces the "HTTP 400 invalid_request_error" bug:
        // 1. Search-phase workflow calls start_adoption tool
        // 2. The tool fires FireAsync(StartPaperwork) MID-EXECUTION
        // 3. Paperwork phase starts with a messages list that has
        //    assistant(tool_calls) but NO matching tool result yet
        // 4. Without patching, the LLM API would reject the history
        //
        // The fix: PaperworkPhase patches orphaned tool calls before
        // starting its own workflow.

        var machine = CreateFullMachine();
        var messages = new List<AgentMessage>();
        var phaseLog = new List<string>();

        // ── Search phase: model calls start_adoption, then paperwork model replies
        var searchModel = new FakeStreamingModel((call, _, ct) => call switch
        {
            1 => ToolCallChunks("Starting adoption...",
                    new AgentToolCall("tc_adopt", "start_adoption", """{"pet_name":"Daisy"}"""), ct),
            _ => TextChunks(["fallback"], ct)
        });

        var searchTools = new ToolKit("search")
            .AddTool(
                name: "start_adoption",
                description: "Begin adoption",
                execute: async (string petName) =>
                {
                    // This fires the state transition WHILE the tool is still executing
                    await machine.FireAsync(Action.StartPaperwork);
                    return ToolResult.Ok($"Adoption started for {petName}!");
                },
                paramName: "pet_name",
                paramDescription: "Pet name");

        // ── Paperwork phase: patches orphans, adds user msg, runs workflow
        var paperworkModel = new FakeStreamingModel((_, _, ct) =>
            TextChunks(["Please provide your ", "ID for the paperwork."], ct));

        // Register Search phase
        machine.OnEnter(Phase.Searching, async ct =>
        {
            phaseLog.Add("search:enter");

            await ConsumeStreamAsync(
                StreamingChatWorkflow.Create("search", searchModel)
                    .WithSystemPrompt("You are a pet adoption assistant.")
                    .WithTools(searchTools)
                    .WithMaxToolRounds(5)
                    .BuildStream(messages, ct));
        });

        // Register Paperwork phase (mirrors PaperworkPhase.cs with the fix)
        machine.OnEnter(Phase.Paperwork, async ct =>
        {
            phaseLog.Add("paperwork:enter");

            // THE FIX: patch orphaned tool calls before starting new workflow
            messages.PatchOrphanedToolCalls();

            messages.Add(AgentMessage.User(
                "I've selected a pet. Please help me with the adoption paperwork."));

            // Validate: at this point, every assistant tool_calls should have
            // matching tool results (either real or patched)
            ValidateNoOrphanedToolCalls(messages);

            await ConsumeStreamAsync(
                StreamingChatWorkflow.Create("paperwork", paperworkModel)
                    .WithSystemPrompt("You are the paperwork assistant.")
                    .BuildStream(messages, ct));
        });

        // ── Run
        messages.Add(AgentMessage.User("I want to adopt Daisy"));

        await machine.FireAsync(Action.Start);
        await RunSseLoop(machine, Phase.Done);

        // ── Assert
        phaseLog.ShouldContain("search:enter");
        phaseLog.ShouldContain("paperwork:enter");

        // The orphaned tool call should have been patched
        var patchedResults = messages.Where(m =>
            m.Role == AgentRole.Tool &&
            m.Content != null &&
            m.Content.Contains("[interrupted")).ToList();
        patchedResults.Count.ShouldBeGreaterThanOrEqualTo(1,
            "start_adoption's tool call should be patched since the result " +
            "wasn't added before the phase transition");

        // Paperwork workflow should have completed
        messages.ShouldContain(m =>
            m.Role == AgentRole.Assistant &&
            m.Content != null &&
            m.Content.Contains("paperwork"),
            "Paperwork phase should have produced a response");
    }

    [Test]
    public void Patching_before_adding_user_message_fixes_orphans()
    {
        // Demonstrates the correct fix pattern (used by PaperworkPhase):
        // 1. The tool fires a phase transition mid-execution
        // 2. The new phase patches orphans FIRST
        // 3. Then adds its own user message
        var messages = new List<AgentMessage>
        {
            AgentMessage.User("I want to adopt Daisy"),
            AgentMessage.Assistant("Starting adoption...",
                [new AgentToolCall("tc_adopt", "start_adoption", """{"pet_name":"Daisy"}""")])                // ← tool result NOT yet appended (tool is still executing)
        };

        // Before patching: orphaned tool call
        FindOrphanedToolCallIds(messages).ShouldContain("tc_adopt");

        // Patch BEFORE adding the new user message (the correct fix)
        messages.PatchOrphanedToolCalls();
        FindOrphanedToolCallIds(messages).ShouldBeEmpty(
            "Patching before the user message should fix the orphan");

        // Now it's safe to add the new phase's user message
        messages.Add(AgentMessage.User(
            "I've selected a pet. Please help me with the adoption paperwork."));
        FindOrphanedToolCallIds(messages).ShouldBeEmpty();
    }

    [Test]
    public void Patching_after_adding_user_message_is_too_late()
    {
        // Demonstrates the bug scenario: if the user message is added
        // BEFORE patching, PatchOrphanedToolCalls stops scanning at the
        // User message boundary and never finds the orphaned tool call.
        var messages = new List<AgentMessage>
        {
            AgentMessage.User("I want to adopt Daisy"),
            AgentMessage.Assistant("Starting adoption...",
                [new AgentToolCall("tc_adopt", "start_adoption", """{"pet_name":"Daisy"}""")]),
            // User message added BEFORE patching (the wrong order)
            AgentMessage.User("I've selected a pet. Please help me with the adoption paperwork.")
        };

        // Patching scans backwards and stops at the User message — misses the orphan
        messages.PatchOrphanedToolCalls();

        FindOrphanedToolCallIds(messages).ShouldContain("tc_adopt",
            "Patching after the user message cannot reach the orphaned tool call");
    }

    // ═════════════════════════════════════════════════════════════
    //  3. Interrupt + resume preserves full message context
    // ═════════════════════════════════════════════════════════════

    [Test, CancelAfter(5_000)]
    public async Task Interrupt_mid_tool_preserves_all_prior_messages()
    {
        // Simulates: user asks "do you have rabbits?", agent starts browsing,
        // user interrupts with "also good for granny". After resume, ALL
        // messages from before the interrupt should still be present.

        var toolStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var model = new FakeStreamingModel((call, req, ct) => call switch
        {
            // Call 1: model decides to call browse_pets
            1 => ToolCallChunks("Let me search...",
                    new AgentToolCall("tc_browse", "browse_pets", """{"category":"rabbit"}"""), ct),
            // Call 3 (after resume): model responds with updated results
            _ => TextChunks(["Here are pets ", "for kids and granny!"], ct)
        });

        var tools = new ToolKit("test")
            .AddTool(new ToolDefinition
            {
                Name = "browse_pets",
                Description = "Search pets",
                Parameters = [new ToolParameter("category", "type")],
                Execute = async (_, ct) =>
                {
                    toolStarted.TrySetResult();
                    // Slow enough for the interrupt to arrive
                    await Task.Delay(500, ct);
                    return "Daisy the rabbit";
                }
            });

        var machine = SM.Create<Phase, Action>(Phase.Searching, b => b
            .From(Phase.Searching).On(Action.Start).To(Phase.Searching)
            .From(Phase.Searching).On(Action.Interrupt).ToInterrupt(Phase.Interrupted)
            .From(Phase.Interrupted).On(Action.Resume).ToResume());

        var messages = new List<AgentMessage>();
        AgentMessage? pendingInterrupt = null;

        // Register Search phase
        machine.OnEnter(Phase.Searching, async ct =>
        {
            await ConsumeStreamAsync(
                StreamingChatWorkflow.Create("search", model)
                    .WithSystemPrompt("You are a pet assistant.")
                    .WithTools(tools)
                    .WithMaxToolRounds(5)
                    .BuildStream(messages, ct));
        });

        // Register Interrupt phase
        machine.OnInterrupt(async (payload, _) =>
        {
            pendingInterrupt = payload as AgentMessage;
            await Task.CompletedTask;
        });

        machine.OnEnter(Phase.Interrupted, async _ =>
        {
            messages.PatchOrphanedToolCalls();
            if (pendingInterrupt is not null)
            {
                messages.Add(pendingInterrupt);
                pendingInterrupt = null;
            }
            await machine.FireAsync(Action.Resume);
        });

        // ── Run
        messages.Add(AgentMessage.User("do you have rabbits?"));
        var loopTask = Task.Run(async () =>
        {
            await machine.FireAsync(Action.Start);
            await RunSseLoop(machine, Phase.Done);
        });

        // Wait for tool to start, then interrupt
        await toolStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(20);

        var interruptResult = await machine.FireAsync(
            Action.Interrupt, AgentMessage.User("also good for granny"));
        interruptResult.Success.ShouldBeTrue();

        await loopTask.WaitAsync(TimeSpan.FromSeconds(3));

        // ── Assert: original user message is still there
        messages[0].Role.ShouldBe(AgentRole.User);
        messages[0].Content.ShouldBe("do you have rabbits?");

        // The orphaned tool call was patched
        messages.ShouldContain(m =>
            m.Role == AgentRole.Tool &&
            m.Content != null &&
            m.Content.Contains("[interrupted"),
            "Orphaned browse_pets call should be patched");

        // Interrupt message was added
        messages.ShouldContain(m =>
            m.Role == AgentRole.User &&
            m.Content == "also good for granny");

        // After resume, the new workflow produced output
        messages.ShouldContain(m =>
            m.Role == AgentRole.Assistant &&
            m.Content != null &&
            m.Content.Contains("granny"));
    }

    // ═════════════════════════════════════════════════════════════
    //  4. Message role sequence validation
    // ═════════════════════════════════════════════════════════════

    [Test]
    public async Task Workflow_with_tool_call_produces_valid_message_sequence()
    {
        // Validates that after a tool-calling workflow, the message sequence
        // follows the pattern that LLM APIs require:
        //   user → assistant(tool_calls) → tool(result) → assistant(final)
        // No orphaned tool calls, no out-of-order messages.

        var model = new FakeStreamingModel((call, _, ct) => call switch
        {
            1 => ToolCallChunks("",
                    new AgentToolCall("tc_1", "get_info", """{"q":"test"}"""), ct),
            _ => TextChunks(["Here is the info!"], ct)
        });

        var tools = new ToolKit("test")
            .AddTool(
                name: "get_info",
                description: "Get info",
                execute: (string q) => ToolResult.Ok($"Info about {q}"),
                paramName: "q",
                paramDescription: "query");

        var messages = new List<AgentMessage> { AgentMessage.User("get info about test") };

        await ConsumeStreamAsync(
            StreamingChatWorkflow.Create("seq-test", model)
                .WithTools(tools)
                .WithMaxToolRounds(3)
                .BuildStream(messages));

        // Validate the full sequence
        messages.Count.ShouldBe(4);
        messages[0].Role.ShouldBe(AgentRole.User);
        messages[1].Role.ShouldBe(AgentRole.Assistant);
        messages[1].ToolCalls.ShouldNotBeNull();
        messages[1].ToolCalls!.Count.ShouldBe(1);
        messages[1].ToolCalls![0].Id.ShouldBe("tc_1");
        messages[2].Role.ShouldBe(AgentRole.Tool);
        messages[2].ToolCallId.ShouldBe("tc_1");
        messages[2].Content!.ShouldContain("Info about test");
        messages[3].Role.ShouldBe(AgentRole.Assistant);
        messages[3].Content!.ShouldContain("info");

        // No orphaned tool calls
        FindOrphanedToolCallIds(messages).ShouldBeEmpty();
    }

    [Test]
    public async Task Multiple_tool_calls_in_one_round_all_get_results()
    {
        // Model requests two tools in a single response. Both should have
        // matching tool results in the message history.

        var model = new FakeStreamingModel((call, _, ct) =>
        {
            if (call == 1)
            {
                // Return two tool calls in one response
                return DoubleToolCallChunks(
                    new AgentToolCall("tc_a", "tool_a", """{"x":"1"}"""),
                    new AgentToolCall("tc_b", "tool_b", """{"x":"2"}"""),
                    ct);
            }
            return TextChunks(["Combined results!"], ct);
        });

        var tools = new ToolKit("multi")
            .AddTool("tool_a", "Tool A",
                (string x) => ToolResult.Ok($"A:{x}"), "x", "param")
            .AddTool("tool_b", "Tool B",
                (string x) => ToolResult.Ok($"B:{x}"), "x", "param");

        var messages = new List<AgentMessage> { AgentMessage.User("call both") };

        await ConsumeStreamAsync(
            StreamingChatWorkflow.Create("multi-test", model)
                .WithTools(tools)
                .WithMaxToolRounds(3)
                .BuildStream(messages));

        // Both tool calls should have results
        messages.ShouldContain(m => m.Role == AgentRole.Tool && m.ToolCallId == "tc_a");
        messages.ShouldContain(m => m.Role == AgentRole.Tool && m.ToolCallId == "tc_b");
        FindOrphanedToolCallIds(messages).ShouldBeEmpty();
    }

    // ═════════════════════════════════════════════════════════════
    //  5. PatchOrphanedToolCalls with user message interleaved
    // ═════════════════════════════════════════════════════════════

    [Test]
    public void Patch_inserts_synthetic_result_adjacent_to_tool_call()
    {
        // Mirrors PaperworkPhase: patch first, THEN add the user message.
        // The synthetic result should be inserted right after the assistant.

        var messages = new List<AgentMessage>
        {
            AgentMessage.User("adopt Daisy"),
            AgentMessage.Assistant("",
                [new AgentToolCall("tc_adopt", "start_adoption", """{"pet_name":"Daisy"}""")])        };

        messages.PatchOrphanedToolCalls();

        // Synthetic result inserted right after the assistant message
        messages.Count.ShouldBe(3);
        messages[2].Role.ShouldBe(AgentRole.Tool);
        messages[2].ToolCallId.ShouldBe("tc_adopt");
        messages[2].Content!.ShouldContain("[interrupted");

        // Safe to add user message now
        messages.Add(AgentMessage.User(
            "I've selected a pet. Please help me with the adoption paperwork."));
        FindOrphanedToolCallIds(messages).ShouldBeEmpty();
    }

    [Test]
    public void Patch_does_not_duplicate_existing_tool_results()
    {
        // If the tool result was already added (no orphan), patching is a no-op
        var messages = new List<AgentMessage>
        {
            AgentMessage.User("adopt Daisy"),
            AgentMessage.Assistant("",
                [new AgentToolCall("tc_adopt", "start_adoption", """{"pet_name":"Daisy"}""")]),
            AgentMessage.ToolResult("tc_adopt", "Adoption started!"),
            AgentMessage.User("What's next?")
        };

        var countBefore = messages.Count;
        messages.PatchOrphanedToolCalls();
        messages.Count.ShouldBe(countBefore, "Should not add duplicate tool results");
    }

    // ═════════════════════════════════════════════════════════════
    //  6. Session recreation from client history (fallback path)
    // ═════════════════════════════════════════════════════════════

    [Test]
    public async Task Recreated_session_with_assistant_history_preserves_context()
    {
        // Simulates session recreation from client-side history that includes
        // the assistant response (i.e., the "done" event was received).
        // The model should see the prior context and respond appropriately.

        var model = new FakeStreamingModel((call, req, ct) =>
        {
            // If the model can see Daisy in the history, it responds correctly
            var hasContext = req.Messages.Any(m =>
                m.Role == AgentRole.Assistant &&
                m.Content != null &&
                m.Content.Contains("Daisy"));

            return hasContext
                ? TextChunks(["Starting adoption for Daisy!"], ct)
                : TextChunks(["Which pet would you like to adopt?"], ct);
        });

        // Recreate session from client history (User + Assistant only — no tools)
        var messages = new List<AgentMessage>
        {
            AgentMessage.User("do you have rabbits?"),
            AgentMessage.Assistant("We have one rabbit: **Daisy**, a five-year-old Holland Lop!")
        };

        // New turn: user wants to adopt
        messages.Add(AgentMessage.User("nice, would like to adopt her"));

        await ConsumeStreamAsync(
            StreamingChatWorkflow.Create("recreated", model)
                .BuildStream(messages));

        // Model should have seen Daisy in history and responded correctly
        var lastContent = messages.Last().Content!;
        lastContent.ShouldContain("Daisy");
        lastContent.ShouldNotContain("Which pet");
    }

    [Test]
    public async Task Recreated_session_without_assistant_history_loses_context()
    {
        // Simulates the bug: session recreated from client history that is
        // MISSING the assistant response (because "done" event was never sent).

        var model = new FakeStreamingModel((call, req, ct) =>
        {
            var hasContext = req.Messages.Any(m =>
                m.Role == AgentRole.Assistant &&
                m.Content != null &&
                m.Content.Contains("Daisy"));

            return hasContext
                ? TextChunks(["Starting adoption for Daisy!"], ct)
                : TextChunks(["Which pet would you like to adopt?"], ct);
        });

        // Session recreated WITHOUT the assistant response (missing "done" event)
        var messages = new List<AgentMessage>
        {
            AgentMessage.User("do you have rabbits?")
        };
        messages.Add(AgentMessage.User("nice, would like to adopt her"));

        await ConsumeStreamAsync(
            StreamingChatWorkflow.Create("broken", model)
                .BuildStream(messages));

        // Model has no context about Daisy — this is the bug
        messages.Last().Content!.ShouldContain("Which pet");
    }

    // ═════════════════════════════════════════════════════════════
    //  7. Full multi-phase flow with shared messages
    // ═════════════════════════════════════════════════════════════

    [Test, CancelAfter(5_000)]
    public async Task Full_search_to_paperwork_flow_accumulates_messages()
    {
        // End-to-end: Search phase browses pets, then Paperwork phase
        // uses the same messages list. Messages from search must survive
        // into paperwork.

        var machine = CreateFullMachine();
        var messages = new List<AgentMessage>();
        var phaseOrder = new List<string>();

        // Search model: browse tool → reply
        var searchModel = new FakeStreamingModel((call, _, ct) => call switch
        {
            1 => ToolCallChunks("",
                    new AgentToolCall("tc_browse", "browse", """{"cat":"rabbit"}"""), ct),
            _ => TextChunks(["Found Daisy the rabbit!"], ct)
        });

        var searchTools = new ToolKit("search")
            .AddTool(
                name: "browse",
                description: "Browse pets",
                execute: () => ToolResult.Ok("Daisy (rabbit): Holland Lop"))
            .AddTool(
                name: "start_adoption",
                description: "Start adoption",
                execute: async (string petName) =>
                {
                    await machine.FireAsync(Action.StartPaperwork);
                    return ToolResult.Ok($"Adoption started for {petName}!");
                },
                paramName: "pet_name",
                paramDescription: "Pet name");

        // Paperwork model: simple response
        var paperworkModel = new FakeStreamingModel((_, _, ct) =>
            TextChunks(["Please bring your ", "ID and proof of address."], ct));

        machine.OnEnter(Phase.Searching, async ct =>
        {
            phaseOrder.Add("searching");

            await ConsumeStreamAsync(
                StreamingChatWorkflow.Create("search", searchModel)
                    .WithTools(searchTools)
                    .WithMaxToolRounds(5)
                    .BuildStream(messages, ct));
        });

        machine.OnEnter(Phase.Paperwork, async ct =>
        {
            phaseOrder.Add("paperwork");
            messages.PatchOrphanedToolCalls();

            messages.Add(AgentMessage.User("Help me with paperwork."));

            await ConsumeStreamAsync(
                StreamingChatWorkflow.Create("paperwork", paperworkModel)
                    .BuildStream(messages, ct));
        });

        // ── Turn 1: browse
        messages.Add(AgentMessage.User("do you have rabbits?"));
        await machine.FireAsync(Action.Start);
        await RunSseLoop(machine, Phase.Done);

        phaseOrder.ShouldBe(["searching"]);
        var afterBrowse = messages.Count;

        // Verify search produced tool calls and results
        messages.ShouldContain(m => m.Role == AgentRole.Tool && m.ToolCallId == "tc_browse");
        messages.ShouldContain(m =>
            m.Role == AgentRole.Assistant && m.Content != null && m.Content.Contains("Daisy"));

        // ── Turn 2: adopt (triggers phase transition via tool)
        // Use a new search model that calls start_adoption
        var adoptModel = new FakeStreamingModel((call, _, ct) =>
            ToolCallChunks("",
                new AgentToolCall("tc_adopt", "start_adoption", """{"pet_name":"Daisy"}"""), ct));

        // Re-register search with the adopt model for turn 2
        machine.OnEnter(Phase.Searching, async ct =>
        {
            phaseOrder.Add("searching");
            await ConsumeStreamAsync(
                StreamingChatWorkflow.Create("search2", adoptModel)
                    .WithTools(searchTools)
                    .WithMaxToolRounds(5)
                    .BuildStream(messages, ct));
        });

        messages.Add(AgentMessage.User("I want to adopt Daisy"));
        await machine.FireAsync(Action.Start);
        await RunSseLoop(machine, Phase.Done);

        // ── Assert: phase order
        phaseOrder.ShouldContain("paperwork");

        // Messages from turn 1 (browse) are still present
        messages.ShouldContain(m => m.Role == AgentRole.Tool && m.ToolCallId == "tc_browse",
            "Turn 1 tool results must survive into turn 2 + paperwork");

        // Paperwork phase produced output
        messages.ShouldContain(m =>
            m.Role == AgentRole.Assistant && m.Content != null && m.Content.Contains("ID"),
            "Paperwork phase should have produced a response");

        // No orphaned tool calls in the final history
        FindOrphanedToolCallIds(messages).ShouldBeEmpty(
            "Final message history should have no orphaned tool calls");
    }

    // ═════════════════════════════════════════════════════════════
    //  8. Message count regression guard
    // ═════════════════════════════════════════════════════════════

    [Test]
    public async Task Workflow_appends_exactly_expected_messages_for_text_only()
    {
        var model = new FakeStreamingModel((_, _, ct) =>
            TextChunks(["Hello!"], ct));

        var messages = new List<AgentMessage> { AgentMessage.User("hi") };
        await ConsumeStreamAsync(
            StreamingChatWorkflow.Create("count", model).BuildStream(messages));

        // user + assistant(final) = 2
        messages.Count.ShouldBe(2);
        messages[0].Role.ShouldBe(AgentRole.User);
        messages[1].Role.ShouldBe(AgentRole.Assistant);
    }

    [Test]
    public async Task Workflow_appends_exactly_expected_messages_for_single_tool_round()
    {
        var model = new FakeStreamingModel((call, _, ct) => call switch
        {
            1 => ToolCallChunks("",
                    new AgentToolCall("tc_1", "echo", """{"text":"hi"}"""), ct),
            _ => TextChunks(["Done!"], ct)
        });

        var tools = new ToolKit("test")
            .AddTool("echo", "Echo",
                (string text) => ToolResult.Ok(text), "text", "text");

        var messages = new List<AgentMessage> { AgentMessage.User("echo hi") };
        await ConsumeStreamAsync(
            StreamingChatWorkflow.Create("count", model)
                .WithTools(tools)
                .WithMaxToolRounds(3)
                .BuildStream(messages));

        // user + assistant(tool_calls) + tool(result) + assistant(final) = 4
        messages.Count.ShouldBe(4);
        messages.Select(m => m.Role).ShouldBe([
            AgentRole.User,
            AgentRole.Assistant,
            AgentRole.Tool,
            AgentRole.Assistant
        ]);
    }

    // ═════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════

    static async IAsyncEnumerable<AgentStreamChunk> DoubleToolCallChunks(
        AgentToolCall call1,
        AgentToolCall call2,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        yield return new AgentStreamChunk
        {
            CompletedResponse = new AgentResponse
            {
                ToolCalls = [call1, call2]
            }
        };
    }

    /// <summary>
    /// Returns tool call IDs that have no matching tool result in the message list.
    /// An empty result means the message history is valid for LLM APIs.
    /// </summary>
    static List<string> FindOrphanedToolCallIds(List<AgentMessage> messages)
    {
        var orphans = new List<string>();
        var toolResultIds = messages
            .Where(m => m.Role == AgentRole.Tool && m.ToolCallId is not null)
            .Select(m => m.ToolCallId!)
            .ToHashSet();

        foreach (var msg in messages)
        {
            if (msg.Role == AgentRole.Assistant && msg.ToolCalls is { Count: > 0 })
            {
                foreach (var call in msg.ToolCalls)
                {
                    if (!toolResultIds.Contains(call.Id))
                        orphans.Add(call.Id);
                }
            }
        }
        return orphans;
    }

    /// <summary>
    /// Throws if there are any orphaned tool calls in the message list.
    /// Use as a pre-flight check before passing messages to an LLM.
    /// </summary>
    static void ValidateNoOrphanedToolCalls(List<AgentMessage> messages)
    {
        var orphans = FindOrphanedToolCallIds(messages);
        if (orphans.Count > 0)
            throw new InvalidOperationException(
                $"Orphaned tool call IDs found: {string.Join(", ", orphans)}. " +
                "Every assistant tool_calls entry must have a matching tool result message.");
    }
}
