using Ananke.Learning.EmpiricalMemory;

namespace LearningPrimitivesDemo.Knowledge;

/// <summary>
/// 31 fixture entries across three implicit topics plus one bridge entry.
/// Topics deliberately share a single bridge tag so multi-hop graph
/// expansion can surface cross-topic entries that embedding-only recall misses.
/// </summary>
internal static class TopicFixture
{
    // ── Topic A — GC Pauses ──────────────────────────────────────────
    internal const string TagGcPause   = "cause/gc-pause";
    internal const string TagJvmFreeze = "effect/jvm-freeze";
    internal const string TagBackend   = "service/backend";

    // ── Topic B — DB Deadlocks ───────────────────────────────────────
    internal const string TagLockConten = "cause/lock-contention";
    internal const string TagDbTimeout  = "effect/db-timeout";
    internal const string TagDatabase   = "service/database";

    // ── Topic C — Network Flapping ───────────────────────────────────
    internal const string TagNicReset   = "cause/nic-reset";
    internal const string TagPacketLoss = "effect/packet-loss";
    internal const string TagGateway    = "service/gateway";

    // ── Bridge — connects Topic A and Topic C ────────────────────────
    /// <summary>
    /// The bridge tag appears in <see cref="BridgeEntry"/> alongside tags
    /// from both Topic A and Topic C.  It has low raw frequency (1 entry)
    /// but high PageRank because it is the only cross-cluster connector.
    /// </summary>
    internal const string TagHighLatency = "symptom/high-latency";

    // ── Bridge entry (also used as the multi-hop probe) ──────────────
    /// <summary>
    /// Probe entry for the multi-hop retrieval demo.  Tagged only with the
    /// bridge tag plus one tag from each of Topic A and C, making it the
    /// sole link between the two topic clusters in the co-occurrence graph.
    /// Valence = 0 so frequency-based importance ignores it entirely.
    /// </summary>
    internal static EmpiricalEntry BridgeEntry { get; } = MakeEntry(
        id: "bridge-01",
        summary: "High latency observed — correlates with both GC pauses and NIC resets",
        tags: new Dictionary<string, float>
        {
            [TagHighLatency] = 1.0f,
            [TagGcPause]     = 0.7f,
            [TagNicReset]    = 0.7f,
        },
        valence: 0f,          // neutral — excluded from valence-based importance
        confidence: 0.65f,
        observationCount: 1);

    // ── All fixture entries ──────────────────────────────────────────

    /// <summary>Creates all 31 fixture entries (30 topic entries + bridge).</summary>
    internal static IReadOnlyList<EmpiricalEntry> CreateAll()
    {
        var entries = new List<EmpiricalEntry>(31);

        // Topic A — 6 positive, 4 negative valence
        for (var i = 0; i < 10; i++)
        {
            entries.Add(MakeEntry(
                $"gc-{i:D2}",
                $"GC pause incident {i}: backend JVM heap pressure causes freeze",
                new Dictionary<string, float>
                {
                    [TagGcPause]   = 0.9f,
                    [TagJvmFreeze] = 0.8f,
                    [TagBackend]   = 0.7f,
                },
                valence: i < 6 ? 0.8f : -0.6f,
                confidence: 0.80f, observationCount: 5));
        }

        // Topic B — 8 positive, 2 negative valence
        for (var i = 0; i < 10; i++)
        {
            entries.Add(MakeEntry(
                $"db-{i:D2}",
                $"DB deadlock incident {i}: lock contention causes query timeout",
                new Dictionary<string, float>
                {
                    [TagLockConten] = 0.9f,
                    [TagDbTimeout]  = 0.8f,
                    [TagDatabase]   = 0.7f,
                },
                valence: i < 8 ? 0.9f : -0.3f,
                confidence: 0.85f, observationCount: 5));
        }

        // Topic C — 5 positive, 5 negative valence
        for (var i = 0; i < 10; i++)
        {
            entries.Add(MakeEntry(
                $"net-{i:D2}",
                $"Network flap incident {i}: NIC reset causes packet loss at gateway",
                new Dictionary<string, float>
                {
                    [TagNicReset]   = 0.9f,
                    [TagPacketLoss] = 0.8f,
                    [TagGateway]    = 0.7f,
                },
                valence: i < 5 ? 0.7f : -0.7f,
                confidence: 0.75f, observationCount: 5));
        }

        entries.Add(BridgeEntry);
        return entries;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static EmpiricalEntry MakeEntry(
        string id,
        string summary,
        Dictionary<string, float> tags,
        float valence,
        float confidence,
        int observationCount) =>
        new()
        {
            Id               = id,
            Kind             = EmpiricalKind.Pattern,
            Tags             = [.. tags.Keys],
            Source           = "fixture",
            Description      = new SemanticDescription
            {
                Summary      = summary,
                SemanticTags = tags,
            },
            Confidence       = confidence,
            ObservationCount = observationCount,
            Evidence         = [],
            FirstObserved    = DateTimeOffset.UtcNow.AddDays(-30),
            LastObserved     = DateTimeOffset.UtcNow,
            Valence          = valence,
        };
}
