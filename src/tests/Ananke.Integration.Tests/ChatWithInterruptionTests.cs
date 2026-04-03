using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Tools;
using Ananke.StateMachine;
using Shouldly;

using SM = Ananke.StateMachine.StateMachine;

namespace Ananke.Integration.Tests;

/// <summary>
/// Integration tests exercising the state-machine + streaming-workflow + interrupt
/// pattern used by the PetAdoptionDemo, without any HTTP/ASP.NET dependencies.
/// </summary>
[TestFixture]
public class ChatWithInterruptionTests
{
    // ── Protocol (mirrors AdoptionMachine) ───────────────────────

    enum Phase { Searching, Interrupted }
    enum Action { Start, Interrupt, Resume }

    static StateMachine<Phase, Action> CreateMachine() =>
        SM.Create<Phase, Action>(Phase.Searching, b => b
            .From(Phase.Searching).On(Action.Start).To(Phase.Searching)
            .From(Phase.Searching).On(Action.Interrupt).ToInterrupt(Phase.Interrupted)
            .From(Phase.Interrupted).On(Action.Resume).ToResume());

    // ── Thread-safe event log ────────────────────────────────────

    sealed class EventLog
    {
        private readonly ConcurrentQueue<string> _events = new();
        public void Add(string evt) => _events.Enqueue(evt);
        public List<string> Snapshot() => [.. _events];
    }

    // ── Fake streaming model ─────────────────────────────────────

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

    // ── Stream consumer (mirrors AdoptionSession.StreamAsync) ────

    static async Task ConsumeStreamAsync(EventLog log, IAsyncEnumerable<ChatSessionEvent> events)
    {
        await foreach (var evt in events)
        {
            switch (evt)
            {
                case TextDeltaEvent d: log.Add($"delta:{d.Text}"); break;
                case ToolCallEvent t: log.Add($"tool_call:{t.Name}"); break;
                case ToolResultEvent t: log.Add($"tool_result:{t.Name}"); break;
                case CompletedEvent: log.Add("completed"); break;
                case ErrorEvent e: log.Add($"error:{e.Message}"); break;
            }
        }
    }

    // ── Phase registration (mirrors SearchPhase + InterruptPhase) ─

    static void RegisterSearchPhase(
        StateMachine<Phase, Action> machine,
        EventLog log,
        List<AgentMessage> messages,
        IStreamingAgentModel model,
        ToolKit? tools = null)
    {
        machine.OnEnter(Phase.Searching, async ct =>
        {
            log.Add("phase:searching");

            var builder = StreamingChatWorkflow.Create("search", model)
                .WithSystemPrompt("You are a test assistant.");

            if (tools is not null)
                builder = builder.WithTools(tools).WithMaxToolRounds(5);

            await ConsumeStreamAsync(log, builder.BuildStream(messages, ct));
        });
    }

    static void RegisterInterruptPhase(
        StateMachine<Phase, Action> machine,
        EventLog log,
        List<AgentMessage> messages)
    {
        AgentMessage? pending = null;

        machine.OnInterrupt(async (payload, _) =>
        {
            pending = payload as AgentMessage;
            log.Add("interrupted");
            await Task.CompletedTask;
        });

        machine.OnEnter(Phase.Interrupted, async _ =>
        {
            messages.PatchOrphanedToolCalls();

            if (pending is not null)
            {
                messages.Add(pending);
                log.Add("interrupt_msg_added");
            }

            pending = null;
            log.Add("resumed");
            await machine.FireAsync(Action.Resume);
        });
    }

    // ── Endpoint loop (mirrors ChatEndpoint while-loop) ──────────

    static async Task RunEndpointLoop(StateMachine<Phase, Action> machine)
    {
        var result = await machine.FireAsync(Action.Start);
        result.Success.ShouldBeTrue("FireAsync(Start) should succeed");

        while (true)
        {
            var work = machine.CurrentWork;
            if (work is null)
            {
                // Brief retry — survives the race where CancelCurrentWork has
                // nulled _currentWork but StartStateWork hasn't run yet.
                await Task.Delay(150);
                work = machine.CurrentWork;
                if (work is null) break;
            }

            try { await work; }
            catch (OperationCanceledException) { }

            // The task completed (or was cancelled by an interrupt).
            // If a transition started new work, CurrentWork differs — keep looping.
            // Otherwise this turn is done.
            if (machine.CurrentWork == work) break;
        }
    }

    // ── Chunk generators ─────────────────────────────────────────

    static async IAsyncEnumerable<AgentStreamChunk> TextChunks(
        string[] parts,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var full = string.Join("", parts);
        foreach (var part in parts)
        {
            yield return new AgentStreamChunk { TextDelta = part };
            await Task.Delay(10, ct);
        }
        yield return new AgentStreamChunk
        {
            CompletedResponse = new AgentResponse { Text = full }
        };
    }

    static async IAsyncEnumerable<AgentStreamChunk> ToolCallChunks(
        string preamble,
        AgentToolCall toolCall,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(preamble))
        {
            yield return new AgentStreamChunk { TextDelta = preamble };
            await Task.Delay(10, ct);
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

    // ═════════════════════════════════════════════════════════════
    //  Tests
    // ═════════════════════════════════════════════════════════════

    [Test, CancelAfter(3_000)]
    public async Task Normal_chat_completes_without_interruption()
    {
        var model = new FakeStreamingModel((_, _, ct) =>
            TextChunks(["Hello! ", "How can I ", "help you?"], ct));

        var machine = CreateMachine();
        var log = new EventLog();
        var messages = new List<AgentMessage> { AgentMessage.User("hi") };

        RegisterSearchPhase(machine, log, messages, model);

        await RunEndpointLoop(machine);

        var events = log.Snapshot();
        events.ShouldContain("phase:searching");
        events.ShouldContain("delta:Hello! ");
        events.ShouldContain("delta:How can I ");
        events.ShouldContain("delta:help you?");
        events.ShouldContain("completed");

        machine.CurrentState.ShouldBe(Phase.Searching);
    }

    [Test, CancelAfter(5_000)]
    public async Task Interrupt_during_tool_execution_patches_orphans_and_resumes()
    {
        // Signal so the test can fire the interrupt at the right moment
        var toolStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var model = new FakeStreamingModel((call, _, ct) => call switch
        {
            1 => ToolCallChunks("Searching...",
                    new AgentToolCall("tc_1", "slow_tool", """{"q":"pets"}"""), ct),
            _ => TextChunks(["Here are ", "the results!"], ct)
        });

        var tools = new ToolKit("test")
            .AddTool(new ToolDefinition
            {
                Name = "slow_tool",
                Description = "Simulates a slow search",
                Parameters = [new ToolParameter("q", "query")],
                Execute = async (_, ct) =>
                {
                    toolStarted.TrySetResult();
                    await Task.Delay(500, ct);
                    return "should not reach here";
                }
            });

        var machine = CreateMachine();
        var log = new EventLog();
        var messages = new List<AgentMessage> { AgentMessage.User("find kid friendly pets") };

        RegisterSearchPhase(machine, log, messages, model, tools);
        RegisterInterruptPhase(machine, log, messages);

        // Run the endpoint loop on a background thread
        var loopTask = Task.Run(() => RunEndpointLoop(machine));

        // Wait for the tool to start executing, then fire the interrupt
        await toolStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(20);

        var interruptResult = await machine.FireAsync(
            Action.Interrupt, AgentMessage.User("also good for granny"));
        interruptResult.Success.ShouldBeTrue("Interrupt should be accepted");

        // Wait for the loop to complete (resumed search finishes)
        await loopTask.WaitAsync(TimeSpan.FromSeconds(2));

        // ── Assert events
        var events = log.Snapshot();

        events[0].ShouldBe("phase:searching");

        // Interrupt cycle
        events.ShouldContain("interrupted");
        events.ShouldContain("interrupt_msg_added");
        events.ShouldContain("resumed");

        // Resumed search re-entered and produced output
        var searchCount = events.Count(e => e == "phase:searching");
        searchCount.ShouldBe(2, "Search phase should be entered twice (initial + resumed)");

        events.ShouldContain("delta:Here are ");
        events.ShouldContain("delta:the results!");

        // ── Assert message history ───────────────────────────────

        // Orphaned tool call was patched with synthetic result
        messages.ShouldContain(m =>
            m.Role == AgentRole.Tool &&
            m.ToolCallId == "tc_1" &&
            m.Content != null &&
            m.Content.Contains("[interrupted"));

        // Interrupt message was appended
        messages.ShouldContain(m =>
            m.Role == AgentRole.User &&
            m.Content == "also good for granny");

        // Machine returned to Searching (not stuck in Interrupted)
        machine.CurrentState.ShouldBe(Phase.Searching);
        machine.IsInterrupted.ShouldBeFalse();
    }

    [Test, CancelAfter(5_000)]
    public async Task Interrupt_during_text_streaming_resumes_and_completes()
    {
        // Model streams text slowly on call 1 so the interrupt can fire mid-stream
        var streamingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var model = new FakeStreamingModel((call, _, ct) => call switch
        {
            1 => SlowTextChunks(streamingStarted, ct),
            _ => TextChunks(["Updated ", "answer!"], ct)
        });

        var machine = CreateMachine();
        var log = new EventLog();
        var messages = new List<AgentMessage> { AgentMessage.User("hello") };

        RegisterSearchPhase(machine, log, messages, model);
        RegisterInterruptPhase(machine, log, messages);

        var loopTask = Task.Run(() => RunEndpointLoop(machine));

        await streamingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(20);

        await machine.FireAsync(Action.Interrupt, AgentMessage.User("wait, change that"));

        await loopTask.WaitAsync(TimeSpan.FromSeconds(2));

        var events = log.Snapshot();
        events.ShouldContain("interrupted");
        events.ShouldContain("resumed");
        events.ShouldContain("delta:Updated ");
        events.ShouldContain("delta:answer!");

        machine.CurrentState.ShouldBe(Phase.Searching);
        machine.IsInterrupted.ShouldBeFalse();
    }

    static async IAsyncEnumerable<AgentStreamChunk> SlowTextChunks(
        TaskCompletionSource signal,
        [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new AgentStreamChunk { TextDelta = "Working " };
        signal.TrySetResult();

        // Just long enough for the interrupt to arrive
        await Task.Delay(500, ct);
        yield return new AgentStreamChunk { TextDelta = "done!" };
        yield return new AgentStreamChunk
        {
            CompletedResponse = new AgentResponse { Text = "Working done!" }
        };
    }

    // ── PatchOrphanedToolCalls unit tests ────────────────────────

    [Test]
    public void Patch_adds_synthetic_results_for_unanswered_tool_calls()
    {
        var messages = new List<AgentMessage>
        {
            AgentMessage.User("hello"),
            AgentMessage.Assistant("", [
                new AgentToolCall("tc_1", "tool_a", "{}"),
                new AgentToolCall("tc_2", "tool_b", "{}")
            ]),
            AgentMessage.ToolResult("tc_1", "result A")
            // tc_2 was never answered (interrupted)
        };

        messages.PatchOrphanedToolCalls();

        messages.Count.ShouldBe(4);
        messages[3].Role.ShouldBe(AgentRole.Tool);
        messages[3].ToolCallId.ShouldBe("tc_2");
        messages[3].Content!.ShouldContain("[interrupted");
    }

    [Test]
    public void Patch_is_noop_when_all_tool_calls_are_answered()
    {
        var messages = new List<AgentMessage>
        {
            AgentMessage.User("hello"),
            AgentMessage.Assistant("", [new AgentToolCall("tc_1", "tool_a", "{}")]),
            AgentMessage.ToolResult("tc_1", "result A")
        };

        messages.PatchOrphanedToolCalls();

        messages.Count.ShouldBe(3);
    }

    [Test]
    public void Patch_handles_all_calls_orphaned()
    {
        var messages = new List<AgentMessage>
        {
            AgentMessage.User("hello"),
            AgentMessage.Assistant("", [
                new AgentToolCall("tc_1", "tool_a", "{}"),
                new AgentToolCall("tc_2", "tool_b", "{}")
            ])
            // Neither answered
        };

        messages.PatchOrphanedToolCalls();

        messages.Count.ShouldBe(4);
        messages[2].ToolCallId.ShouldBe("tc_1");
        messages[3].ToolCallId.ShouldBe("tc_2");
        messages[2].Content!.ShouldContain("[interrupted");
        messages[3].Content!.ShouldContain("[interrupted");
    }

    [Test]
    public void Patch_is_noop_when_no_tool_calls_in_history()
    {
        var messages = new List<AgentMessage>
        {
            AgentMessage.User("hello"),
            AgentMessage.Assistant("Hi there!")
        };

        messages.PatchOrphanedToolCalls();

        messages.Count.ShouldBe(2);
    }
}
