using Ananke.Abstractions.Tracing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Ananke.OpenTelemetry;

/// <summary>
/// Extension methods to integrate Ananke tracing into the standard OpenTelemetry DI pipeline.
/// </summary>
public static class TracingExtensions
{
    /// <summary>
    /// Registers the full Ananke OpenTelemetry tracing pipeline: resource, activity sources,
    /// OTLP exporter, and <see cref="IWorkflowTracer"/> singleton.
    /// <example>
    /// <code>
    /// services.AddTracingPipeline(o =>
    /// {
    ///     o.ServiceName = "my-app";
    ///     o.ServiceVersion = "0.1.0";
    ///     o.UseBetterStack(token);
    /// });
    /// </code>
    /// </example>
    /// </summary>
    public static IServiceCollection AddTracingPipeline(
        this IServiceCollection services,
        Action<OtelTracingOptions> configure)
    {
        var options = new OtelTracingOptions();
        configure(options);

        services.AddOpenTelemetry()
            .ConfigureResource(r =>
            {
                r.AddService(
                    serviceName: options.ServiceName,
                    serviceVersion: options.ServiceVersion);
            })
            .WithTracing(t =>
            {
                foreach (var name in options.ActivitySourceNames)
                    t.AddSource(name);

                if (options.OtlpEndpoint is not null)
                {
                    t.AddOtlpExporter(o =>
                    {
                        o.Endpoint = options.OtlpEndpoint;
                        o.Headers = options.OtlpHeaders;
                        o.Protocol = OtlpExportProtocol.HttpProtobuf;
                    });
                }
            });

        services.AddSingleton<IWorkflowTracer, ActivitySourceTracer>();

        return services;
    }

    /// <summary>
    /// Registers the Ananke <see cref="System.Diagnostics.ActivitySource"/> names with the
    /// OpenTelemetry tracing pipeline. Call inside <c>.WithTracing(t =&gt; t.AddTracing())</c>.
    /// </summary>
    /// <param name="builder">The <see cref="TracerProviderBuilder"/> to configure.</param>
    /// <param name="includeStateMachine">Also register the <c>Ananke.StateMachine</c> source.</param>
    public static TracerProviderBuilder AddTracing(
        this TracerProviderBuilder builder,
        bool includeStateMachine = false)
    {
        builder.AddSource(Sources.Orchestration);
        if (includeStateMachine)
            builder.AddSource(Sources.StateMachine);
        return builder;
    }

    /// <summary>
    /// Registers <see cref="ActivitySourceTracer"/> as the <see cref="IWorkflowTracer"/> singleton.
    /// Inject <see cref="IWorkflowTracer"/> and pass it to <c>workflow.UseTracing(tracer)</c>.
    /// </summary>
    public static IServiceCollection AddWorkflowTracer(this IServiceCollection services)
    {
        services.AddSingleton<IWorkflowTracer, ActivitySourceTracer>();
        return services;
    }
}
