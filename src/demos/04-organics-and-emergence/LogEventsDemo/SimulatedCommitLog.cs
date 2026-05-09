namespace LogEventsDemo;

/// <summary>
/// Simulated Git commit history per service. Provides fake recent deploys
/// and code changes for the <c>commits</c> REPL command.
/// </summary>
internal static class SimulatedCommitLog
{
    internal sealed record CommitEntry(
        DateTimeOffset Timestamp,
        string Service,
        string Hash,
        string Author,
        string Message);

    /// <summary>Pre-built commit history for each service.</summary>
    internal static IReadOnlyList<CommitEntry> Commits { get; } =
    [
        // api-gateway
        new(new(2025, 7, 14, 6, 0, 0, TimeSpan.Zero), "api-gateway", "a3f1c2d", "alice",
            "feat: add customer field to order response"),
        new(new(2025, 7, 13, 14, 30, 0, TimeSpan.Zero), "api-gateway", "b7e4a9f", "bob",
            "fix: null check on order.Customer in GetById"),
        new(new(2025, 7, 12, 10, 0, 0, TimeSpan.Zero), "api-gateway", "c1d8f3e", "alice",
            "refactor: extract OrderController validation"),
        new(new(2025, 7, 10, 16, 45, 0, TimeSpan.Zero), "api-gateway", "d9a2b5c", "charlie",
            "deploy: v3.1.0 — new order validation endpoint"),

        // background-worker
        new(new(2025, 7, 14, 5, 0, 0, TimeSpan.Zero), "background-worker", "e4c7d1a", "bob",
            "chore: bump redis client to 2.8.1"),
        new(new(2025, 7, 13, 9, 0, 0, TimeSpan.Zero), "background-worker", "f2a9e8b", "charlie",
            "fix: add maxpool=25 to redis connection string"),
        new(new(2025, 7, 11, 11, 30, 0, TimeSpan.Zero), "background-worker", "a8b3c6d", "alice",
            "feat: batch processing for report jobs"),
        new(new(2025, 7, 9, 15, 0, 0, TimeSpan.Zero), "background-worker", "b5d1e4f", "bob",
            "fix: unbounded queue growth under load"),

        // reporting-backend
        new(new(2025, 7, 14, 4, 0, 0, TimeSpan.Zero), "reporting-backend", "c3f7a2e", "charlie",
            "deploy: v2.4.1 — new monthly-revenue template"),
        new(new(2025, 7, 13, 16, 0, 0, TimeSpan.Zero), "reporting-backend", "d6b1c9f", "alice",
            "feat: add currency_code to revenue reports"),
        new(new(2025, 7, 12, 8, 30, 0, TimeSpan.Zero), "reporting-backend", "e9a4d7b", "bob",
            "fix: add index on aggregated_metrics.report_date"),
        new(new(2025, 7, 10, 13, 0, 0, TimeSpan.Zero), "reporting-backend", "f1c8e3a", "charlie",
            "refactor: migrate report templates to new schema"),

        // iot-ingestion
        new(new(2025, 7, 14, 3, 0, 0, TimeSpan.Zero), "iot-ingestion", "a2d5b8c", "alice",
            "feat: add reconnect backoff for mqtt client"),
        new(new(2025, 7, 12, 12, 0, 0, TimeSpan.Zero), "iot-ingestion", "b4e1c6d", "bob",
            "fix: telemetry batch size limit to prevent OOM"),
        new(new(2025, 7, 11, 9, 0, 0, TimeSpan.Zero), "iot-ingestion", "c7f3a9e", "charlie",
            "chore: upgrade mqtt client library to 5.0.2")
    ];

    /// <summary>Gets recent commits for a service, most recent first.</summary>
    internal static IReadOnlyList<CommitEntry> GetForService(string service) =>
        Commits
            .Where(c => c.Service.Equals(service, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Timestamp)
            .ToList();
}
