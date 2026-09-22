using System.Text.Json;
using Ananke.Design;
using Ananke.Orchestration.Planning;
using ItineraryDemo.Model;
using ItineraryDemo.Features.Running;
using ItineraryDemo.Features.Verifying;
using ItineraryDemo.Shared;

// A traveller's request — a plan, and a service that says what it can actually have — carried out one
// step at a time, and steered when part of it turns out to be impossible. Some of what was asked for
// cannot be had, and what happens then is the whole demo: a choice nobody in the run may make goes to
// a person, and a wall nothing can plan around goes back to the planner, because the shape of the plan
// is what is wrong.
//
// Nothing happens here. This reads the request, decides which command was asked for, and hands over
// — so that what the demo *does* is in a file named after it rather than in a script that grew.

if (Options.Read(args, out var complaint) is not { } options)
{
    Console.Error.WriteLine(complaint);
    return 2;
}

var planFolder = Path.Combine(AppContext.BaseDirectory, "plan");

TripService service;
PlanTree plan;

try
{
    service = TripService.Load(planFolder);
    plan = PlanManifest.Load(Path.Combine(AppContext.BaseDirectory, options.Plan)).ToTree();
}
catch (Exception unreadable) when (unreadable is InvalidOperationException or IOException or JsonException)
{
    Console.Error.WriteLine($"  ✗ {unreadable.Message}");
    return 1;
}

// Before anything that needs a key: does the plan even admit? A demo whose premise quietly became
// satisfiable, or whose plan cannot run at all, proves nothing either way.
return options.Verify
    ? VerifyCommand.Run(service, plan)
    : await RunCommand.RunAsync(service, plan, options);
