using System.Text.Json;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Streaming;

namespace Ananke.Design;

/// <summary>Sinks a run's events reach, and a way to use more than one at a time.</summary>
public static class EventSinks
{
    /// <summary>
    /// One sink over several, each reported to in order.
    /// </summary>
    /// <remarks>
    /// <see cref="WorkflowEventReporting.BeginScope"/> takes one sink, and the first one in owns the
    /// flow — so a consumer that both narrates a run and keeps a record of it has to join them itself.
    /// Reported in the order given, because a reader watching a console expects the narration before
    /// whatever else a run does with the same event.
    /// </remarks>
    public static IWorkflowEventSink Several(params IWorkflowEventSink[] sinks)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        return new SeveralSinks(sinks);
    }

    private sealed class SeveralSinks(IWorkflowEventSink[] sinks) : IWorkflowEventSink
    {
        public async ValueTask ReportAsync(WorkflowEvent evt, CancellationToken ct = default)
        {
            foreach (var sink in sinks)
                await sink.ReportAsync(evt, ct).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// A run's plan events and tool calls, one JSON object per line.
/// </summary>
/// <remarks>
/// <para>
/// <b>What the narration says in prose, this says as data.</b> A narrated run reads well and cannot be
/// queried; a reader asking which tools a step called, or what a supervisor offered at the third halt,
/// wants the events themselves. One object per line so a run can be read back an event at a time —
/// by <c>jq</c> as often as by code — and so a run killed mid-pass leaves everything before it intact.
/// </para>
/// <para>
/// <b>Plan events and tool calls only.</b> The rest of a workflow's stream is about the runner, not
/// about the plan, and a record that carried it would bury what a reader came for.
/// </para>
/// </remarks>
public sealed class JsonLinesEventSink : IWorkflowEventSink, IDisposable
{
    private readonly StreamWriter _writer;

    private JsonLinesEventSink(StreamWriter writer) => _writer = writer;

    /// <summary>Opens <paramref name="path"/> for writing, replacing anything already there.</summary>
    public static JsonLinesEventSink Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return new JsonLinesEventSink(new StreamWriter(path, append: false) { AutoFlush = true });
    }

    /// <inheritdoc />
    public ValueTask ReportAsync(WorkflowEvent evt, CancellationToken ct = default)
    {
        if (evt is PlanEvent or AgentToolCalled)
            _writer.WriteLine(JsonSerializer.Serialize(new { type = evt.GetType().Name, @event = (object)evt }));

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => _writer.Dispose();
}
