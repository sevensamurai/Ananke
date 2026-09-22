using System.Globalization;
using System.Text.Json;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.OpenAI;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Orchestration.Tests;

/// <summary>
/// Whether a step reports what it found the way the plan reads it: whether it could do its task, and
/// the options it found, in order, without picking one.
/// </summary>
/// <remarks>
/// <para>
/// Each case gives a step a contract and a tool, and runs it <see cref="Runs"/> times. Every run must
/// report the expected status and list the expected options, in order. The trip cases search a small
/// stay service that answers the dates asked, or its nearest alternatives when nothing matches; the
/// coding cases check packages with a tool that returns a fixed answer.
/// </para>
/// <para>
/// Live and explicit — it spends model calls. Run with
/// <c>dotnet test --filter FullyQualifiedName~StepReportEvalTests</c>, with <c>OPENAI_API_KEY</c> in the
/// environment or the repo's <c>.env</c>; <c>ANANKE_TEST_MODEL</c> chooses the model and
/// <c>ANANKE_EVAL_RUNS</c> the number of runs.
/// </para>
/// </remarks>
[TestFixture]
[Category("Live")]
[Explicit("Calls a real model; needs OPENAI_API_KEY.")]
public class StepReportEvalTests
{
    /// <summary>How many times each case runs. All of them must match.</summary>
    private static int Runs =>
        int.TryParse(Environment.GetEnvironmentVariable("ANANKE_EVAL_RUNS"), out var n) && n > 0
            ? n
            : 3;

    /// <summary>One step, the tools it has, and what its report should say.</summary>
    /// <param name="Goal">The step's goal.</param>
    /// <param name="Criterion">Its one acceptance criterion.</param>
    /// <param name="Tools">The tools it is given.</param>
    /// <param name="Done">Whether the step should report it could do its task.</param>
    /// <param name="Options">Text each reported option should contain, in order.</param>
    public sealed record Scenario(
        string Goal,
        string Criterion,
        Func<ToolKit> Tools,
        bool Done,
        string[] Options);

    public static IEnumerable<TestCaseData> Cases()
    {
        yield return Case("Trip_OneStayIsFree", new Scenario(
            "Find a stay in Hakone for 1 night, checking in on 2027-04-07.",
            "a stay in hakone for 1 night from 2027-04-07",
            Stays,
            Done: true, Options: ["onsen-ryokan"]));

        yield return Case("Trip_TwoStaysAreFree", new Scenario(
            "Find a stay in Kyoto for 2 nights, checking in on 2027-04-04.",
            "a stay in kyoto for 2 nights from 2027-04-04",
            Stays,
            Done: true, Options: ["gion-machiya", "station-hotel"]));

        yield return Case("Trip_TwoNightsAreNotFreeButOneIs", new Scenario(
            "Find a stay in Hakone for 2 nights in a row between 2027-04-01 and 2027-04-08.",
            "a stay in hakone for 2 nights in a row between 2027-04-01 and 2027-04-08",
            Stays,
            Done: false, Options: ["onsen-ryokan"]));

        yield return Case("Trip_NothingIsFree", new Scenario(
            "Find a stay in Hakone for 2 nights in a row between 2027-04-01 and 2027-04-06.",
            "a stay in hakone for 2 nights in a row between 2027-04-01 and 2027-04-06",
            Stays,
            Done: false, Options: []));

        yield return Case("Code_TheUpgradeIsCompatible", new Scenario(
            "Upgrade AutoMapper to 14.0.0 in Billing.Api, which targets net8.0.",
            "Billing.Api builds with AutoMapper 14.0.0",
            () => Packages("""{"package":"AutoMapper","version":"14.0.0","compatible":true}"""),
            Done: true, Options: ["14.0.0"]));

        yield return Case("Code_TwoSerializersFit", new Scenario(
            "Choose a JSON serializer for Billing.Api, which targets net8.0.",
            "Billing.Api builds with the chosen JSON serializer",
            () => Packages("""{"candidates":[{"package":"System.Text.Json","compatible":true,"notes":"part of the framework"},{"package":"Newtonsoft.Json","version":"13.0.3","compatible":true,"notes":"widely used"}]}"""),
            Done: true, Options: ["System.Text.Json", "Newtonsoft.Json"]));

        yield return Case("Code_TheUpgradeIsIncompatibleButOthersWork", new Scenario(
            "Upgrade AutoMapper to 14.0.0 in Billing.Legacy, which targets net6.0.",
            "Billing.Legacy builds with AutoMapper 14.0.0",
            () => Packages("""{"package":"AutoMapper","version":"14.0.0","compatible":false,"reason":"14.0.0 requires net8.0","alternatives":["pin AutoMapper to 13.0.1","migrate to Mapster 7.4.0"]}"""),
            Done: false, Options: ["13.0.1", "Mapster"]));

        yield return Case("Code_TheUpgradeIsIncompatibleAndNothingElseWorks", new Scenario(
            "Upgrade AutoMapper to 14.0.0 in Billing.Legacy, which targets net6.0.",
            "Billing.Legacy builds with AutoMapper 14.0.0",
            () => Packages("""{"package":"AutoMapper","version":"14.0.0","compatible":false,"reason":"14.0.0 requires net8.0","alternatives":[]}"""),
            Done: false, Options: []));
    }

    [TestCaseSource(nameof(Cases))]
    public async Task RunAsync_EachCase_ReportsTheExpectedStatusAndOptions(Scenario scenario)
    {
        var lines = new List<string>();
        var mismatches = 0;

        for (var run = 1; run <= Runs; run++)
        {
            var calls = new ToolCalls();
            NodeOutcome outcome;

            using (WorkflowEventReporting.BeginScope(calls))
                outcome = await Once(scenario).ConfigureAwait(false);

            var matches = Matches(scenario, outcome);

            if (!matches)
                mismatches++;

            lines.Add($"run {run}: {(matches ? "ok" : "MISMATCH")} · Done={outcome.Done} · "
                + $"Options=[{string.Join(" | ", outcome.Options)}] · Summary={outcome.Summary}");

            lines.AddRange(calls.Seen.Select(call =>
                $"    tool {call.ToolName} {call.Arguments} → {(call.IsError ? "error: " : "")}{call.Result}"));

            if (calls.Seen.Count == 0)
                lines.Add("    no tool was called");
        }

        foreach (var line in lines)
            TestContext.Out.WriteLine(line);

        mismatches.ShouldBe(0, string.Join(Environment.NewLine, lines));
    }

    // ── Fixtures ──

    private static TestCaseData Case(string name, Scenario scenario) =>
        new TestCaseData(scenario).SetName(name);

    private static bool Matches(Scenario scenario, NodeOutcome outcome) =>
        outcome.Done == scenario.Done
        && outcome.Options.Count == scenario.Options.Length
        && outcome.Options.Select((option, i) => option.Contains(scenario.Options[i], StringComparison.OrdinalIgnoreCase))
            .All(found => found);

    private static Task<NodeOutcome> Once(Scenario scenario)
    {
        var contract = new AgentContract { Goal = scenario.Goal, AcceptanceCriteria = [scenario.Criterion] };

        var runner = new PlanNodeAgentRunner(new PlanNodeAgentOptions
        {
            Model = Model(),
            Configure = builder => builder.WithTools(scenario.Tools()).WithMaxToolRounds(4)
        });

        return runner.RunAsync(new PlanNodeContext
        {
            PlanId = "eval",
            PlanVersion = 1,
            Node = new PlanNode { Id = "step", Contract = contract },
            Contract = contract,
            TreeView = string.Empty,
            Projection = new PlanTreeProjection.Projection { Text = string.Empty, EstimatedTokens = 0 }
        });
    }

    /// <summary>A package check for the step's project that always gives <paramref name="answer"/>.</summary>
    private static ToolKit Packages(string answer) => new ToolKit("packages").AddTool(
        "check_package",
        "Checks packages for this step's project, already filled in: whether they work with it, and what "
            + "else does when they do not.",
        () => ToolResult.Ok(answer));

    /// <summary>
    /// Each place's stays, best ranked first, with the nights each is free; <see langword="null"/> is
    /// free every night.
    /// </summary>
    private static readonly Dictionary<string, (string Stay, DateOnly[]? Free)[]> Service =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["hakone"] = [("onsen-ryokan", new[] { new DateOnly(2027, 4, 7) }), ("lakeside-inn", Array.Empty<DateOnly>())],
            ["kyoto"] = [("gion-machiya", null), ("station-hotel", null)]
        };

    /// <summary>
    /// A trip service's stay search, and its search for the nearest options when that finds nothing.
    /// </summary>
    private static ToolKit Stays() => new ToolKit("trip")
        .AddTool(
            "search_stays",
            "Searches the trip service for stays in a place free for that many nights in a row, checking "
                + "in on or after 'from' and checking out by 'to'. Dates are yyyy-MM-dd.",
            tool => StayParameters(tool).OnExecute(args => ToolResult.Ok(SearchStays(args, options: false))))
        .AddTool(
            "search_stay_options",
            "Searches the trip service for the nearest stays in a place inside the same dates, for when "
                + "search_stays finds none: fewer nights in a row, on the nights that are free. Dates are "
                + "yyyy-MM-dd.",
            tool => StayParameters(tool).OnExecute(args => ToolResult.Ok(SearchStays(args, options: true))));

    private static ToolBuilder StayParameters(ToolBuilder tool) => tool
        .Param("place", "The place.")
        .Param("from", "The earliest check-in, yyyy-MM-dd.")
        .Param("to", "The latest check-out, yyyy-MM-dd.")
        .Param("nights", "How many nights in a row.");

    private static string SearchStays(ToolArgs args, bool options)
    {
        if (!DateOnly.TryParseExact(args.Get("from"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
            || !DateOnly.TryParseExact(args.Get("to"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to)
            || !int.TryParse(args.Get("nights"), CultureInfo.InvariantCulture, out var nights)
            || nights < 1)
        {
            return JsonSerializer.Serialize(new { error = "'from' and 'to' are yyyy-MM-dd dates and 'nights' is a whole number" });
        }

        var stays = Service.TryGetValue(args.Get("place"), out var offered) ? offered : [];

        if (options)
        {
            return JsonSerializer.Serialize(new
            {
                options = stays
                    .SelectMany(s => FreeRuns(s.Free, from, to).Select(run => new
                    {
                        stay = s.Stay,
                        checkIn = Text(run.Start),
                        nights = run.Length
                    }))
                    .ToArray()
            });
        }

        return JsonSerializer.Serialize(new
        {
            stays = stays
                .Select(s => new
                {
                    stay = s.Stay,
                    nights,
                    checkIns = Nights(from, to.AddDays(-nights + 1))
                        .Where(checkIn => Nights(checkIn, checkIn.AddDays(nights)).All(night => IsFree(s.Free, night)))
                        .Select(Text)
                        .ToArray()
                })
                .Where(s => s.checkIns.Length > 0)
                .ToArray()
        });
    }

    private static bool IsFree(DateOnly[]? free, DateOnly night) => free is null || free.Contains(night);

    /// <summary>The nights from <paramref name="first"/> up to, not including, <paramref name="end"/>.</summary>
    private static IEnumerable<DateOnly> Nights(DateOnly first, DateOnly end)
    {
        for (var night = first; night < end; night = night.AddDays(1))
            yield return night;
    }

    /// <summary>Each run of free nights inside <c>[from, to)</c>.</summary>
    private static IEnumerable<(DateOnly Start, int Length)> FreeRuns(DateOnly[]? free, DateOnly from, DateOnly to)
    {
        DateOnly? start = null;
        var length = 0;

        foreach (var night in Nights(from, to))
        {
            if (IsFree(free, night))
            {
                start ??= night;
                length++;
                continue;
            }

            if (start is { } began)
                yield return (began, length);

            start = null;
            length = 0;
        }

        if (start is { } last)
            yield return (last, length);
    }

    private static string Text(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The tool calls one run made, with what each returned.</summary>
    private sealed class ToolCalls : IWorkflowEventSink
    {
        public List<AgentToolCalled> Seen { get; } = [];

        public ValueTask ReportAsync(WorkflowEvent evt, CancellationToken ct = default)
        {
            if (evt is AgentToolCalled called)
                Seen.Add(called);

            return ValueTask.CompletedTask;
        }
    }

    private static IAgentModel Model() => OpenAIChatAgentModel.Create(
        Keys.Require("OPENAI_API_KEY"),
        Environment.GetEnvironmentVariable("ANANKE_TEST_MODEL") ?? Models.OpenAI.Gpt56Luna);
}
