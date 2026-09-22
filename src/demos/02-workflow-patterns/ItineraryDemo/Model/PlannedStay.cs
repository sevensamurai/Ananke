using System.Globalization;
using Ananke.Orchestration.Planning;

namespace ItineraryDemo.Model;

/// <summary>The stay one step of the plan asks for, where the step stands, and the option it settled on.</summary>
/// <remarks>
/// <c>OnSetDates</c> says whether the step asks for these exact nights (<c>stay_on</c>) rather than for
/// some nights somewhere between the dates (<c>stay</c>). Only a stay on set dates is part of the week.
/// </remarks>
internal sealed record PlannedStay(
    string Step, string Place, DateOnly CheckIn, DateOnly CheckOut, int Nights, bool OnSetDates, StepState State, string? Choice)
{
    /// <summary>The stay each step of <paramref name="tree"/> asks for, in plan order, leaving out given-up steps.</summary>
    public static IReadOnlyList<PlannedStay> In(PlanTree tree)
    {
        var stays = new List<PlannedStay>();

        foreach (var id in tree.Root.ChildIds)
        {
            var node = tree.Node(id);

            if (node.State is StepState.Skipped)
                continue;

            foreach (var text in node.Contract.AcceptanceCriteria)
            {
                switch (Criterion.Parse(text))
                {
                    case { Name: "stay_on", Arguments.Count: 3 } set
                        when Date(set.Argument(1), out var checkIn) && Date(set.Argument(2), out var checkOut):
                        stays.Add(new PlannedStay(
                            id, set.Argument(0)!, checkIn, checkOut, checkOut.DayNumber - checkIn.DayNumber,
                            OnSetDates: true, node.State, node.Result));
                        break;

                    case { Name: "stay", Arguments.Count: 4 } window
                        when Date(window.Argument(1), out var from) && Date(window.Argument(2), out var to)
                             && int.TryParse(window.Argument(3), out var nights):
                        stays.Add(new PlannedStay(
                            id, window.Argument(0)!, from, to, nights, OnSetDates: false, node.State, node.Result));
                        break;
                }
            }
        }

        return stays;
    }

    private static bool Date(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
