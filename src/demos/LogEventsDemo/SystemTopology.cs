namespace LogEventsDemo;

/// <summary>
/// Static definition of the simulated 4-component distributed system,
/// its services, infrastructure dependencies, and dependency edges.
/// </summary>
internal static class SystemTopology
{
    /// <summary>All services in the simulated system.</summary>
    internal static IReadOnlyList<ServiceDefinition> Services { get; } =
    [
        new ServiceDefinition
        {
            Name = "api-gateway",
            Role = "User-facing HTTP endpoints",
            InfraDependencies = [],
            UpstreamServices = ["background-worker", "reporting-backend"],
            BaseErrorRate = 0.02f,
            NormalMessages =
            [
                "GET /api/v1/status 200 OK (12ms)",
                "POST /api/v1/orders 201 Created (45ms)",
                "GET /api/v1/users/42 200 OK (8ms)",
                "Health check passed",
                "Request rate: 342 req/s"
            ],
            TransientErrorMessages =
            [
                "GET /api/v1/orders 503 Service Unavailable",
                "Upstream timeout: background-worker did not respond within 5000ms",
                "Circuit breaker OPEN for reporting-backend",
                "Rate limit exceeded for client 10.0.0.5"
            ]
        },
        new ServiceDefinition
        {
            Name = "background-worker",
            Role = "Async job processing (queue consumers)",
            InfraDependencies = ["redis"],
            UpstreamServices = [],
            BaseErrorRate = 0.03f,
            NormalMessages =
            [
                "Dequeued job order-process-7821 from redis",
                "Job order-process-7821 completed in 230ms",
                "Queue depth: 14 pending jobs",
                "Worker pool: 4/8 threads active",
                "Heartbeat OK — redis latency 2ms"
            ],
            TransientErrorMessages =
            [
                "Redis ETIMEDOUT: connection to 10.0.1.3:6379 timed out after 3000ms",
                "Job order-process-7834 failed: connection pool exhausted",
                "OOM: worker heap exceeded 512MB limit, killing job batch-report-42",
                "Redis ECONNREFUSED: cannot connect to 10.0.1.3:6379"
            ]
        },
        new ServiceDefinition
        {
            Name = "reporting-backend",
            Role = "Aggregation, scheduled reports",
            InfraDependencies = ["postgresql", "mongodb"],
            UpstreamServices = ["background-worker"],
            BaseErrorRate = 0.02f,
            NormalMessages =
            [
                "Report daily-sales-2025-07-14 generated in 1200ms",
                "PostgreSQL query: SELECT aggregated_metrics completed in 45ms",
                "MongoDB find: report_templates returned 3 docs in 12ms",
                "Scheduled report cycle started",
                "Cache hit for report template monthly-revenue"
            ],
            TransientErrorMessages =
            [
                "PostgreSQL slow query: SELECT aggregated_metrics took 12400ms (threshold: 5000ms)",
                "MongoDB error: document validation failed — schema mismatch on field 'revenue_cents'",
                "Report daily-sales timed out waiting for upstream data from background-worker",
                "PostgreSQL connection pool exhausted (max: 20, active: 20, waiting: 8)"
            ]
        },
        new ServiceDefinition
        {
            Name = "iot-ingestion",
            Role = "Device telemetry, event routing",
            InfraDependencies = ["mqtt"],
            UpstreamServices = [],
            BaseErrorRate = 0.04f,
            NormalMessages =
            [
                "MQTT: received telemetry from device sensor-th-042 (temp: 22.4°C)",
                "Event routed: device-alert → notification-queue",
                "Telemetry batch: 128 events ingested in 340ms",
                "MQTT broker connection healthy — latency 4ms",
                "Device registry: 847 active devices"
            ],
            TransientErrorMessages =
            [
                "MQTT broker disconnected: connection reset by peer",
                "Telemetry batch failed: broker not available",
                "Device sensor-th-099 telemetry dropped: queue full",
                "MQTT reconnect attempt 3/5 — backoff 8000ms"
            ]
        }
    ];

    /// <summary>
    /// Dependency edges: service → services it calls.
    /// Used by pattern detector to understand cascade paths.
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> DependencyGraph { get; } =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["api-gateway"] = ["background-worker", "reporting-backend"],
            ["background-worker"] = [],
            ["reporting-backend"] = ["background-worker"],
            ["iot-ingestion"] = []
        };

    /// <summary>
    /// Infrastructure component → services that depend on it.
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> InfraConsumers { get; } =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["redis"] = ["background-worker"],
            ["postgresql"] = ["reporting-backend"],
            ["mongodb"] = ["reporting-backend"],
            ["mqtt"] = ["iot-ingestion"]
        };
}
