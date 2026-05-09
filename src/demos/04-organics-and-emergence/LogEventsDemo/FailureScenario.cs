namespace LogEventsDemo;

/// <summary>
/// Declarative failure scenario describing a cascade across the simulated system.
/// The simulator activates scenarios stochastically or on schedule, producing
/// correlated error streams across multiple services with causal delays.
/// </summary>
internal sealed record FailureScenario
{
    /// <summary>Human-readable scenario name.</summary>
    public required string Name { get; init; }

    /// <summary>Probability of this scenario triggering per simulation tick (0–1).</summary>
    public required float TriggerProbability { get; init; }

    /// <summary>Ordered cascade stages: each stage produces errors in one service after a delay.</summary>
    public required IReadOnlyList<CascadeStage> Stages { get; init; }

    /// <summary>Root cause tag used for empirical entries.</summary>
    public required string CauseTag { get; init; }

    /// <summary>Infrastructure component at the root of the failure (if any).</summary>
    public string? InfraTag { get; init; }
}

/// <summary>One stage in a failure cascade.</summary>
internal sealed record CascadeStage
{
    /// <summary>Service that emits errors at this stage.</summary>
    public required string Service { get; init; }

    /// <summary>Delay from scenario start before this stage fires.</summary>
    public required TimeSpan Delay { get; init; }

    /// <summary>Log severity for errors at this stage.</summary>
    public required LogLevel Level { get; init; }

    /// <summary>Error messages emitted at this stage.</summary>
    public required IReadOnlyList<string> Messages { get; init; }

    /// <summary>Structured fields attached to each error log.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();

    /// <summary>Number of error log events to emit at this stage.</summary>
    public int EventCount { get; init; } = 1;
}

/// <summary>Pre-built failure scenarios for this demo.</summary>
internal static class FailureScenarios
{
    internal static IReadOnlyList<FailureScenario> All { get; } =
    [
        // 1. Redis connection pool exhaustion → Worker timeout → API 503
        new FailureScenario
        {
            Name = "Redis Connection Pool Exhaustion",
            TriggerProbability = 0.08f,
            CauseTag = "connection-pool-exhaustion",
            InfraTag = "redis",
            Stages =
            [
                new CascadeStage
                {
                    Service = "background-worker",
                    Delay = TimeSpan.Zero,
                    Level = LogLevel.Error,
                    Messages = ["Redis ETIMEDOUT: connection to 10.0.1.3:6379 timed out after 3000ms",
                                "Redis connection pool exhausted (max: 25, active: 25, waiting: 12)"],
                    Fields = new Dictionary<string, string>
                    {
                        ["error_code"] = "ETIMEDOUT",
                        ["infra"] = "redis",
                        ["pool_active"] = "25",
                        ["pool_max"] = "25"
                    },
                    EventCount = 3
                },
                new CascadeStage
                {
                    Service = "background-worker",
                    Delay = TimeSpan.FromSeconds(2),
                    Level = LogLevel.Critical,
                    Messages = ["Job processing halted: no redis connections available"],
                    Fields = new Dictionary<string, string>
                    {
                        ["error_code"] = "POOL_EXHAUSTED",
                        ["infra"] = "redis"
                    }
                },
                new CascadeStage
                {
                    Service = "api-gateway",
                    Delay = TimeSpan.FromSeconds(5),
                    Level = LogLevel.Error,
                    Messages = ["Upstream timeout: background-worker did not respond within 5000ms",
                                "GET /api/v1/orders 503 Service Unavailable"],
                    Fields = new Dictionary<string, string>
                    {
                        ["error_code"] = "UPSTREAM_TIMEOUT",
                        ["upstream"] = "background-worker"
                    },
                    EventCount = 2
                }
            ]
        },

        // 2. PostgreSQL slow query cascade
        new FailureScenario
        {
            Name = "PostgreSQL Slow Query Cascade",
            TriggerProbability = 0.06f,
            CauseTag = "slow-query",
            InfraTag = "postgresql",
            Stages =
            [
                new CascadeStage
                {
                    Service = "reporting-backend",
                    Delay = TimeSpan.Zero,
                    Level = LogLevel.Warning,
                    Messages = ["PostgreSQL slow query: SELECT aggregated_metrics took 12400ms (threshold: 5000ms)"],
                    Fields = new Dictionary<string, string>
                    {
                        ["error_code"] = "SLOW_QUERY",
                        ["infra"] = "postgresql",
                        ["duration_ms"] = "12400"
                    }
                },
                new CascadeStage
                {
                    Service = "reporting-backend",
                    Delay = TimeSpan.FromSeconds(3),
                    Level = LogLevel.Error,
                    Messages = ["PostgreSQL connection pool exhausted (max: 20, active: 20, waiting: 8)",
                                "Report daily-sales-2025-07-14 failed: query timeout after 30000ms"],
                    Fields = new Dictionary<string, string>
                    {
                        ["error_code"] = "POOL_EXHAUSTED",
                        ["infra"] = "postgresql",
                        ["pool_active"] = "20",
                        ["pool_max"] = "20"
                    },
                    EventCount = 2
                },
                new CascadeStage
                {
                    Service = "api-gateway",
                    Delay = TimeSpan.FromSeconds(8),
                    Level = LogLevel.Error,
                    Messages = ["GET /api/v1/reports/daily 504 Gateway Timeout"],
                    Fields = new Dictionary<string, string>
                    {
                        ["error_code"] = "GATEWAY_TIMEOUT",
                        ["upstream"] = "reporting-backend"
                    }
                }
            ]
        },

        // 3. MongoDB schema mismatch after deploy
        new FailureScenario
        {
            Name = "MongoDB Schema Mismatch After Deploy",
            TriggerProbability = 0.05f,
            CauseTag = "schema-mismatch",
            InfraTag = "mongodb",
            Stages =
            [
                new CascadeStage
                {
                    Service = "reporting-backend",
                    Delay = TimeSpan.Zero,
                    Level = LogLevel.Error,
                    Messages = ["MongoDB error: document validation failed — schema mismatch on field 'revenue_cents'",
                                "Report template 'monthly-revenue' cannot be deserialized: missing field 'currency_code'"],
                    Fields = new Dictionary<string, string>
                    {
                        ["error_code"] = "SCHEMA_MISMATCH",
                        ["infra"] = "mongodb",
                        ["collection"] = "report_templates",
                        ["deploy"] = "v2.4.1"
                    },
                    EventCount = 4
                },
                new CascadeStage
                {
                    Service = "api-gateway",
                    Delay = TimeSpan.FromSeconds(1),
                    Level = LogLevel.Error,
                    Messages = ["GET /api/v1/reports/monthly 500 Internal Server Error"],
                    Fields = new Dictionary<string, string>
                    {
                        ["error_code"] = "INTERNAL_ERROR",
                        ["upstream"] = "reporting-backend"
                    },
                    EventCount = 2
                }
            ]
        },

        // 4. MQTT broker disconnect
        new FailureScenario
        {
            Name = "MQTT Broker Disconnect",
            TriggerProbability = 0.07f,
            CauseTag = "broker-disconnect",
            InfraTag = "mqtt",
            Stages =
            [
                new CascadeStage
                {
                    Service = "iot-ingestion",
                    Delay = TimeSpan.Zero,
                    Level = LogLevel.Error,
                    Messages = ["MQTT broker disconnected: connection reset by peer"],
                    Fields = new Dictionary<string, string>
                    {
                        ["error_code"] = "ECONNRESET",
                        ["infra"] = "mqtt"
                    }
                },
                new CascadeStage
                {
                    Service = "iot-ingestion",
                    Delay = TimeSpan.FromSeconds(2),
                    Level = LogLevel.Warning,
                    Messages = ["MQTT reconnect attempt 1/5 — backoff 2000ms",
                                "Telemetry batch failed: broker not available",
                                "Device telemetry dropped: 64 events lost"],
                    Fields = new Dictionary<string, string>
                    {
                        ["error_code"] = "BROKER_UNAVAILABLE",
                        ["infra"] = "mqtt",
                        ["events_lost"] = "64"
                    },
                    EventCount = 3
                },
                new CascadeStage
                {
                    Service = "iot-ingestion",
                    Delay = TimeSpan.FromSeconds(10),
                    Level = LogLevel.Info,
                    Messages = ["MQTT reconnected to broker after 10s downtime"],
                    Fields = new Dictionary<string, string>
                    {
                        ["infra"] = "mqtt",
                        ["downtime_s"] = "10"
                    }
                }
            ]
        },

        // 5. Worker OOM from unbounded queue
        new FailureScenario
        {
            Name = "Worker OOM from Unbounded Queue",
            TriggerProbability = 0.04f,
            CauseTag = "oom-unbounded-queue",
            InfraTag = "redis",
            Stages =
            [
                new CascadeStage
                {
                    Service = "background-worker",
                    Delay = TimeSpan.Zero,
                    Level = LogLevel.Warning,
                    Messages = ["Queue depth rising: 1,247 pending jobs (threshold: 500)",
                                "Worker memory: 487MB / 512MB limit"],
                    Fields = new Dictionary<string, string>
                    {
                        ["queue_depth"] = "1247",
                        ["memory_mb"] = "487",
                        ["memory_limit_mb"] = "512"
                    },
                    EventCount = 2
                },
                new CascadeStage
                {
                    Service = "background-worker",
                    Delay = TimeSpan.FromSeconds(4),
                    Level = LogLevel.Critical,
                    Messages = ["OOM: worker heap exceeded 512MB limit, killing job batch-report-42",
                                "Worker process restarting after OOM kill"],
                    Fields = new Dictionary<string, string>
                    {
                        ["error_code"] = "OOM_KILL",
                        ["memory_mb"] = "518",
                        ["memory_limit_mb"] = "512"
                    },
                    EventCount = 2
                },
                new CascadeStage
                {
                    Service = "api-gateway",
                    Delay = TimeSpan.FromSeconds(6),
                    Level = LogLevel.Error,
                    Messages = ["Upstream timeout: background-worker did not respond within 5000ms"],
                    Fields = new Dictionary<string, string>
                    {
                        ["error_code"] = "UPSTREAM_TIMEOUT",
                        ["upstream"] = "background-worker"
                    }
                }
            ]
        },

        // 6. API null reference from new deploy
        new FailureScenario
        {
            Name = "API Null Reference from New Deploy",
            TriggerProbability = 0.05f,
            CauseTag = "null-reference",
            Stages =
            [
                new CascadeStage
                {
                    Service = "api-gateway",
                    Delay = TimeSpan.Zero,
                    Level = LogLevel.Error,
                    Messages = ["NullReferenceException in OrderController.GetById: order.Customer was null",
                                "POST /api/v1/orders/validate 500 Internal Server Error"],
                    Fields = new Dictionary<string, string>
                    {
                        ["error_code"] = "NULL_REFERENCE",
                        ["exception"] = "NullReferenceException",
                        ["location"] = "OrderController.GetById",
                        ["deploy"] = "v3.1.0"
                    },
                    EventCount = 5
                },
                new CascadeStage
                {
                    Service = "api-gateway",
                    Delay = TimeSpan.FromSeconds(1),
                    Level = LogLevel.Warning,
                    Messages = ["Error rate spike: 23% of requests returning 500 (baseline: 0.5%)"],
                    Fields = new Dictionary<string, string>
                    {
                        ["error_rate"] = "0.23",
                        ["baseline_rate"] = "0.005"
                    }
                }
            ]
        },

        // 7. Reporting timeout from upstream latency
        new FailureScenario
        {
            Name = "Reporting Timeout from Upstream Latency",
            TriggerProbability = 0.05f,
            CauseTag = "upstream-latency",
            Stages =
            [
                new CascadeStage
                {
                    Service = "background-worker",
                    Delay = TimeSpan.Zero,
                    Level = LogLevel.Warning,
                    Messages = ["Job processing latency increased: avg 2400ms (baseline: 230ms)",
                                "Redis latency spike: GET operation took 1200ms"],
                    Fields = new Dictionary<string, string>
                    {
                        ["latency_ms"] = "2400",
                        ["baseline_ms"] = "230",
                        ["infra"] = "redis"
                    },
                    EventCount = 2
                },
                new CascadeStage
                {
                    Service = "reporting-backend",
                    Delay = TimeSpan.FromSeconds(5),
                    Level = LogLevel.Error,
                    Messages = ["Report daily-sales timed out waiting for upstream data from background-worker"],
                    Fields = new Dictionary<string, string>
                    {
                        ["error_code"] = "UPSTREAM_TIMEOUT",
                        ["upstream"] = "background-worker",
                        ["timeout_ms"] = "30000"
                    }
                },
                new CascadeStage
                {
                    Service = "api-gateway",
                    Delay = TimeSpan.FromSeconds(8),
                    Level = LogLevel.Error,
                    Messages = ["GET /api/v1/reports/daily 504 Gateway Timeout"],
                    Fields = new Dictionary<string, string>
                    {
                        ["error_code"] = "GATEWAY_TIMEOUT",
                        ["upstream"] = "reporting-backend"
                    }
                }
            ]
        }
    ];
}
