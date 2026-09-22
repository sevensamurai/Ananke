using System.Globalization;
using System.Text;
using Ananke.Abstractions.Tracing;
using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Streaming;

namespace Ananke.Design;

/// <summary>
/// A running account of what a supervised session is actually doing — the plan's decisions and the
/// work underneath them — written for a person, locally, while it happens.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two streams, one story.</b> A plan reports its decisions as workflow events, and the work
/// beneath them — jobs, model calls, tool calls — arrives as spans on <see cref="IWorkflowTracer"/>.
/// Read apart, neither answers *"what is going on"*: the events say a node was satisfied without
/// saying it called a tool twice to get there, and the spans say a tool ran without saying which
/// step of which plan wanted it. This joins them, in arrival order, and attributes the work to the
/// node that asked for it.
/// </para>
/// <para>
/// <b>It explains rather than dumps.</b> A span with <c>tool.hallucination=true</c> is not printed
/// as an attribute; it is printed as *the model asked for a tool that does not exist, and was told
/// so*. What a reader needs is what the framework did and why, and that is knowable from the shape
/// of the span — while prompts and replies, which are the bulk of a request dump, are deliberately
/// absent: this is a record of the session's <em>conduct</em>, and the model's own words already
/// have a home in the node's report.
/// </para>
/// <para>
/// <b>Local, and the only copy that is guaranteed.</b> It needs no collector, no exporter and no
/// account: the transcript is a string this process holds and a caller can write beside whatever the
/// run produced. A provider's dashboard is a convenience; this is the record.
/// </para>
/// <para>
/// One per run. Lines are collected in arrival order under a lock, so a forked branch cannot
/// interleave half a line into another's.
/// </para>
/// </remarks>
public sealed class SessionTrace : IWorkflowTracer
{
    private readonly Action<string>? _write;
    private readonly TimeProvider _time;
    private readonly PlanNarrator _narrator = new();
    private readonly List<string> _lines = [];
    private readonly Lock _gate = new();

    /// <summary>Creates a trace that reports each line to <paramref name="write"/> as it happens.</summary>
    /// <param name="write">
    /// Where each line goes as it is produced — a console, a log. <see langword="null"/> collects
    /// them silently for <see cref="ToTranscript"/>.
    /// </param>
    /// <param name="timeProvider">Clock for the elapsed times. Defaults to the system clock.</param>
    public SessionTrace(Action<string>? write = null, TimeProvider? timeProvider = null)
    {
        _write = write;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Every line so far, in the order it happened.</summary>
    public IReadOnlyList<string> Lines
    {
        get { lock (_gate) return [.. _lines]; }
    }

    /// <summary>
    /// Takes one workflow event and adds what it means to the account.
    /// </summary>
    /// <remarks>
    /// Plan events are narrated by <see cref="PlanNarrator"/> — the same lines a console shows — and
    /// everything else is ignored. Note that <b>attribution does not come from here</b>: events are
    /// consumed by whoever is reading the stream, which can be a step behind the work, so a tool call
    /// is attributed from the span's own name instead.
    /// </remarks>
    public void Note(WorkflowEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (_narrator.Describe(evt) is { } narrated)
            Add(narrated);
    }

    /// <summary>Adds a line of a caller's own — a decision, a heading, an aside.</summary>
    public void Note(string line) => Add(line);

    /// <summary>The whole account as one document.</summary>
    public string ToTranscript()
    {
        var transcript = new StringBuilder();

        lock (_gate)
        {
            foreach (var line in _lines)
                transcript.AppendLine(line);
        }

        return transcript.ToString();
    }

    /// <inheritdoc />
    public ITrace StartTrace(
        string workflowName, string executionId, IDictionary<string, string>? metadata = null) =>
        new Scope(this, workflowName);

    private void Add(string line)
    {
        lock (_gate)
            _lines.Add(line);

        _write?.Invoke(line);
    }

    /// <summary>What a span says once it is over.</summary>
    /// <remarks>
    /// Reported on close rather than on open, because the interesting half — how many tool rounds,
    /// how long, whether anything went wrong — is only known then. Jobs are the exception: a job
    /// announces itself, because a reader watching a plan unfold wants to know what has started
    /// before waiting on it.
    /// </remarks>
    private sealed class Span : ISpan
    {
        private readonly SessionTrace _trace;
        private readonly string _name;
        private readonly SpanKind _kind;
        private readonly int _depth;
        private readonly long _startedAt;
        private readonly string? _agent;
        private readonly Dictionary<string, string> _attributes = new(StringComparer.Ordinal);

        private Exception? _error;

        public Span(SessionTrace trace, string name, SpanKind kind, int depth, string? agent = null)
        {
            _trace = trace;
            _name = name;
            _kind = kind;
            _depth = depth;
            _startedAt = trace._time.GetTimestamp();

            // An LLM span is named "<agent>/<phase>", and a plan node's agent is "plan-node:<id>",
            // so the work names the step that wanted it. A tool call inherits that from the call it
            // was made during — which is what makes "write-index called save" true rather than a
            // guess at whatever the event stream had reached.
            _agent = kind is SpanKind.LlmCall ? Owner(name) : agent;

            if (kind is SpanKind.Job)
                trace.Add($"{Indent(depth)}▸ {name} — started");
        }

        public string SpanId { get; } = Guid.NewGuid().ToString("n")[..8];

        public ISpan StartSpan(string name, SpanKind kind = SpanKind.Job) =>
            new Span(_trace, name, kind, _depth + 1, _agent);

        private static string Owner(string spanName)
        {
            var agent = spanName.Split('/')[0];

            return agent.StartsWith("plan-node:", StringComparison.Ordinal)
                ? agent["plan-node:".Length..]
                : agent;
        }

        public void SetAttribute(string key, string value) => _attributes[key] = value;

        public void RecordError(Exception exception) => _error = exception;

        public void RecordRetry(int attemptNumber, string reason) =>
            _trace.Add($"{Indent(_depth + 1)}↻ attempt {attemptNumber} failed, trying again — {reason}");

        public void RecordFaultResolved(string toolName, bool recovered) =>
            _trace.Add(recovered
                ? $"{Indent(_depth + 1)}✓ {toolName} worked on a later attempt; the earlier fault is closed"
                : $"{Indent(_depth + 1)}✗ {toolName} never worked; the model gave up on it");

        public ValueTask DisposeAsync()
        {
            if (Describe() is { } line)
                _trace.Add(Indent(_depth) + line + Elapsed());

            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// The line this span leaves behind, or <see langword="null"/> when it has nothing to add.
        /// </summary>
        /// <remarks>
        /// <b>A model call that did nothing unusual says nothing.</b> The step line already reports
        /// what the node came back with, so a bare *"asked the model"* beside it is the shape of
        /// trace that gets skimmed and then ignored. It speaks up when it used tools, retried, or
        /// failed — which is where a reader's question actually is.
        /// </remarks>
        private string? Describe() => _kind switch
        {
            SpanKind.Job => $"▪ {_name} — done",
            SpanKind.ToolCall => $"↳ {Under()}called {ToolName()}{ToolOutcome()}",
            _ => ModelOutcome() is { Length: > 0 } outcome ? $"· {Under()}asked the model{outcome}" : null
        };

        /// <summary>Which plan step wanted this, when it was running under one.</summary>
        private string Under() => _agent is null ? "" : $"{_agent}: ";

        private string ToolName() =>
            _name.StartsWith("tool:", StringComparison.Ordinal) ? _name[5..] : _name;

        /// <summary>
        /// What became of a tool call, and what the framework did about it.
        /// </summary>
        /// <remarks>
        /// Each of these is a fault the tier handles by <em>telling the model</em> rather than by
        /// failing, which is invisible from the outside: a run recovers and nothing says it had to.
        /// </remarks>
        private string ToolOutcome()
        {
            if (Flag("tool.hallucination"))
                return $" — no such tool ('{Attribute("tool.hallucination.requested_name") ?? ToolName()}'); "
                    + "it was told so and can correct itself";

            if (Flag("tool.malformed_arguments"))
                return " — its arguments were not valid JSON; it was asked to re-emit the call";

            if (Flag("tool.error"))
            {
                return Attribute("tool.retryable") is "false"
                    ? " — the tool failed and said not to try again"
                    : " — the tool failed; it may try again";
            }

            var capped = Flag("tool.output_capped")
                ? $", output truncated to {Attribute("tool.output_chars_after")} chars to fit the window"
                : "";

            return Attribute("output_length") is { } chars
                ? $" → {chars} chars back{capped}"
                : capped;
        }

        /// <summary>What one model call cost in rounds and retries, when it cost anything unusual.</summary>
        private string ModelOutcome()
        {
            var parts = new List<string>();

            if (Attribute("tool_rounds") is { } rounds && rounds != "0")
                parts.Add($"{rounds} tool round(s)");

            if (Attribute("gen_ai.retry_count") is { } retries && retries != "0")
                parts.Add($"{retries} retry/retries");

            if (_error is { } error)
                parts.Add($"failed: {error.Message}");

            // Only alongside something worth reporting: on its own, the length of a reply is a
            // measurement nobody asked for.
            if (parts.Count > 0 && Attribute("response_length") is { } length)
                parts.Add($"{length} chars back");

            return parts.Count == 0 ? "" : $" — {string.Join(", ", parts)}";
        }

        private bool Flag(string key) => Attribute(key) is "true";

        private string? Attribute(string key) =>
            _attributes.TryGetValue(key, out var value) ? value : null;

        private string Elapsed()
        {
            var seconds = _trace._time.GetElapsedTime(_startedAt).TotalSeconds;

            return seconds < 0.1
                ? ""
                : string.Create(CultureInfo.InvariantCulture, $"  ({seconds:F1}s)");
        }

        private static string Indent(int depth) => new(' ', 5 + (depth * 2));
    }

    private sealed class Scope(SessionTrace trace, string workflowName) : ITrace
    {
        public string TraceId { get; } = Guid.NewGuid().ToString("n");

        public ISpan StartSpan(string name, SpanKind kind = SpanKind.Job) =>
            new Span(trace, name, kind, depth: 0);

        public ValueTask DisposeAsync()
        {
            trace.Add($"     ▪ {workflowName} — the run is over");
            return ValueTask.CompletedTask;
        }
    }
}
