using Ananke.Orchestration.Tracing;

namespace Ananke.Orchestration.Tests.Fakes;

/// <summary>
/// Turns a tool call's content on for work run outside a workflow, as <c>RecordContent(true)</c> does
/// for a workflow's jobs.
/// </summary>
internal static class RecordingContent
{
    /// <summary>Turns content on until the returned scope is disposed.</summary>
    public static IDisposable Begin()
    {
        var previous = WorkflowTraceContext.Value;
        WorkflowTraceContext.Value = (previous ?? new TraceInfo("test", "test")) with { RecordContent = true };
        return new Restore(previous);
    }

    private sealed class Restore(TraceInfo? previous) : IDisposable
    {
        public void Dispose() => WorkflowTraceContext.Value = previous;
    }
}
