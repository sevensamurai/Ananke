using Ananke.Learning;

using Ananke.Learning.EmpiricalMemory;

namespace LogEventsDemo;

/// <summary>
/// Seeds <see cref="IEmpiricalMemory"/> with static architectural knowledge:
/// system topology, known failure modes, and runbook fragments.
/// All entries are committed as <see cref="EmpiricalKind.Heuristic"/> with
/// Source="wiki".
/// </summary>
internal static class KnowledgeSeeder
{
    /// <summary>
    /// Loads all static knowledge entries into empirical memory.
    /// Called once at startup before the simulation begins.
    /// </summary>
    internal static async Task SeedAsync(IEmpiricalMemory memory)
    {
        var entries = BuildEntries();
        foreach (var entry in entries)
            await memory.CommitAsync(entry);
    }

    private static List<EmpiricalEntry> BuildEntries()
    {
        var now = DateTimeOffset.UtcNow;
        var entries = new List<EmpiricalEntry>();

        // ── Architecture: service dependency relationships ────────────
        entries.Add(new EmpiricalEntry
        {
            Id = "wiki-arch-api-gateway",
            Kind = EmpiricalKind.Heuristic,
            Tags = ["architecture", "api-gateway"],
            Source = "wiki",
            Description = new SemanticDescription
            {
                Summary = "api-gateway depends on background-worker and reporting-backend for upstream requests",
                SemanticTags = new Dictionary<string, float>
                {
                    ["service:api-gateway"] = 1.0f,
                    ["dependency:api-gateway→background-worker"] = 0.9f,
                    ["dependency:api-gateway→reporting-backend"] = 0.9f,
                    ["role:http-endpoint"] = 0.7f
                }
            },
            Confidence = 0.9f,
            ObservationCount = 1,
            Evidence = ["System architecture document"],
            FirstObserved = now,
            LastObserved = now,
            Situation = "Understanding API gateway failure modes",
            PreferredApproach = "Check upstream services (background-worker, reporting-backend) when api-gateway returns 5xx"
        });

        entries.Add(new EmpiricalEntry
        {
            Id = "wiki-arch-background-worker",
            Kind = EmpiricalKind.Heuristic,
            Tags = ["architecture", "background-worker", "redis"],
            Source = "wiki",
            Description = new SemanticDescription
            {
                Summary = "background-worker depends on redis for queue and cache; processes async jobs",
                SemanticTags = new Dictionary<string, float>
                {
                    ["service:background-worker"] = 1.0f,
                    ["infra:redis"] = 0.9f,
                    ["role:queue-consumer"] = 0.8f,
                    ["failure-mode:connection-pool"] = 0.6f,
                    ["failure-mode:oom"] = 0.6f
                }
            },
            Confidence = 0.9f,
            ObservationCount = 1,
            Evidence = ["System architecture document"],
            FirstObserved = now,
            LastObserved = now,
            Situation = "Understanding background-worker failure modes",
            PreferredApproach = "Check redis connectivity and queue depth when worker jobs fail"
        });

        entries.Add(new EmpiricalEntry
        {
            Id = "wiki-arch-reporting",
            Kind = EmpiricalKind.Heuristic,
            Tags = ["architecture", "reporting-backend", "postgresql", "mongodb"],
            Source = "wiki",
            Description = new SemanticDescription
            {
                Summary = "reporting-backend depends on postgresql (relational) and mongodb (document store)",
                SemanticTags = new Dictionary<string, float>
                {
                    ["service:reporting-backend"] = 1.0f,
                    ["infra:postgresql"] = 0.9f,
                    ["infra:mongodb"] = 0.8f,
                    ["role:aggregation"] = 0.7f,
                    ["dependency:reporting-backend→background-worker"] = 0.6f
                }
            },
            Confidence = 0.9f,
            ObservationCount = 1,
            Evidence = ["System architecture document"],
            FirstObserved = now,
            LastObserved = now,
            Situation = "Understanding reporting-backend failure modes",
            PreferredApproach = "Check PostgreSQL query performance and MongoDB schema compatibility"
        });

        entries.Add(new EmpiricalEntry
        {
            Id = "wiki-arch-iot",
            Kind = EmpiricalKind.Heuristic,
            Tags = ["architecture", "iot-ingestion", "mqtt"],
            Source = "wiki",
            Description = new SemanticDescription
            {
                Summary = "iot-ingestion depends on mqtt broker for device telemetry and event routing",
                SemanticTags = new Dictionary<string, float>
                {
                    ["service:iot-ingestion"] = 1.0f,
                    ["infra:mqtt"] = 0.9f,
                    ["role:telemetry-ingestion"] = 0.8f,
                    ["failure-mode:broker-disconnect"] = 0.7f
                }
            },
            Confidence = 0.9f,
            ObservationCount = 1,
            Evidence = ["System architecture document"],
            FirstObserved = now,
            LastObserved = now,
            Situation = "Understanding iot-ingestion failure modes",
            PreferredApproach = "Check MQTT broker connectivity when telemetry stops flowing"
        });

        // ── Known failure modes (runbook fragments) ──────────────────
        entries.Add(new EmpiricalEntry
        {
            Id = "wiki-failure-redis-pool",
            Kind = EmpiricalKind.Heuristic,
            Tags = ["runbook", "redis", "connection-pool"],
            Source = "wiki",
            Description = new SemanticDescription
            {
                Summary = "Redis connection pool exhaustion causes worker timeouts cascading to API 503s",
                SemanticTags = new Dictionary<string, float>
                {
                    ["cause:connection-pool-exhaustion"] = 1.0f,
                    ["infra:redis"] = 1.0f,
                    ["service:background-worker"] = 0.9f,
                    ["error:etimedout"] = 0.8f,
                    ["effect:upstream-timeout"] = 0.7f,
                    ["service:api-gateway"] = 0.5f
                }
            },
            Confidence = 0.8f,
            ObservationCount = 1,
            Evidence = ["Post-mortem: 2025-03-incident-redis-pool"],
            FirstObserved = now,
            LastObserved = now,
            Situation = "Redis ETIMEDOUT errors appearing in background-worker logs",
            PreferredApproach = "Increase redis maxpool, add connection pool monitoring alert",
            AvoidedApproach = "Restarting workers without fixing pool size"
        });

        entries.Add(new EmpiricalEntry
        {
            Id = "wiki-failure-pg-slow",
            Kind = EmpiricalKind.Heuristic,
            Tags = ["runbook", "postgresql", "slow-query"],
            Source = "wiki",
            Description = new SemanticDescription
            {
                Summary = "PostgreSQL slow queries exhaust connection pool causing report timeouts",
                SemanticTags = new Dictionary<string, float>
                {
                    ["cause:slow-query"] = 1.0f,
                    ["infra:postgresql"] = 1.0f,
                    ["service:reporting-backend"] = 0.9f,
                    ["error:pool_exhausted"] = 0.8f,
                    ["effect:report-timeout"] = 0.7f
                }
            },
            Confidence = 0.8f,
            ObservationCount = 1,
            Evidence = ["Post-mortem: 2025-04-pg-slow-query-cascade"],
            FirstObserved = now,
            LastObserved = now,
            Situation = "PostgreSQL queries exceeding 5s threshold",
            PreferredApproach = "Add missing index, tune query planner settings",
            AvoidedApproach = "Increasing timeout without fixing underlying query"
        });

        entries.Add(new EmpiricalEntry
        {
            Id = "wiki-failure-schema",
            Kind = EmpiricalKind.Heuristic,
            Tags = ["runbook", "mongodb", "schema-mismatch", "deploy"],
            Source = "wiki",
            Description = new SemanticDescription
            {
                Summary = "MongoDB schema mismatch after deploy causes deserialization failures in reporting",
                SemanticTags = new Dictionary<string, float>
                {
                    ["cause:schema-mismatch"] = 1.0f,
                    ["infra:mongodb"] = 1.0f,
                    ["service:reporting-backend"] = 0.9f,
                    ["error:schema_mismatch"] = 0.8f,
                    ["effect:internal-error"] = 0.7f
                }
            },
            Confidence = 0.8f,
            ObservationCount = 1,
            Evidence = ["Post-mortem: 2025-05-mongodb-schema-deploy"],
            FirstObserved = now,
            LastObserved = now,
            Situation = "MongoDB document validation errors after a deploy",
            PreferredApproach = "Run schema migration before deploy; use versioned document schemas",
            AvoidedApproach = "Deploying without backward-compatible schema changes"
        });

        entries.Add(new EmpiricalEntry
        {
            Id = "wiki-failure-oom",
            Kind = EmpiricalKind.Heuristic,
            Tags = ["runbook", "oom", "background-worker"],
            Source = "wiki",
            Description = new SemanticDescription
            {
                Summary = "Unbounded queue growth causes worker OOM kills, cascading to API timeouts",
                SemanticTags = new Dictionary<string, float>
                {
                    ["cause:oom-unbounded-queue"] = 1.0f,
                    ["service:background-worker"] = 1.0f,
                    ["error:oom_kill"] = 0.9f,
                    ["infra:redis"] = 0.6f,
                    ["effect:upstream-timeout"] = 0.7f,
                    ["service:api-gateway"] = 0.5f
                }
            },
            Confidence = 0.8f,
            ObservationCount = 1,
            Evidence = ["Post-mortem: 2025-02-worker-oom"],
            FirstObserved = now,
            LastObserved = now,
            Situation = "Worker memory approaching limit with rising queue depth",
            PreferredApproach = "Add queue depth limit and backpressure; scale workers horizontally"
        });

        return entries;
    }
}
