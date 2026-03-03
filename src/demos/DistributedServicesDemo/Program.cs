using System.Text;
using Ananke.Abstractions.Channels;
using Ananke.Abstractions.Config;
using Ananke.Abstractions.Distributed;
using Ananke.Bridge;
using Ananke.MQTT;
using Ananke.Orchestration;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Jobs;
using Ananke.Orchestration.Memory;
using Ananke.Orchestration.Streaming;
using Ananke.Redis;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//  Ananke — Distributed Services Demo
//
//  A support-ticket triage workflow that shows five Ananke features
//  working together in one pipeline:
//
//    1. Workflow orchestration   — Workflow<T> graph-as-code builder
//    2. Agent-to-agent handoff   — Handoff.To<>() over MQTT or in-memory
//    3. Conversation memory      — IConversationMemory (Redis or in-memory)
//    4. State machine lifecycle  — AbstractStateMachine with distributed lock
//    5. Bridge convenience layer — .StateMachineJob() wires FSM transitions into
//                                  the workflow with full type inference
//
//  Modes:
//    dotnet run                  → Triage workflow (single process)
//    dotnet run -- --specialist  → Specialist service (MQTT listener)
//
//  Infrastructure (appsettings.json):
//    Mqtt:Host  empty → InMemoryHandoffChannel    | set → MqttHandoffChannel
//    Redis:Host empty → InMemoryConversationMemory | set → RedisConversationMemory
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Console.OutputEncoding = Encoding.UTF8;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var mqttHost = config["Mqtt:Host"];
var mqttPort = int.TryParse(config["Mqtt:Port"], out var mp) ? mp : 1883;
var mqttNamespace = config["Mqtt:Namespace"] ?? "handoff";
var useMqtt = !string.IsNullOrWhiteSpace(mqttHost);

var redisHost = config["Redis:Host"];
var redisPort = int.TryParse(config["Redis:Port"], out var rp) ? rp : 6379;
var useRedis = !string.IsNullOrWhiteSpace(redisHost);

var isSpecialist = args.Contains("--specialist", StringComparer.OrdinalIgnoreCase);

if (isSpecialist)
    await RunSpecialistAsync();
else
    await RunTriageAsync();

// ═══════════════════════════════════════════════════════════════════
// Triage Workflow (sender / orchestrator)
// ═══════════════════════════════════════════════════════════════════

async Task RunTriageAsync()
{
    Console.WriteLine("━━━ Ananke — Triage Workflow ━━━");
    Console.WriteLine($"  Handoff: {(useMqtt ? $"MQTT ({mqttHost}:{mqttPort})" : "In-Memory")}");
    Console.WriteLine($"  Memory:  {(useRedis ? $"Redis ({redisHost}:{redisPort})" : "In-Memory")}");
    Console.WriteLine();

    // ── Conversation memory ──────────────────────────────────────────
    IConversationMemory memory;
    ConnectionMultiplexer? redis = null;

    if (useRedis)
    {
        redis = await ConnectionMultiplexer.ConnectAsync($"{redisHost}:{redisPort}");
        memory = new RedisConversationMemory(redis, ttl: TimeSpan.FromHours(1));
        Console.WriteLine("  ✓ Connected to Redis");
    }
    else
    {
        memory = new InMemoryConversationMemory(ttl: TimeSpan.FromHours(1));
    }

    // ── Handoff channel ──────────────────────────────────────────────
    IHandoffChannel channel;

    if (useMqtt)
    {
        var mqtt = new MqttHandoffChannel();
        var connected = await mqtt.ConfigureAsync(new ChannelConfig
        {
            Host = mqttHost!,
            Port = mqttPort,
            Namespace = mqttNamespace
        });
        if (!connected)
        {
            Console.WriteLine("  ✗ Failed to connect to MQTT broker. Exiting.");
            return;
        }
        Console.WriteLine("  ✓ Connected to MQTT broker");
        Console.WriteLine("  ⚠ Make sure the specialist is running: dotnet run -- --specialist");
        Console.WriteLine();
        channel = mqtt;
    }
    else
    {
        var inMemory = new InMemoryHandoffChannel();

        // In-memory specialist handler — simulates a second process.
        // No Console.WriteLine here: all output goes through the
        // StreamAsync consumer below to avoid interleaved lines.
        inMemory.RegisterHandler<TicketHandoff, SpecialistResult>(
            "specialist-queue",
            async ticket =>
            {
                await Task.Delay(800);
                return new SpecialistResult
                {
                    Resolution = ResolveTicket(ticket),
                    HandledBy = "specialist-agent-1 (in-memory)"
                };
            });

        channel = inMemory;
    }

    // ── Ticket lifecycle FSM ─────────────────────────────────────────
    //  A simple state machine that tracks each ticket's lifecycle:
    //
    //    New ──[BeginTriage]──► Triaging ──[Resolve]──► Resolved ──[Close]──► Closed
    //
    //  The FSM is defined once; each ticket gets independent state via
    //  its own context ID (parsed from the ticket ID).  The Bridge
    //  convenience layer (.StateMachineJob) wires the transitions into the
    //  workflow — no manual glue code needed.

    var lifecycle = new TicketLifecycleMachine(new InMemoryDistributedLock());
    Console.WriteLine("  FSM:     Ticket lifecycle (New → Triaging → Resolved → Closed)");
    Console.WriteLine();

    // Helper: maps a TicketState to the FSM context for that ticket.
    // Each ticket ID like "TK-001" produces a unique long (1, 2, 3…)
    // so the FSM tracks per-ticket state independently.
    TicketLifecycleContext FsmContext(TicketState s) =>
        new(long.Parse(s.TicketId[3..]));

    // ── Build the triage workflow ────────────────────────────────────
    //
    //  The pipeline runs each ticket through this graph:
    //
    //    classify → fsm_triage → [decide] → escalate    ─┐
    //                                      → auto_resolve ─┤
    //                                                     │
    //    fsm_resolve ─────────────────────────────────────┘
    //        │
    //      notify → fsm_close → End
    //
    //  Jobs prefixed with "fsm_" are Bridge jobs that fire a state
    //  machine transition.  Business logic lives in the other jobs.
    //
    //  NOTE: Job lambdas must NOT call Console.WriteLine directly.
    //  StreamAsync runs jobs on a background thread; writing to Console
    //  from both the job thread and the consumer loop causes interleaved
    //  output.  Instead, all output is printed from the stream consumer
    //  below, which inspects JobCompleted.State for diagnostics.

    var workflow = new Workflow<TicketState>("support-triage")

        // ── Business jobs ────────────────────────────────────────────

        .Job("classify", async (state, ct) =>
        {
            await Task.Delay(300, ct);

            var history = await memory.GetHistoryAsync(state.CustomerId, ct);
            var priorCount = history.Count > 0
                ? history.Count(m => m.Role == AgentRole.Assistant)
                : 0;

            var severity = state.Description.Contains("down", StringComparison.OrdinalIgnoreCase) ? 9
                : state.Description.Contains("slow", StringComparison.OrdinalIgnoreCase) ? 6
                : state.Description.Contains("question", StringComparison.OrdinalIgnoreCase) ? 2
                : 4;

            return state with
            {
                Severity = severity,
                Category = severity >= 5 ? "escalate" : "self-service",
                PriorInteractions = priorCount
            };
        })

        .Job("auto_resolve", async (state, ct) =>
        {
            await Task.Delay(200, ct);
            return state with
            {
                Resolution = $"Auto-resolved: We've found a help article for \"{state.Description}\".",
                ResolvedBy = "auto-responder"
            };
        })

        .Job("escalate", Handoff.To<TicketState, TicketHandoff, SpecialistResult>(
            "specialist-queue",
            channel,
            state => new TicketHandoff
            {
                TicketId = state.TicketId,
                Summary = state.Description,
                Severity = state.Severity
            },
            (state, response) => state with
            {
                Resolution = response.Resolution,
                ResolvedBy = response.HandledBy
            },
            timeout: TimeSpan.FromSeconds(30)))

        .Job("notify", async (state, ct) =>
        {
            await Task.Delay(100, ct);

            await memory.AddAsync(state.CustomerId, [
                AgentMessage.User($"[{state.TicketId}] {state.Description}"),
                AgentMessage.Assistant($"[{state.ResolvedBy}] {state.Resolution}")
            ], ct);

            return state with { Notified = true };
        })

        // ── FSM bridge jobs ─────────────────────────────────────────
        //  .StateMachineJob() is a Bridge extension method.  It wraps
        //  StateMachineTriggerJob under the hood, but the compiler
        //  infers all 5 generic type parameters from the lambdas —
        //  so the call site stays clean.
        //
        //  Each call takes:
        //    1. job name        — referenced by the routing section below
        //    2. state machine   — the FSM instance to transition
        //    3. contextSelector — which ticket ID we're acting on
        //    4. transitionSelector — which FSM action to fire
        //    5. resultMapper (optional) — fold FSM result into workflow state
        .StateMachineJob("fsm_triage", lifecycle,
            FsmContext,
            _ => LifecycleAction.BeginTriage,
            (s, r) => s with { FsmState = r.CurrentState.ToString() })

        .StateMachineJob("fsm_resolve", lifecycle,
            FsmContext,
            _ => LifecycleAction.Resolve,
            (s, r) => s with { FsmState = r.CurrentState.ToString() })

        .StateMachineJob("fsm_close", lifecycle,
            FsmContext,
            _ => LifecycleAction.Close,
            (s, r) => s with { FsmState = r.CurrentState.ToString() })

        // ── Routing — connects the jobs into the graph shown above ──

        .Then("classify", "fsm_triage")
        .Then("fsm_triage", Workflow.Decide<TicketState>(s =>
            s.Severity >= 5 ? "escalate" : "auto_resolve"))
        .Then("escalate", "fsm_resolve")
        .Then("auto_resolve", "fsm_resolve")
        .Then("fsm_resolve", "notify")
        .Then("notify", "fsm_close")
        .Then("fsm_close", Workflow.End);

    // ── Run tickets ──────────────────────────────────────────────────
    //  TK-001 and TK-003 are from the same customer (CUST-42).
    //  When TK-003 runs, memory recalls the TK-001 resolution.
    //
    //  ALL output is printed here from the stream consumer — never
    //  from inside a job lambda — so lines never interleave.

    var tickets = new[]
    {
        new TicketState { TicketId = "TK-001", CustomerId = "CUST-42", Description = "Production database is down since 3am" },
        new TicketState { TicketId = "TK-002", CustomerId = "CUST-99", Description = "Quick question about billing" },
        new TicketState { TicketId = "TK-003", CustomerId = "CUST-42", Description = "Dashboard loading is extremely slow" },
    };

    foreach (var ticket in tickets)
    {
        Console.WriteLine($"┌─ Ticket {ticket.TicketId} (customer {ticket.CustomerId}): \"{ticket.Description}\"");
        Console.WriteLine("│");

        await foreach (var evt in workflow.StreamAsync(ticket))
        {
            switch (evt)
            {
                case JobStarted<TicketState> js:
                    Console.WriteLine($"│  ▶ {js.JobName}");
                    break;

                case JobCompleted<TicketState> jc:
                    var detail = DescribeJob(jc);
                    var suffix = string.IsNullOrEmpty(detail) ? "" : $"  → {detail}";
                    Console.WriteLine($"│  ✓ {jc.JobName} ({jc.Duration.TotalMilliseconds:F0}ms){suffix}");
                    break;

                case WorkflowCompleted<TicketState> wc:
                    var s = wc.Result.FinalState;
                    Console.WriteLine("│");
                    Console.WriteLine($"│  Resolution:    {s.Resolution}");
                    Console.WriteLine($"│  Handled by:    {s.ResolvedBy}");
                    Console.WriteLine($"│  Prior tickets: {s.PriorInteractions}");
                    Console.WriteLine($"│  Notified:      {s.Notified}");
                    Console.WriteLine($"│  FSM state:     {s.FsmState}");
                    break;

                case WorkflowFaulted<TicketState> wf:
                    Console.WriteLine($"│  ✗ FAILED: {wf.Exception.Message}");
                    break;
            }
        }

        Console.WriteLine("└─ Done");
        Console.WriteLine();
    }

    Console.WriteLine("━━━ All tickets processed ━━━");

    if (channel is IAsyncDisposable disposableChannel)
        await disposableChannel.DisposeAsync();
    if (redis is not null)
        await redis.DisposeAsync();
}

// ═══════════════════════════════════════════════════════════════════
// Specialist Service (responder — runs as second process)
// ═══════════════════════════════════════════════════════════════════

async Task RunSpecialistAsync()
{
    Console.WriteLine("━━━ Ananke — Specialist Service ━━━");

    if (!useMqtt)
    {
        Console.WriteLine("  ✗ MQTT is not configured in appsettings.json.");
        Console.WriteLine("  Set Mqtt:Host to your broker address and try again.");
        return;
    }

    Console.WriteLine($"  Connecting to MQTT broker at {mqttHost}:{mqttPort}...");

    await using var channel = new MqttHandoffChannel();
    var connected = await channel.ConfigureAsync(new ChannelConfig
    {
        Host = mqttHost!,
        Port = mqttPort,
        Namespace = mqttNamespace
    });

    if (!connected)
    {
        Console.WriteLine("  ✗ Failed to connect to MQTT broker. Exiting.");
        return;
    }

    Console.WriteLine("  ✓ Connected to MQTT broker");
    Console.WriteLine("  Listening for handoff requests on 'specialist-queue'...");
    Console.WriteLine("  Press Ctrl+C to stop.");
    Console.WriteLine();

    // Specialist mode is a standalone process — no StreamAsync consumer,
    // so Console.WriteLine is safe here (single thread of control).
    await channel.SubscribeAsync<TicketHandoff, SpecialistResult>(
        "specialist-queue",
        async ticket =>
        {
            Console.WriteLine($"  🔧 Received ticket {ticket.TicketId}: \"{ticket.Summary}\" (severity {ticket.Severity})");
            await Task.Delay(800);

            var resolution = ResolveTicket(ticket);
            Console.WriteLine($"  ✅ Resolved: {Truncate(resolution, 80)}");
            Console.WriteLine();

            return new SpecialistResult { Resolution = resolution, HandledBy = $"specialist-agent-1 (MQTT/{mqttHost})" };
        });

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    try
    {
        await Task.Delay(Timeout.Infinite, cts.Token);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine();
        Console.WriteLine("  Specialist service stopped.");
    }
}

// ═══════════════════════════════════════════════════════════════════
// Shared helpers
// ═══════════════════════════════════════════════════════════════════

static string ResolveTicket(TicketHandoff ticket) => ticket.Severity switch
{
    >= 8 => $"CRITICAL: Immediate escalation applied — {ticket.Summary} resolved via emergency protocol",
    >= 5 => $"HIGH: Investigation complete — root cause identified for: {ticket.Summary}",
    _ => $"STANDARD: Resolved — {ticket.Summary}"
};

static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";

// Produces a brief description for each completed job by inspecting the
// post-job state.  Printed alongside the "✓" line in the stream consumer
// so all output comes from a single thread — no interleaving.
static string DescribeJob(JobCompleted<TicketState> jc) => jc.JobName switch
{
    "classify" => $"severity {jc.State.Severity}, {jc.State.Category}"
        + (jc.State.PriorInteractions > 0 ? $", 🧠 {jc.State.PriorInteractions} prior" : ""),
    "escalate" or "auto_resolve" => Truncate(jc.State.Resolution ?? "", 70),
    "notify" => "🧠 saved to memory",
    _ when jc.JobName.StartsWith("fsm_") => jc.State.FsmState ?? "",
    _ => ""
};
