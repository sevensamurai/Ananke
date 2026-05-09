using Ananke.Learning;

using Ananke.Learning.EmpiricalMemory;

namespace LogEventsDemo;

/// <summary>
/// Rule-based sliding-window detector that analyzes log event streams and
/// produces <see cref="EmpiricalEntry"/> (Kind=Pattern) when correlations
/// are detected. No LLM — all detection is structural.
/// </summary>
/// <remarks>
/// Detects:
/// <list type="bullet">
///   <item>Temporal co-occurrence of errors within configurable windows</item>
///   <item>Error-rate spikes per service</item>
///   <item>Service-correlated error clustering (cascade detection)</item>
/// </list>
/// </remarks>
internal sealed class RuleBasedPatternDetector
{
    private readonly IEmpiricalMemory _memory;
    private readonly TimeSpan _windowSize;
    private int _patternCounter;

    internal RuleBasedPatternDetector(IEmpiricalMemory memory, TimeSpan? windowSize = null)
    {
        _memory = memory;
        _windowSize = windowSize ?? TimeSpan.FromSeconds(15);
    }

    /// <summary>
    /// Scans all log events and detects patterns, committing them to empirical memory.
    /// Returns the number of patterns detected.
    /// </summary>
    internal async Task<int> DetectAsync(IReadOnlyList<LogEvent> events, CancellationToken ct = default)
    {
        var detected = 0;

        detected += await DetectCascadesAsync(events, ct);
        detected += await DetectErrorSpikesAsync(events, ct);
        detected += await DetectCoOccurrenceAsync(events, ct);

        return detected;
    }

    /// <summary>
    /// Detects cascading failures: errors in service A followed by errors in
    /// a dependent service B within the window.
    /// </summary>
    private async Task<int> DetectCascadesAsync(IReadOnlyList<LogEvent> events, CancellationToken ct)
    {
        var detected = 0;
        var errorEvents = events
            .Where(e => e.Level >= LogLevel.Error)
            .OrderBy(e => e.Timestamp)
            .ToList();

        // Group errors by correlation ID for cascade detection
        var correlationGroups = errorEvents
            .Where(e => e.CorrelationId is not null)
            .GroupBy(e => e.CorrelationId!)
            .Where(g => g.Select(e => e.Service).Distinct().Count() > 1);

        foreach (var group in correlationGroups)
        {
            if (ct.IsCancellationRequested) break;

            var groupEvents = group.OrderBy(e => e.Timestamp).ToList();
            var first = groupEvents[0];
            var last = groupEvents[^1];

            if (last.Timestamp - first.Timestamp > _windowSize)
                continue;

            var services = groupEvents.Select(e => e.Service).Distinct().ToList();
            var tags = LogTagExtractor.ExtractWindowTags(groupEvents);
            tags[$"pattern:cascade"] = 1.0f;
            tags[$"cascade:{string.Join("→", services)}"] = 0.9f;

            var condition = $"{first.Service} {first.Level}: {Truncate(first.Message, 60)}";
            var effect = $"{last.Service} {last.Level}: {Truncate(last.Message, 60)}";
            var latency = last.Timestamp - first.Timestamp;

            var entry = new EmpiricalEntry
            {
                Id = $"pattern-cascade-{Interlocked.Increment(ref _patternCounter)}",
                Kind = EmpiricalKind.Pattern,
                Tags = ["auto-detected", "cascade", .. services],
                Source = "auto-detected",
                Description = new SemanticDescription
                {
                    Summary = $"Cascade: {string.Join(" → ", services)} ({latency.TotalSeconds:F1}s)",
                    SemanticTags = tags
                },
                Confidence = 0.4f,
                ObservationCount = 1,
                Evidence = [$"Correlated errors {first.CorrelationId}: {services.Count} services, {latency.TotalSeconds:F1}s span"],
                FirstObserved = first.Timestamp,
                LastObserved = last.Timestamp,
                Condition = condition,
                Effect = effect,
                Latency = latency
            };

            await _memory.CommitAsync(entry, ct);
            detected++;
        }

        return detected;
    }

    /// <summary>
    /// Detects error-rate spikes: a service producing significantly more errors
    /// than its baseline within a time window.
    /// </summary>
    private async Task<int> DetectErrorSpikesAsync(IReadOnlyList<LogEvent> events, CancellationToken ct)
    {
        var detected = 0;

        // Partition events into windows
        if (events.Count == 0) return 0;

        var start = events[0].Timestamp;
        var end = events[^1].Timestamp;
        var windowCount = (int)Math.Ceiling((end - start).TotalSeconds / _windowSize.TotalSeconds);

        for (var w = 0; w < windowCount && !ct.IsCancellationRequested; w++)
        {
            var wStart = start + w * _windowSize;
            var wEnd = wStart + _windowSize;

            var windowEvents = events.Where(e => e.Timestamp >= wStart && e.Timestamp < wEnd).ToList();
            if (windowEvents.Count == 0) continue;

            foreach (var svc in SystemTopology.Services)
            {
                var svcEvents = windowEvents.Where(e => e.Service == svc.Name).ToList();
                if (svcEvents.Count == 0) continue;

                var errorCount = svcEvents.Count(e => e.Level >= LogLevel.Error);
                var errorRate = (float)errorCount / svcEvents.Count;

                // Spike = error rate > 3x base rate and at least 3 errors
                if (errorRate > svc.BaseErrorRate * 3 && errorCount >= 3)
                {
                    var tags = LogTagExtractor.ExtractWindowTags(
                        svcEvents.Where(e => e.Level >= LogLevel.Error));
                    tags[$"pattern:error-spike"] = 1.0f;
                    tags[$"service:{svc.Name}"] = 1.0f;

                    var entry = new EmpiricalEntry
                    {
                        Id = $"pattern-spike-{Interlocked.Increment(ref _patternCounter)}",
                        Kind = EmpiricalKind.Pattern,
                        Tags = ["auto-detected", "error-spike", svc.Name],
                        Source = "auto-detected",
                        Description = new SemanticDescription
                        {
                            Summary = $"Error spike in {svc.Name}: {errorRate:P0} error rate ({errorCount} errors in {_windowSize.TotalSeconds}s window)",
                            SemanticTags = tags
                        },
                        Confidence = 0.5f,
                        ObservationCount = 1,
                        Evidence = [$"Window {wStart:HH:mm:ss}–{wEnd:HH:mm:ss}: {errorCount}/{svcEvents.Count} events were errors"],
                        FirstObserved = wStart,
                        LastObserved = wEnd,
                        Condition = $"{svc.Name} error rate rises to {errorRate:P0}",
                        Effect = $"{errorCount} errors in {_windowSize.TotalSeconds}s"
                    };

                    await _memory.CommitAsync(entry, ct);
                    detected++;
                }
            }
        }

        return detected;
    }

    /// <summary>
    /// Detects temporal co-occurrence: different error codes appearing together
    /// within the window, suggesting a common cause.
    /// </summary>
    private async Task<int> DetectCoOccurrenceAsync(IReadOnlyList<LogEvent> events, CancellationToken ct)
    {
        var detected = 0;

        var errorEvents = events
            .Where(e => e.Level >= LogLevel.Error && e.Fields.ContainsKey("error_code"))
            .OrderBy(e => e.Timestamp)
            .ToList();

        // Sliding window: find pairs of distinct error codes from different services
        for (var i = 0; i < errorEvents.Count && !ct.IsCancellationRequested; i++)
        {
            var anchor = errorEvents[i];
            var windowEnd = anchor.Timestamp + _windowSize;

            var coOccurring = errorEvents
                .Where(e => e != anchor
                    && e.Service != anchor.Service
                    && e.Timestamp >= anchor.Timestamp
                    && e.Timestamp <= windowEnd
                    && e.Fields.TryGetValue("error_code", out var code)
                    && code != anchor.Fields["error_code"])
                .ToList();

            if (coOccurring.Count == 0) continue;

            var partner = coOccurring[0]; // take first co-occurrence
            var allEvents = new[] { anchor, partner };
            var tags = LogTagExtractor.ExtractWindowTags(allEvents);
            tags["pattern:co-occurrence"] = 1.0f;

            var anchorCode = anchor.Fields["error_code"];
            var partnerCode = partner.Fields["error_code"];

            var entry = new EmpiricalEntry
            {
                Id = $"pattern-cooccur-{Interlocked.Increment(ref _patternCounter)}",
                Kind = EmpiricalKind.Pattern,
                Tags = ["auto-detected", "co-occurrence", anchor.Service, partner.Service],
                Source = "auto-detected",
                Description = new SemanticDescription
                {
                    Summary = $"Co-occurrence: {anchor.Service}/{anchorCode} + {partner.Service}/{partnerCode} within {_windowSize.TotalSeconds}s",
                    SemanticTags = tags
                },
                Confidence = 0.3f,
                ObservationCount = 1,
                Evidence = [$"{anchor.Service} {anchorCode} at {anchor.Timestamp:HH:mm:ss} + {partner.Service} {partnerCode} at {partner.Timestamp:HH:mm:ss}"],
                FirstObserved = anchor.Timestamp,
                LastObserved = partner.Timestamp,
                Condition = $"{anchor.Service} emits {anchorCode}",
                Effect = $"{partner.Service} emits {partnerCode} within {(partner.Timestamp - anchor.Timestamp).TotalSeconds:F1}s",
                Latency = partner.Timestamp - anchor.Timestamp
            };

            await _memory.CommitAsync(entry, ct);
            detected++;

            // Skip ahead past this window to avoid duplicate detections
            while (i + 1 < errorEvents.Count && errorEvents[i + 1].Timestamp <= windowEnd)
                i++;
        }

        return detected;
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : string.Concat(text.AsSpan(0, maxLen - 3), "...");
}
