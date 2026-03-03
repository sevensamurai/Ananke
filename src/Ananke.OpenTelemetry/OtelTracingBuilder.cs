using System.Diagnostics;
using Ananke.Abstractions.Tracing;
using Microsoft.Extensions.DependencyInjection;

namespace Ananke.OpenTelemetry;

/// <summary>
/// Convenience factory for non-DI scenarios (console apps, background services).
/// For ASP.NET or hosted apps use <c>services.AddTracingPipeline(o => ...)</c> instead.
/// <example>
/// <code>
/// using var tracing = OtelTracingBuilder.Build(o =>
/// {
///     o.ServiceName = "my-app";
///     o.UseBetterStack(token);
/// });
///
/// var workflow = new Workflow&lt;MyState&gt;("name")
///     .UseTracing(tracing.Tracer)
///     ...
/// </code>
/// </example>
/// </summary>
public static class OtelTracingBuilder
{
    /// <summary>
    /// Builds an OTLP export pipeline using the standard OpenTelemetry DI pipeline internally.
    /// Dispose the returned <see cref="TracingPipeline"/> on shutdown to flush pending spans.
    /// </summary>
    public static TracingPipeline Build(Action<OtelTracingOptions> configure)
    {
        var options = new OtelTracingOptions();
        configure(options);

        if (options.OtlpEndpoint is null)
            throw new InvalidOperationException(
                "OTLP endpoint is not configured. Call UseBetterStack() or UseOtlp().");

        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;

        var services = new ServiceCollection();
        services.AddTracingPipeline(configure);

        var sp = services.BuildServiceProvider();
        var tracer = sp.GetRequiredService<IWorkflowTracer>();

        Console.WriteLine($"[OTel] service={options.ServiceName} endpoint={options.OtlpEndpoint}");

        return new TracingPipeline(sp, tracer);
    }
}
