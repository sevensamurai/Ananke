using Ananke.Orchestration.Tools;
using Ananke.Federation.Execution;

namespace Ananke.Federation.LocalEmulators;

/// <summary>
/// Stub emulators for Google Cloud data-service capabilities
/// (<c>bigquery</c>, <c>spanner</c>, <c>bigtable</c>, <c>pubsub</c>,
/// <c>maps</c>, <c>artifact_service</c>) on Vertex AI / Gemini Enterprise.
/// Each returns deterministic fixture data. No real Google Cloud calls are made.
/// </summary>
internal sealed class GoogleDataServiceExecutor : IPlatformNativeExecutor
{
    private readonly string _capability;

    public GoogleDataServiceExecutor(string capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        _capability = capability;
    }

    public string Capability => _capability;
    public bool IsStub => true;

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var result = _capability switch
        {
            "bigquery"         => BigQueryFixture(args),
            "spanner"          => SpannerFixture(args),
            "bigtable"         => BigtableFixture(args),
            "pubsub"           => PubSubFixture(args),
            "maps"             => MapsFixture(args),
            "artifact_service" => ArtifactServiceFixture(args),
            _                  => (object)new { capability = _capability, note = "[STUB] No fixture defined." }
        };
        return Task.FromResult(ToolResult.Json(result));
    }

    private static object BigQueryFixture(IReadOnlyDictionary<string, object?> args)
    {
        var sql = args.TryGetValue("query", out var q) ? q?.ToString() ?? "" : "";
        return new
        {
            query = sql,
            rows = new[] { new { col1 = "fixture-a", col2 = 42 }, new { col1 = "fixture-b", col2 = 99 } },
            totalRows = 2,
            note = "[STUB] BigQuery fixture data."
        };
    }

    private static object SpannerFixture(IReadOnlyDictionary<string, object?> args) => new
    {
        rows = new[] { new { id = "row-1", value = "spanner-fixture" } },
        note = "[STUB] Spanner fixture data."
    };

    private static object BigtableFixture(IReadOnlyDictionary<string, object?> args) => new
    {
        rows = new[] { new { rowKey = "rk-001", family = "cf1", qualifier = "q1", value = "bigtable-fixture" } },
        note = "[STUB] Bigtable fixture data."
    };

    private static object PubSubFixture(IReadOnlyDictionary<string, object?> args)
    {
        var message = args.TryGetValue("message", out var m) ? m?.ToString() ?? "" : "";
        return new { messageId = "msg-fixture-001", message, note = "[STUB] PubSub fixture." };
    }

    private static object MapsFixture(IReadOnlyDictionary<string, object?> args)
    {
        var query = args.TryGetValue("address", out var a) ? a?.ToString() ?? "" : "";
        return new
        {
            query,
            lat = 37.4221,
            lng = -122.0841,
            formattedAddress = "1600 Amphitheatre Pkwy, Mountain View, CA 94043",
            note = "[STUB] Maps fixture."
        };
    }

    private static object ArtifactServiceFixture(IReadOnlyDictionary<string, object?> args) => new
    {
        artifactId = "artifact-fixture-001",
        uri = "gs://ananke-local-emulator/fixture-artifact",
        note = "[STUB] Artifact Service fixture."
    };
}
