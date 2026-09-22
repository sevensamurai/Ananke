using Ananke.Design;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Streaming;

namespace ItineraryDemo.Shared;

/// <summary>
/// What the run has to say, printed to the console as it happens and kept beside the itinerary.
/// </summary>
/// <remarks>
/// Two <see cref="PlanNarrator"/>s over the same events, so a node's report and its verdict — two
/// events — read as the one line they belong to at both widths. The console's clips long
/// model-written sentences and shows the checks that passed; the log's keeps every word and every
/// tool call, because a step or the Planner can make a dozen calls and the console keeps the shape.
/// </remarks>
internal sealed class Narration : IWorkflowEventSink, IDisposable
{
    private readonly StreamWriter _writer;
    private readonly PlanNarrator _console = new(lines: PlanNarratorLines.PassedChecks);

    private readonly PlanNarrator _log = new(
        int.MaxValue, PlanNarratorLines.PassedChecks | PlanNarratorLines.ToolCalls);

    private Narration(StreamWriter writer) => _writer = writer;

    public static string PathIn(string runFolder) => Path.Combine(runFolder, "run.log");

    public static Narration Open(string runFolder) =>
        new(new StreamWriter(PathIn(runFolder), append: false) { AutoFlush = true });

    /// <summary>Prints one line, to the console and to the run's log.</summary>
    public void Line(string text)
    {
        Console.WriteLine(text);
        _writer.WriteLine(text);
    }

    /// <inheritdoc />
    public ValueTask ReportAsync(WorkflowEvent evt, CancellationToken ct = default)
    {
        if (_console.Describe(evt) is { } shown)
            Console.WriteLine(shown);

        if (_log.Describe(evt) is { } kept)
            _writer.WriteLine(kept);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => _writer.Dispose();
}
