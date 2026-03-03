namespace Ananke.OpenTelemetry;

/// <summary>
/// Configuration options for OpenTelemetry tracing export.
/// </summary>
public sealed class OtelTracingOptions
{
    /// <summary>
    /// The service name that appears in the trace backend (e.g. "stock-trader").
    /// </summary>
    public string ServiceName { get; set; } = "Ananke";

    /// <summary>
    /// The service version that appears in the trace backend (e.g. "0.1.0").
    /// </summary>
    public string? ServiceVersion { get; set; }

    /// <summary>
    /// ActivitySource names to subscribe to.
    /// Defaults to <see cref="Sources.Orchestration"/>.
    /// Call <see cref="AddSource"/> to add more (e.g. <see cref="Sources.StateMachine"/>).
    /// </summary>
    public List<string> ActivitySourceNames { get; } = [Sources.Orchestration];

    /// <summary>
    /// OTLP exporter endpoint (e.g. <c>https://in-otel.logs.betterstack.com</c>).
    /// </summary>
    public Uri? OtlpEndpoint { get; set; }

    /// <summary>
    /// OTLP exporter headers in <c>key=value</c> format, comma-separated.
    /// For BetterStack: <c>"Authorization=Bearer {token}"</c>.
    /// </summary>
    public string? OtlpHeaders { get; set; }

    /// <summary>
    /// Convenience method to add an additional <see cref="System.Diagnostics.ActivitySource"/> name to export.
    /// </summary>
    public OtelTracingOptions AddSource(string activitySourceName)
    {
        ActivitySourceNames.Add(activitySourceName);
        return this;
    }

    /// <summary>
    /// Configures the OTLP endpoint and auth header for BetterStack Telemetry.
    /// </summary>
    public OtelTracingOptions UseBetterStack(string sourceToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceToken);
        OtlpEndpoint = new Uri("https://in-otel.logs.betterstack.com");
        OtlpHeaders = $"Authorization=Bearer {sourceToken}";
        return this;
    }

    /// <summary>
    /// Configures a custom OTLP endpoint (Jaeger, Grafana Tempo, Aspire Dashboard, etc.).
    /// </summary>
    public OtelTracingOptions UseOtlp(string endpoint, string? headers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        OtlpEndpoint = new Uri(endpoint);
        OtlpHeaders = headers;
        return this;
    }
}
