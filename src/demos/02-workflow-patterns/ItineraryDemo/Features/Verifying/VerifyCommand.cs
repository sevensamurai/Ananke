using Ananke.Orchestration.Planning;
using ItineraryDemo.Features.Planning;
using ItineraryDemo.Features.Stays;
using ItineraryDemo.Model;

namespace ItineraryDemo.Features.Verifying;

/// <summary>
/// Checks the plan without a model, a key, or a run.
/// </summary>
/// <remarks>
/// <b>The playground checks itself.</b> The facts are this demo's, and a test project reaching into a
/// demo would be a suite depending on a playground. <c>--verify</c> costs nothing and fails loudly.
/// </remarks>
internal static class VerifyCommand
{
    public static int Run(TripService service, PlanTree plan)
    {
        // Whether a criterion can be satisfied is asked against the service and the plan as loaded,
        // before any step has run.
        var checks = Validate.Checks(service, _ => Task.FromResult<PlanTree?>(plan));

        var faults = PlanAdmission.Faults(plan, [checks]);

        foreach (var fault in faults)
            Console.WriteLine($"  ✗ {fault}");

        foreach (var (nodeId, node) in plan.Current.Nodes)
        {
            foreach (var criterion in node.Contract.AcceptanceCriteria)
            {
                // Anything that does not parse this cleanly is already named above, by admission.
                if (Criterion.Parse(criterion) is not { Name: "stay", Arguments.Count: 4 } parsed
                    || parsed.Argument(0) is not { } place || !service.Knows(place)
                    || !DateOnly.TryParse(parsed.Argument(1), out var from)
                    || !DateOnly.TryParse(parsed.Argument(2), out var to)
                    || !int.TryParse(parsed.Argument(3), out var nights))
                {
                    continue;
                }

                var found = service.Search(place, from, to, nights).FirstOrDefault();

                Console.WriteLine(found is not null
                    ? $"  {nodeId}: {criterion} — can be met: {found.Stay} from {found.CheckIns[0]:yyyy-MM-dd}"
                    : $"  {nodeId}: {criterion} — the service cannot meet it as written");
            }
        }

        return faults.Count == 0 ? 0 : 1;
    }
}
