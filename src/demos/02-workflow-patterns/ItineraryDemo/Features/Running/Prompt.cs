using Ananke.Orchestration.Planning;
using ItineraryDemo.Shared;
using ItineraryDemo.Model;

namespace ItineraryDemo.Features.Running;

/// <summary>What somebody answered: one of the options, something else, or the one refusal nobody authors.</summary>
internal sealed record Answer
{
    public int? Picked { get; init; }

    public string? Said { get; init; }

    public PlanRefusal? Refusal { get; init; }
}

/// <summary>
/// Puts a halt to a person and takes their answer.
/// </summary>
/// <remarks>
/// <para>
/// <b>The only thing in an attended run that still writes to the console mid-run.</b> Everything
/// else the run has to say goes to the log; this cannot, because it is a question and somebody is
/// waiting to answer it.
/// </para>
/// <para>
/// <b>There is no way through without choosing.</b> An empty line re-asks. That is this side of a
/// guarantee the workflow already enforces — a resume carrying no pick routes straight back to the
/// question — and it is why cancel has to be on the list: a person with no acceptable option still
/// needs a way to stop.
/// </para>
/// </remarks>
internal static class Prompt
{
    public static Answer Ask(PlanQuestion question, PlanCoordination halt, Narration narration)
    {
        Console.WriteLine();
        Console.WriteLine($"  ⏸  {halt.HaltReason}");
        Console.WriteLine();

        // What a person was shown, before they answer. A record that holds the decision and not the
        // options it was made among cannot say whether the choice was a real one.
        narration.Line($"the run paused and put this to a person: {halt.HaltReason}");

        foreach (var offered in question.Options)
            narration.Line($"  {(offered.Recommended ? "→" : " ")} {offered.Summary}");

        // Options that only answer a step read differently from options that change the plan.
        Console.WriteLine(question.Options.Count > 0 && question.Options.All(o => o.Answer is not null)
            ? "     Either would work. The supervisor would take:"
            : "     The supervisor suggests:");

        for (var i = 0; i < question.Options.Count; i++)
        {
            Console.WriteLine($"       {i + 1}. {question.Options[i].Summary}"
                + (question.Options[i].Recommended ? "   ← recommended" : ""));
        }

        // Appended here rather than offered by the supervisor, so no supervisor can leave somebody
        // stuck (R32). Lettered, because neither is one of the options and numbering them would move
        // them every time the supervisor offers a different number of alternatives.
        Console.WriteLine("       s. Say something else — in your own words, and it goes back to the supervisor");
        Console.WriteLine("       c. Cancel — stop here, and leave the trip unplanned");
        Console.WriteLine();

        while (true)
        {
            Console.Write($"     Your choice [1-{question.Options.Count}, s, c]: ");

            var typed = Console.ReadLine();

            // End of input: there is nobody to ask. Spinning here would print "Not an option"
            // forever at a stream that has already ended.
            if (typed is null)
            {
                throw new InvalidOperationException(
                    "The run stopped to ask, and there is no one to answer: standard input has "
                    + "ended. Run this attached to a terminal, or pass --auto to let the run answer "
                    + "its own questions with the supervisor's recommendation.");
            }

            var answer = typed.Trim();

            if (string.Equals(answer, "c", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine();
                narration.Line("a person chose: cancel — stop the run here");
                return new Answer { Refusal = PlanRefusal.Cancel };
            }

            if (int.TryParse(answer, out var picked) && picked >= 1 && picked <= question.Options.Count)
            {
                Console.WriteLine();
                narration.Line($"a person picked option {picked}: {question.Options[picked - 1].Summary}");
                return new Answer { Picked = picked };
            }

            if (string.Equals(answer, "s", StringComparison.OrdinalIgnoreCase))
            {
                Console.Write("     In your own words: ");
                answer = Console.ReadLine()?.Trim() ?? string.Empty;
            }

            // Anything else is taken as what it is — somebody saying something the list did not
            // contain — rather than being rejected until they pick from it. That is the whole of why
            // a narrowed list is safe, and this loop used to discard it into a cancel: an answer the
            // supervisor most needed to hear read as the person giving up on the trip.
            if (!string.IsNullOrWhiteSpace(answer))
            {
                Console.WriteLine();
                narration.Line($"a person answered off the list: {answer}");
                return new Answer { Said = answer };
            }

            Console.WriteLine("     Nothing typed. Pick a number, 's' to say something else, 'c' to stop.");
        }
    }
}
