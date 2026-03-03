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

    void RecordError(Exception exception);
}
