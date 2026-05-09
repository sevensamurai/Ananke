using System.Text;
using Ananke.Abstractions;
using Ananke.Abstractions.Channels;
using Ananke.Abstractions.Config;
using Ananke.Abstractions.Distributed;
using Ananke.StateMachine;
using Ananke.StateMachine.Channels;
using Ananke.StateMachine.Middleware;
using StateMachineDemo;

// --------------------------------------------------------------------------
//  Ananke — StateMachineDemo
//  Domain: Car Engine IoT — Distributed FSM
//
//  State diagram:
//
//     Parked --[Start]--> Running --[Drive]--> Moving
//       ^                   ^                    |
//       |               [Resume]             [Halt]
//       |                   |                    |
//       +---[Park]--- Idle <---------------------+
//
//  Two "processes" in the same project (via in-memory channels):
//
//    Engine Controller (writer → reader → FSM)
//      Sends transition commands. The StateMachineChannelWorker bridge
//      auto-dispatches to the FSM — no hand-written worker needed.
//
//    Trip Reporter (observer)
//      Subscribes to transition events via the FSM middleware callback.
//      Records trip segments: time running, distance traveled.
//
//  Sections:
//    1. In-memory channels — full lifecycle with typed-action delivery
//    2. Guard condition    — cannot Drive without fuel
//    3. Fault / Reset      — engine fault blocks transitions
//    4. MQTT-driven        — same FSM over MQTT broker (--mqtt flag)
//
//  Usage:
//    dotnet run                     # sections 1–3 (no broker needed)
//    dotnet run -- --mqtt           # sections 1–4 (requires Docker broker)
// --------------------------------------------------------------------------

var enableMqtt = args.Contains("--mqtt", StringComparer.OrdinalIgnoreCase);

Console.OutputEncoding = Encoding.UTF8;
DemoConsole.PrintBanner();

// -- Shared infrastructure ------------------------------------------------
var lockStore = new InMemoryDistributedLock();
var options = new StateMachineOptions { AllowImplicitSelfTransitions = false };

// -- Trip reporter (the "second process") ----------------------------------
//    Observes transitions and records trip segments.
var reporter = new TripReporter();

// -- Engine FSM -----------------------------------------------------------
var engine = new CarEngineStateMachine(lockStore, lockStore, options);
engine.UseMiddleware(new LoggingMiddleware<CarContext, EngineState, EngineTransition>(
    msg => DemoConsole.Dim($"    ~ {msg}")));

// --------------------------------------------------------------------------
//  1. In-memory channels — full lifecycle
// --------------------------------------------------------------------------
DemoConsole.Section("1. In-memory channel — full engine lifecycle");

// Build the channel pair (no MQTT broker needed)
var reader = new InMemoryChannelReader<CarContext, EngineTransition>();
var writer = new InMemoryChannelWriter<EngineTransition>().LinkTo(reader);

// StateMachineChannelWorker: the generic bridge — zero hand-written code
var worker = new StateMachineChannelWorker<CarContext, EngineState, EngineTransition, EngineNotification>(engine)
{
    OnTransition = (ctx, transition, result) =>
    {
        // Report to console
        if (result.Success)
            DemoConsole.Say($"  ✓  {result.PreviousState,-10} --[{transition}]-->  {result.CurrentState,-10}  (car: {ctx.Id})");
        else
            DemoConsole.Say($"  ✗  {result.PreviousState,-10} --[{transition}]-->  blocked     ({result.ErrorMessage})");

        // Feed the trip reporter
        reporter.OnTransition(ctx, transition, result);
    }
};

await reader.ConfigureAsync(new ChannelConfig(), worker);
await writer.ConfigureAsync(new ChannelConfig());

var car1 = new CarContext { Id = "CAR-001", Model = "Model S", FuelLevel = 85.0 };
DemoConsole.Say($"  Car: {car1.Id} ({car1.Model}), fuel: {car1.FuelLevel}%");
Console.WriteLine();

// Parked → Running → Moving → Idle → Parked (full trip)
await Send(writer, car1, EngineTransition.Start);
await Send(writer, car1, EngineTransition.Drive);
await Task.Delay(50); // simulate driving
await Send(writer, car1, EngineTransition.Halt);
await Send(writer, car1, EngineTransition.Park);

Console.WriteLine();
reporter.PrintReport(car1.Id);

// --------------------------------------------------------------------------
//  2. Guard condition — cannot Drive without fuel
// --------------------------------------------------------------------------
DemoConsole.Section("2. Guard condition — fuel required to Drive");

var car2 = new CarContext { Id = "CAR-002", Model = "Civic", FuelLevel = 0.0 };
DemoConsole.Say($"  Car: {car2.Id} ({car2.Model}), fuel: {car2.FuelLevel}%");
Console.WriteLine();

await Send(writer, car2, EngineTransition.Start);   // ✓ engine can start
await Send(writer, car2, EngineTransition.Drive);    // ✗ no fuel — guard blocks

Console.WriteLine();
DemoConsole.Say("  Refueling...");
car2 = car2 with { FuelLevel = 50.0 };
DemoConsole.Say($"  Fuel now: {car2.FuelLevel}%");
await Send(writer, car2, EngineTransition.Drive);    // ✓ guard passes

await Send(writer, car2, EngineTransition.Halt);
await Send(writer, car2, EngineTransition.Park);

// --------------------------------------------------------------------------
//  3. Fault / Reset — engine fault blocks all transitions
// --------------------------------------------------------------------------
DemoConsole.Section("3. Fault / Reset — engine malfunction");

// Fresh machine for fault isolation
var faultLock = new InMemoryDistributedLock();
var faultEngine = new CarEngineStateMachine(faultLock, faultLock, options);
var car3 = new CarContext { Id = "CAR-003", Model = "Corolla", FuelLevel = 70.0 };

DemoConsole.Say($"  Car: {car3.Id}, status: {faultEngine.OperationalStatus}");
Console.WriteLine();

var fault = await faultEngine.FaultAsync(car3, "Check engine light — cylinder misfire detected");
DemoConsole.Say($"  FaultAsync → [{fault.CurrentStatus}]");
DemoConsole.Say($"  Reason: {fault.Reason}");
Console.WriteLine();

DemoConsole.Say("  Attempting Start while Faulted:");
var blocked = await faultEngine.TransitionAsync(car3, EngineTransition.Start);
DemoConsole.Say($"  ✗  {blocked.PreviousState,-10} --[Start]-->  blocked  ({blocked.ErrorMessage})");
Console.WriteLine();

var reset = await faultEngine.ResetAsync(car3, "Cylinder repaired — diagnostics passed");
DemoConsole.Say($"  ResetAsync → [{reset.CurrentStatus}]");
Console.WriteLine();

DemoConsole.Say("  Retry Start after Reset:");
var ok = await faultEngine.TransitionAsync(car3, EngineTransition.Start);
DemoConsole.Say($"  ✓  {ok.PreviousState,-10} --[Start]-->  {ok.CurrentState}");

// --------------------------------------------------------------------------
//  4. MQTT-driven — same FSM, real broker (opt-in via --mqtt)
// --------------------------------------------------------------------------
if (enableMqtt)
    await MqttSection.RunAsync(options);

// -- Cleanup --------------------------------------------------------------
await reader.DisposeAsync();
await writer.DisposeAsync();

Console.WriteLine();
DemoConsole.Say("--------------------------------------------------------------");
DemoConsole.Say("  Done.");
DemoConsole.Say("--------------------------------------------------------------");

// =========================================================================
//  Helpers
// =========================================================================

static async Task Send(InMemoryChannelWriter<EngineTransition> w, CarContext ctx, EngineTransition t)
{
    var result = await w.SendAsync(ctx, t);
    if (!result.Success)
        Console.WriteLine($"  ✗  SendAsync({t}) failed: {result.ErrorMessage}");

    // Small delay so the background processor can drain
    await Task.Delay(20);
}

// =========================================================================
//  Domain
// =========================================================================

enum EngineState { Parked, Running, Idle, Moving }
enum EngineTransition { Start, Drive, Halt, Park, Resume }
enum EngineNotification { OverSpeed }

// -- Context — plain IBaseContext, no IMqttContext, works everywhere -------

sealed record CarContext : IBaseContext
{
    public required string Id { get; init; }
    public string Model { get; init; } = string.Empty;
    public double FuelLevel { get; init; }
}

