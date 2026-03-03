using Ananke.Abstractions.Tracing;

namespace Ananke.OpenTelemetry;

/// <summary>
/// Owns the OTel export pipeline and exposes the <see cref="IWorkflowTracer"/>.
/// Dispose on shutdown — this disposes the internal <c>ServiceProvider</c>,
/// which in turn disposes <c>TracerProvider</c> and flushes pending spans.
/// </summary>
public sealed class TracingPipeline : IDisposable
{
    private readonly IDisposable _serviceProvider;

    internal TracingPipeline(IDisposable serviceProvider, IWorkflowTracer tracer)
    {
        _serviceProvider = serviceProvider;
        Tracer = tracer;
    }

    /// <summary>
    /// The workflow tracer to pass to <c>workflow.UseTracing(tracing.Tracer)</c>.
    /// </summary>
    public IWorkflowTracer Tracer { get; }

    /// <inheritdoc/>
    public void Dispose() => _serviceProvider.Dispose();
}
