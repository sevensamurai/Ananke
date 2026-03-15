using Ananke.Abstractions.Tracing;

namespace Ananke.Orchestration.Tracing;

/// <summary>
/// No-op tracer used when tracing is not configured. All operations are zero-cost.
/// </summary>
public sealed class NullTracer : IWorkflowTracer
{
    public static readonly NullTracer Instance = new();

    public ITrace StartTrace(string workflowName, string executionId, IDictionary<string, string>? metadata = null)
        => NullTrace.Instance;

    private sealed class NullTrace : ITrace
    {
        public static readonly NullTrace Instance = new();

        public string TraceId => string.Empty;

        public ISpan StartSpan(string name, SpanKind kind = SpanKind.Job) => NullSpan.Instance;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NullSpan : ISpan
    {
        public static readonly NullSpan Instance = new();

        public string SpanId => string.Empty;

        public ISpan StartSpan(string name, SpanKind kind = SpanKind.Job) => Instance;

        public void SetAttribute(string key, string value) { }

        public void RecordError(Exception exception) { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
