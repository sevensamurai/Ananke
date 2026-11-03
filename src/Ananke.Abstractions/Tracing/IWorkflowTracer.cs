using System.Globalization;

namespace Ananke.Abstractions.Tracing;

public enum SpanKind
{
    Job,
    LlmCall,
    ToolCall
}

/// <summary>
/// Creates a trace scope for a workflow execution — the equivalent of Python's <c>with trace("name")</c>.
/// </summary>
public interface IWorkflowTracer
{
    ITrace StartTrace(string workflowName, string executionId, IDictionary<string, string>? metadata = null);
}

/// <summary>
/// Represents a top-level trace that groups all spans within a single workflow execution.
/// </summary>
public interface ITrace : IAsyncDisposable
{
    string TraceId { get; }

    ISpan StartSpan(string name, SpanKind kind = SpanKind.Job);
}

/// <summary>
/// Represents a unit of work within a trace (job execution, LLM call, tool call).
/// Spans can nest via <see cref="StartSpan"/>.
/// </summary>
public interface ISpan : IAsyncDisposable
{
    string SpanId { get; }

    ISpan StartSpan(string name, SpanKind kind = SpanKind.Job);

    void SetAttribute(string key, string value);

    /// <summary>
    /// Sets a numeric attribute.
    /// Default implementation formats it invariantly and calls <see cref="SetAttribute(string, string)"/>;
    /// override in tracing backends that keep an attribute's type.
    /// </summary>
    void SetAttribute(string key, long value) =>
        SetAttribute(key, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Sets a flag attribute.
    /// Default implementation calls <see cref="SetAttribute(string, string)"/> with <c>"true"</c> or
    /// <c>"false"</c>; override in tracing backends that keep an attribute's type.
    /// </summary>
    void SetAttribute(string key, bool value) => SetAttribute(key, value ? "true" : "false");

    void RecordError(Exception exception);

    /// <summary>
    /// Records a retry attempt on this span.
    /// Default implementation is a no-op; override in tracing backends to emit a span event.
    /// </summary>
    void RecordRetry(int attemptNumber, string reason) { }

    /// <summary>
    /// Records whether a prior fault on the named tool was ultimately recovered or abandoned.
    /// Default implementation is a no-op; override in tracing backends to emit a span event.
    /// </summary>
    void RecordFaultResolved(string toolName, bool recovered) { }
}
