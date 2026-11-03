using System.Collections.Concurrent;
using Ananke.Abstractions.Tracing;

namespace Ananke.Orchestration.Tests.Fakes;

/// <summary>Keeps every span it opens, with each attribute as the type it was set with.</summary>
internal sealed class RecordingTracer : IWorkflowTracer
{
    private readonly ConcurrentQueue<RecordedSpan> _spans = new();

    /// <summary>Every span opened so far, in the order they were opened.</summary>
    public IReadOnlyList<RecordedSpan> Spans => [.. _spans];

    public ITrace StartTrace(string workflowName, string executionId, IDictionary<string, string>? metadata = null) =>
        new Trace(this);

    private RecordedSpan Open(string name, SpanKind kind)
    {
        var span = new RecordedSpan(this, name, kind);
        _spans.Enqueue(span);
        return span;
    }

    private sealed class Trace(RecordingTracer tracer) : ITrace
    {
        public string TraceId { get; } = Guid.NewGuid().ToString("n");

        public ISpan StartSpan(string name, SpanKind kind = SpanKind.Job) => tracer.Open(name, kind);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class RecordedSpan(RecordingTracer tracer, string name, SpanKind kind) : ISpan
    {
        private readonly ConcurrentDictionary<string, object> _attributes = new(StringComparer.Ordinal);

        public string Name => name;

        public SpanKind Kind => kind;

        public IReadOnlyDictionary<string, object> Attributes => _attributes;

        public string SpanId { get; } = Guid.NewGuid().ToString("n")[..16];

        public ISpan StartSpan(string name, SpanKind kind = SpanKind.Job) => tracer.Open(name, kind);

        public void SetAttribute(string key, string value) => _attributes[key] = value;

        public void SetAttribute(string key, long value) => _attributes[key] = value;

        public void SetAttribute(string key, bool value) => _attributes[key] = value;

        public void RecordError(Exception exception) { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
