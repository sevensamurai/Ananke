using System.Text;
using Ananke.Abstractions;
using Ananke.Abstractions.Distributed;
using Ananke.StateMachine;
using Ananke.StateMachine.Builder;
using Ananke.StateMachine.Middleware;

// --------------------------------------------------------------------------
//  Ananke — StateMachineDemo
//  Domain: Support Ticket Lifecycle
//
//  State diagram:
//
//     +----------------------------------------------------------+
//     ¦                                                          ¦
//     ¦   Open --[Assign]--? InProgress --[Resolve]--? Resolved ¦
//     ¦    ?                                            ¦    ¦   ¦
//     ¦    ¦                                        [Reopen] ¦   ¦
//     ¦    +--------------------------------------------+ [Close] ¦
//     ¦                                                       ¦   ¦
//     ¦                                                    Closed ¦
//     +----------------------------------------------------------+
//
//  Transitions defined:
//    Open       --[Assign]-->  InProgress   (always valid)
//    InProgress --[Resolve]--> Resolved     (guard: ResolutionNote must be set)
//    Resolved   --[Reopen]-->  Open         (allowed — re-raise a ticket)
//    Resolved   --[Close]-->   Closed       (terminal — archives the ticket)
//
//  NOT defined (invalid — the machine will reject these):
//    Open       --[Resolve]    (must Assign first)
//    InProgress --[Close]      (must Resolve before Close)
//    Closed     --[Reopen]     (Closed is terminal)
//
//  Sections:
//    1. Happy path          — full lifecycle with lifecycle hooks
//    2. Invalid transitions — three different rejection scenarios
//    3. Guard condition     — Resolve blocked, then unblocked by note
//    4. Fault / Reset       — circuit-breaker that blocks all transitions
// --------------------------------------------------------------------------

Console.OutputEncoding = Encoding.UTF8;
PrintBanner();

// -- Shared machine (sections 1–3) ----------------------------------------
//    One machine instance handles multiple independent tickets via context IDs.
//    The middleware intercepts every transition attempt — success or failure.
var lock1 = new InMemoryDistributedLock();
var machine = new TicketMachine(lock1, lock1,
    new StateMachineOptions { AllowImplicitSelfTransitions = false });
machine.UseMiddleware(new LoggingMiddleware<TicketContext, TicketState, TicketTransition>(
    msg => Dim($"    ~ {msg}")));

// --------------------------------------------------------------------------
//  1. Happy path — full lifecycle with hooks and after-action
// --------------------------------------------------------------------------
Section("1. Happy path — full ticket lifecycle");

var t1 = new TicketContext("1") { Title = "Login page returns HTTP 500" };
Say($"  Ticket #{t1.Id}: \"{t1.Title}\"  [starts: {machine.CurrentState}]");
Console.WriteLine();

// Open ? InProgress  (OnEnter InProgress fires)
await Do(machine, t1, TicketTransition.Assign);

// InProgress ? Resolved  (guard: ResolutionNote must be non-empty; OnExit fires)
machine.ResolutionNote = "Fixed null-reference in AuthController.LoginAsync";
Say($"  Note set: \"{machine.ResolutionNote}\"");
await Do(machine, t1, TicketTransition.Resolve);

// Resolved ? Closed
await Do(machine, t1, TicketTransition.Close);

// --------------------------------------------------------------------------
//  2. Invalid transitions — three rejection scenarios
// --------------------------------------------------------------------------
Section("2. Invalid transitions");

// 2a — Cannot Resolve before Assigning
Say("  2a. Open --[Resolve]--> ?  (no path defined — must Assign first)");
var t2a = new TicketContext("10") { Title = "Payment timeout" };
await Do(machine, t2a, TicketTransition.Resolve);   // ?
Console.WriteLine();

// 2b — Cannot jump from InProgress directly to Closed
Say("  2b. InProgress --[Close]--> ?  (must Resolve before Close)");
var t2b = new TicketContext("11") { Title = "Dark mode flicker" };
await Do(machine, t2b, TicketTransition.Assign);    // ? Open ? InProgress (setup)
await Do(machine, t2b, TicketTransition.Close);     // ?
Console.WriteLine();

// 2c — Closed is terminal; cannot reopen once archived
Say("  2c. Closed --[Reopen]--> ?  (Closed is a terminal state)");
var t2c = new TicketContext("12") { Title = "Cache invalidation bug" };
machine.ResolutionNote = "Cache key normalised to lowercase";
await Do(machine, t2c, TicketTransition.Assign);    // ? setup
await Do(machine, t2c, TicketTransition.Resolve);   // ? setup
await Do(machine, t2c, TicketTransition.Close);     // ? setup — now Closed
await Do(machine, t2c, TicketTransition.Reopen);    // ?

// --------------------------------------------------------------------------
//  3. Guard condition — Resolve requires a non-empty ResolutionNote
// --------------------------------------------------------------------------
Section("3. Guard condition — ResolutionNote required before Resolve");

machine.ResolutionNote = null;   // clear any note left from section 2

var t3 = new TicketContext("20") { Title = "Export button does nothing" };
Say($"  Ticket #{t3.Id}: \"{t3.Title}\"");
Console.WriteLine();

await Do(machine, t3, TicketTransition.Assign);     // ? Open ? InProgress

// Guard blocks the transition — note is empty
Say("  ResolutionNote is null — guard will block Resolve:");
await Do(machine, t3, TicketTransition.Resolve);    // ? guard fails
Console.WriteLine();

// Set the note and retry — guard now passes
machine.ResolutionNote = "JS event listener was missing after last bundle update";
Say($"  Note set: \"{machine.ResolutionNote}\"");
Say("  Retry — guard now passes:");
await Do(machine, t3, TicketTransition.Resolve);    // ?

// --------------------------------------------------------------------------
//  4. OperationalStatus — Fault and Reset circuit breaker
// --------------------------------------------------------------------------
Section("4. OperationalStatus — Fault blocks all transitions until Reset");

// Fresh isolated machine — fault/reset applies to the whole machine instance
var lock2 = new InMemoryDistributedLock();
var guardedMachine = new TicketMachine(lock2, lock2,
    new StateMachineOptions { AllowImplicitSelfTransitions = false });
var t4 = new TicketContext("30") { Title = "Database migration failure" };

Say($"  Ticket #{t4.Id}: \"{t4.Title}\"");
Say($"  Operational status: {guardedMachine.OperationalStatus}");
Console.WriteLine();

// Fault — simulates a critical incident requiring manual intervention
var fault = await guardedMachine.FaultAsync(t4, "Schema migration rolled back — manual DBA intervention required");
Say($"  FaultAsync ? [{fault.CurrentStatus}]");
Say($"  Reason: {fault.Reason}");
Console.WriteLine();

// All transitions are now blocked regardless of whether they would otherwise be valid
Say("  Attempting Assign while Faulted:");
await Do(guardedMachine, t4, TicketTransition.Assign);  // ? blocked
Console.WriteLine();

// Reset — operator clears the fault after remediation
var reset = await guardedMachine.ResetAsync(t4, "Migration re-applied by DBA — system verified healthy");
Say($"  ResetAsync ? [{reset.CurrentStatus}]");
Say($"  Reason: {reset.Reason}");
Console.WriteLine();

// Normal transitions resume after reset
Say("  Retry Assign after Reset:");
await Do(guardedMachine, t4, TicketTransition.Assign);  // ?

Console.WriteLine();
Say("--------------------------------------------------------------");
Say("  Done.");
Say("--------------------------------------------------------------");

// -- Helpers --------------------------------------------------------------

static async Task Do(TicketMachine m, TicketContext ctx, TicketTransition t)
{
    var r = await m.TransitionAsync(ctx, t);
    if (r.Success)
        Console.WriteLine($"  ?  {r.PreviousState,-12} --[{t}]--?  {r.CurrentState}");
    else
        Console.WriteLine($"  ?  {r.PreviousState,-12} --[{t}]--?  blocked  ({r.ErrorMessage})");
}

static void Section(string title)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"-- {title}");
    Console.ResetColor();
    Console.WriteLine();
}

static void Say(string msg) => Console.WriteLine(msg);

static void Dim(string msg)
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine(msg);
    Console.ResetColor();
}

static void PrintBanner()
{
    Console.WriteLine("--------------------------------------------------------------");
    Console.WriteLine("  Ananke — StateMachineDemo  |  Support Ticket Lifecycle");
    Console.WriteLine();
    Console.WriteLine("   Open --[Assign]--? InProgress --[Resolve]--? Resolved");
    Console.WriteLine("    ?                                            ¦    ¦");
    Console.WriteLine("    ¦                                        [Reopen] ¦");
    Console.WriteLine("    +--------------------------------------------+ [Close]");
    Console.WriteLine("                                                       ¦");
    Console.WriteLine("                                                    Closed");
    Console.WriteLine("--------------------------------------------------------------");
}

// -- Domain enums ---------------------------------------------------------

enum TicketState      { Open, InProgress, Resolved, Closed }
enum TicketTransition { Assign, Resolve, Reopen, Close }
enum TicketNotification { Escalated }

// -- Context --------------------------------------------------------------

sealed class TicketContext(string id) : IBaseContext
{
    public string Id { get; } = id;
    public string Title { get; init; } = string.Empty;
}

// -- State machine ---------------------------------------------------------

sealed class TicketMachine(IDistributedLock locker, IKeyValueDataAdapter store, StateMachineOptions? options = null)
    : AbstractStateMachine<TicketContext, TicketState, TicketTransition, TicketNotification>(
        TicketState.Open, locker, store, options)
{
    /// <summary>
    /// Guard condition for <see cref="TicketTransition.Resolve"/>.
    /// Must be non-empty before a ticket can be marked resolved.
    /// </summary>
    public string? ResolutionNote { get; set; }

    protected override Action<ITransitionBuilder<TicketState, TicketTransition>> Transitions => b => b
        // -- Valid transitions -------------------------------------------------
        .From(TicketState.Open)
            .On(TicketTransition.Assign).To(TicketState.InProgress)
        .From(TicketState.InProgress)
            .On(TicketTransition.Resolve).To(TicketState.Resolved)
                .When(() => !string.IsNullOrWhiteSpace(ResolutionNote))
        .From(TicketState.Resolved)
            .On(TicketTransition.Reopen).To(TicketState.Open)
        .From(TicketState.Resolved)
            .On(TicketTransition.Close).To(TicketState.Closed)
        // -- Lifecycle hooks on InProgress -------------------------------------
        .State(TicketState.InProgress)
            .OnEnter(async () => Console.WriteLine("    ? [OnEnter] InProgress — work timer started"))
            .OnExit(async () =>  Console.WriteLine("    ? [OnExit]  InProgress — work timer stopped"));

    public override Task<TransitionResult<TicketState>> TransitionAsync(
        TicketContext ctx, TicketTransition t) =>
        InternalTransitionAsync(ctx, t);

    public override Task NotifyAsync(TicketContext ctx, TicketNotification n) =>
        Task.CompletedTask;
}

