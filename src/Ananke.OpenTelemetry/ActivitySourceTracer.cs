using System.Diagnostics;
using Ananke.Abstractions.Tracing;

namespace Ananke.OpenTelemetry;

/// <summary>
/// <see cref="IWorkflowTracer"/> backed by <see cref="System.Diagnostics.ActivitySource"/>.
/// Each trace maps to a root <see cref="Activity"/>, each span to a child <see cref="Activity"/>.
/// </summary>
public sealed class ActivitySourceTracer(string? sourceName = null) : IWorkflowTracer
{
    public const string DefaultSourceName = Sources.Orchestration;

    private readonly ActivitySource _source = new(sourceName ?? DefaultSourceName);

    public ActivitySource Source => _source;

    public ITrace StartTrace(string workflowName, string executionId, IDictionary<string, string>? metadata = null)
    {
        var activity = _source.StartActivity(workflowName, ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag("Ananke.workflow", workflowName);
            activity.SetTag("Ananke.execution_id", executionId);

            if (metadata is not null)
            {
                foreach (var (key, value) in metadata)
                    activity.SetTag(key, value);
            }
        }

        return new ActivityTrace(activity, _source);
    }

    private sealed class ActivityTrace(Activity? activity, ActivitySource source) : ITrace
    {
        public string TraceId => activity?.TraceId.ToString() ?? string.Empty;

        public ISpan StartSpan(string name, SpanKind kind = SpanKind.Job)
        {
            var child = source.StartActivity(name, ActivityKind.Internal, activity?.Context ?? default);
            child?.SetTag("Ananke.span_kind", kind.ToString().ToLowerInvariant());
            return new ActivitySpan(child, source);
        }

        public ValueTask DisposeAsync()
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ActivitySpan(Activity? activity, ActivitySource source) : ISpan
    {
        public string SpanId => activity?.SpanId.ToString() ?? string.Empty;

        public ISpan StartSpan(string name, SpanKind kind = SpanKind.Job)
        {
            var child = source.StartActivity(name, ActivityKind.Internal, activity?.Context ?? default);
            child?.SetTag("Ananke.span_kind", kind.ToString().ToLowerInvariant());
            return new ActivitySpan(child, source);
        }

        public void SetAttribute(string key, string value) => activity?.SetTag(key, value);

        public void RecordError(Exception exception)
        {
            if (activity is null) return;

            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                { "exception.type", exception.GetType().FullName },
                { "exception.message", exception.Message },
                { "exception.stacktrace", exception.StackTrace }
            }));
        }

        public ValueTask DisposeAsync()
        {
            if (activity is not null && activity.Status == ActivityStatusCode.Unset)
                activity.SetStatus(ActivityStatusCode.Ok);

            activity?.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
