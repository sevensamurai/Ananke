using Ananke.Learning;
using Ananke.Learning.Offline;

namespace LogEventsDemo;

/// <summary>
/// Console REPL for human-driven log investigation. Each command interacts
/// with <see cref="IEmpiricalMemory"/> — recording user attention, recalling
/// past patterns, and allowing reinforcement/contradiction of detected entries.
/// </summary>
internal sealed class Explorer
{
    private readonly LogSimulator _simulator;
    private readonly IEmpiricalMemory _memory;
    private readonly IOfflineLearner _learner;
    private readonly RuleBasedPatternDetector _detector;
    private DateTimeOffset _windowStart;
    private DateTimeOffset _windowEnd;

    /// <summary>Discoveries from the last offline learning cycle, shown at next prompt.</summary>
    private readonly List<string> _pendingDiscoveries = [];

    internal Explorer(
        LogSimulator simulator,
        IEmpiricalMemory memory,
        IOfflineLearner learner,
        RuleBasedPatternDetector detector)
    {
        _simulator = simulator;
        _memory = memory;
        _learner = learner;
        _detector = detector;

        // Default window: last 5 minutes of simulated time
        _windowEnd = simulator.CurrentTime;
        _windowStart = _windowEnd.AddMinutes(-5);
    }

    internal async Task RunAsync(CancellationToken ct = default)
    {
        PrintHelp();

        while (!ct.IsCancellationRequested)
        {
            // Surface discoveries from offline learning
            if (_pendingDiscoveries.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\n  💡 While you were away, I found:");
                foreach (var d in _pendingDiscoveries)
                    Console.WriteLine($"     • {d}");
                Console.ResetColor();
                _pendingDiscoveries.Clear();
            }

            Console.Write("\n  log> ");
            var line = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToLowerInvariant();
            var arg = parts.Length > 1 ? string.Join(' ', parts[1..]) : null;

            try
            {
                switch (cmd)
                {
                    case "tail":
                        CmdTail(arg, parts.Length > 2 ? parts[2] : null);
                        break;
                    case "grep":
                        CmdGrep(arg);
                        break;
                    case "timerange":
                        CmdTimeRange(parts);
                        break;
                    case "correlate":
                        await CmdCorrelateAsync(ct);
                        break;
                    case "investigate":
                        await CmdInvestigateAsync(arg, ct);
                        break;
                    case "commits":
                        CmdCommits(arg);
                        break;
                    case "arch":
                        CmdArch(arg);
                        break;
                    case "recall":
                        await CmdRecallAsync(arg, ct);
                        break;
                    case "confirm":
                        await CmdConfirmAsync(arg, ct);
                        break;
                    case "reject":
                        await CmdRejectAsync(arg, ct);
                        break;
                    case "learn":
                        await CmdLearnAsync(ct);
                        break;
                    case "status":
                        await CmdStatusAsync(ct);
                        break;
                    case "help":
                        PrintHelp();
                        break;
                    case "quit" or "exit":
                        return;
                    default:
                        Console.WriteLine($"  Unknown command: {cmd}. Type 'help' for available commands.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  Error: {ex.Message}");
                Console.ResetColor();
            }
        }
    }

    // ── Commands ─────────────────────────────────────────────────────

    private void CmdTail(string? service, string? countStr)
    {
        var count = 10;
        if (countStr is not null && int.TryParse(countStr, out var n)) count = n;
        if (service is not null && int.TryParse(service, out var n2))
        {
            count = n2;
            service = null;
        }

        var logs = _simulator.History
            .Where(e => service is null || e.Service.Equals(service, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .Reverse()
            .ToList();

        if (logs.Count == 0)
        {
            Console.WriteLine($"  No log events{(service is not null ? $" for {service}" : "")}.");
            return;
        }

        foreach (var log in logs)
            PrintLogEvent(log);
    }

    private void CmdGrep(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            Console.WriteLine("  Usage: grep <pattern> [service]");
            return;
        }

        var logs = _simulator.History
            .Where(e => e.Message.Contains(pattern, StringComparison.OrdinalIgnoreCase)
                || e.Fields.Values.Any(v => v.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(e => e.Timestamp)
            .Take(20)
            .Reverse()
            .ToList();

        Console.WriteLine($"  Found {logs.Count} matching events:");
        foreach (var log in logs)
            PrintLogEvent(log);
    }

    private void CmdTimeRange(string[] parts)
    {
        if (parts.Length < 3)
        {
            Console.WriteLine($"  Current window: {_windowStart:HH:mm:ss} – {_windowEnd:HH:mm:ss}");
            Console.WriteLine("  Usage: timerange <HH:mm:ss> <HH:mm:ss>");
            return;
        }

        if (TimeSpan.TryParse(parts[1], out var start) && TimeSpan.TryParse(parts[2], out var end))
        {
            var baseDate = _simulator.CurrentTime.Date;
            _windowStart = new DateTimeOffset(baseDate + start, _simulator.CurrentTime.Offset);
            _windowEnd = new DateTimeOffset(baseDate + end, _simulator.CurrentTime.Offset);
            Console.WriteLine($"  Window set: {_windowStart:HH:mm:ss} – {_windowEnd:HH:mm:ss}");
        }
        else
        {
            Console.WriteLine("  Invalid time format. Use HH:mm:ss.");
        }
    }

    private async Task CmdCorrelateAsync(CancellationToken ct)
    {
        var windowEvents = _simulator.History
            .Where(e => e.Timestamp >= _windowStart && e.Timestamp <= _windowEnd && e.Level >= LogLevel.Warning)
            .ToList();

        if (windowEvents.Count == 0)
        {
            Console.WriteLine("  No warning/error events in current time window.");
            return;
        }

        Console.WriteLine($"  Analyzing {windowEvents.Count} warning/error events in window...");

        var tags = LogTagExtractor.ExtractWindowTags(windowEvents);
        var situation = new SemanticDescription { SemanticTags = tags };

        var matches = await _memory.RecallAsync(situation.ToEmbeddingText(),
            new RecallOptions { TopK = 5, MinConfidence = 0.1f }, ct);

        if (matches.Count == 0)
        {
            Console.WriteLine("  No matching patterns found in empirical memory.");
            return;
        }

        Console.WriteLine($"\n  📋 Recalled {matches.Count} relevant entries:");
        foreach (var match in matches)
            PrintMatch(match);
    }

    private async Task CmdInvestigateAsync(string? entryId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entryId))
        {
            Console.WriteLine("  Usage: investigate <entry-id>");
            return;
        }

        var entry = await _memory.GetAsync(entryId, ct);
        if (entry is null)
        {
            Console.WriteLine($"  Entry '{entryId}' not found.");
            return;
        }

        PrintEntryDetail(entry);

        // Recall related entries
        var related = await _memory.RecallAsync(entry.Description.ToEmbeddingText(),
            new RecallOptions { TopK = 3, MinConfidence = 0.1f }, ct);

        var others = related.Where(m => m.Entry.Id != entry.Id).ToList();
        if (others.Count > 0)
        {
            Console.WriteLine("\n  🔗 Related entries:");
            foreach (var match in others)
                PrintMatch(match);
        }
    }

    private void CmdCommits(string? service)
    {
        if (string.IsNullOrWhiteSpace(service))
        {
            Console.WriteLine("  Usage: commits <service>");
            Console.WriteLine("  Services: api-gateway, background-worker, reporting-backend, iot-ingestion");
            return;
        }

        var commits = SimulatedCommitLog.GetForService(service);
        if (commits.Count == 0)
        {
            Console.WriteLine($"  No commits found for '{service}'.");
            return;
        }

        Console.WriteLine($"\n  📝 Recent commits for {service}:");
        foreach (var c in commits)
            Console.WriteLine($"     {c.Timestamp:yyyy-MM-dd HH:mm} {c.Hash} ({c.Author}) {c.Message}");
    }

    private void CmdArch(string? component)
    {
        if (string.IsNullOrWhiteSpace(component))
        {
            Console.WriteLine("\n  🏗️  System Architecture:");
            Console.WriteLine("  ┌─────────────────┐     ┌──────────────────┐");
            Console.WriteLine("  │  API Gateway     │────▶│ Background Worker│");
            Console.WriteLine("  │  (HTTP)          │     │ (Redis queue)    │");
            Console.WriteLine("  └────────┬────────┘     └──────────────────┘");
            Console.WriteLine("           │");
            Console.WriteLine("           ▼");
            Console.WriteLine("  ┌─────────────────┐     ┌──────────────────┐");
            Console.WriteLine("  │ Reporting Backend│     │ IoT Ingestion    │");
            Console.WriteLine("  │ (PG + Mongo)     │     │ (MQTT)           │");
            Console.WriteLine("  └─────────────────┘     └──────────────────┘");
            return;
        }

        var svc = SystemTopology.Services.FirstOrDefault(
            s => s.Name.Contains(component, StringComparison.OrdinalIgnoreCase));

        if (svc is null)
        {
            Console.WriteLine($"  Component '{component}' not found.");
            return;
        }

        Console.WriteLine($"\n  🏗️  {svc.Name}");
        Console.WriteLine($"     Role: {svc.Role}");
        Console.WriteLine($"     Infra: {(svc.InfraDependencies.Count > 0 ? string.Join(", ", svc.InfraDependencies) : "none")}");
        Console.WriteLine($"     Upstream: {(svc.UpstreamServices.Count > 0 ? string.Join(", ", svc.UpstreamServices) : "none")}");
        Console.WriteLine($"     Base error rate: {svc.BaseErrorRate:P0}");
    }

    private async Task CmdRecallAsync(string? situation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(situation))
        {
            Console.WriteLine("  Usage: recall <situation description>");
            return;
        }

        var matches = await _memory.RecallAsync(situation,
            new RecallOptions { TopK = 5, MinConfidence = 0.1f }, ct);

        if (matches.Count == 0)
        {
            Console.WriteLine("  No matching entries found.");
            return;
        }

        Console.WriteLine($"\n  🧠 Recalled {matches.Count} entries:");
        foreach (var match in matches)
            PrintMatch(match);
    }

    private async Task CmdConfirmAsync(string? entryId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entryId))
        {
            Console.WriteLine("  Usage: confirm <entry-id>");
            return;
        }

        await _memory.ReinforceAsync(entryId, new Reinforcement
        {
            NewEvidence = [$"Human confirmed at {DateTimeOffset.UtcNow:HH:mm:ss}"],
            Source = "human-confirmed",
            Reward = 1.0f
        }, ct);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  ✅ Entry '{entryId}' reinforced.");
        Console.ResetColor();
    }

    private async Task CmdRejectAsync(string? entryId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entryId))
        {
            Console.WriteLine("  Usage: reject <entry-id>");
            return;
        }

        await _memory.ContradictAsync(entryId, $"Human rejected at {DateTimeOffset.UtcNow:HH:mm:ss}", ct);

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  ❌ Entry '{entryId}' contradicted.");
        Console.ResetColor();
    }

    private async Task CmdLearnAsync(CancellationToken ct)
    {
        Console.WriteLine("  🔄 Running offline learning cycle...");
        var result = await _learner.LearnAsync(ct);

        Console.WriteLine($"  Learning complete: {result.Explored} explored, {result.Reinforced} reinforced, "
            + $"{result.Contradicted} contradicted, {result.Decayed} decayed");

        if (result.Discoveries.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("  💡 Discoveries:");
            foreach (var d in result.Discoveries)
                Console.WriteLine($"     • {d}");
            Console.ResetColor();
        }

        // Queue any discoveries for the next prompt
        _pendingDiscoveries.AddRange(result.Discoveries);
    }

    private async Task CmdStatusAsync(CancellationToken ct)
    {
        var patterns = await _memory.BrowseAsync(0, 1000, EmpiricalKind.Pattern, ct: ct);
        var heuristics = await _memory.BrowseAsync(0, 1000, EmpiricalKind.Heuristic, ct: ct);
        var skills = await _memory.BrowseAsync(0, 1000, EmpiricalKind.Skill, ct: ct);

        Console.WriteLine("\n  📊 Empirical Memory Status:");
        Console.WriteLine($"     Patterns:   {patterns.Count}");
        Console.WriteLine($"     Heuristics: {heuristics.Count}");
        Console.WriteLine($"     Skills:     {skills.Count}");
        Console.WriteLine($"     Total:      {patterns.Count + heuristics.Count + skills.Count}");
        Console.WriteLine($"     Log events: {_simulator.History.Count}");
        Console.WriteLine($"     Sim time:   {_simulator.CurrentTime:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"     Window:     {_windowStart:HH:mm:ss} – {_windowEnd:HH:mm:ss}");

        // Show top-5 highest-confidence patterns
        var topPatterns = patterns.OrderByDescending(e => e.Confidence).Take(5).ToList();
        if (topPatterns.Count > 0)
        {
            Console.WriteLine("\n     Top patterns:");
            foreach (var p in topPatterns)
                Console.WriteLine($"       [{p.Id}] conf={p.Confidence:F2} obs={p.ObservationCount} {Truncate(p.Description.ToString(), 60)}");
        }
    }

    // ── Display helpers ─────────────────────────────────────────────

    private static void PrintLogEvent(LogEvent evt)
    {
        var color = evt.Level switch
        {
            LogLevel.Error => ConsoleColor.Red,
            LogLevel.Critical => ConsoleColor.DarkRed,
            LogLevel.Warning => ConsoleColor.Yellow,
            _ => ConsoleColor.Gray
        };

        Console.ForegroundColor = color;
        Console.WriteLine($"  {evt}");
        Console.ResetColor();
    }

    private static void PrintMatch(EmpiricalMatch match)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"     [{match.Entry.Id}]");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($" score={match.Score:F3} conf={match.Entry.Confidence:F2} kind={match.Entry.Kind}");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine($"       {Truncate(match.Entry.Description.ToString(), 80)}");
        if (match.Entry.Condition is not null)
            Console.WriteLine($"       IF: {Truncate(match.Entry.Condition, 70)}");
        if (match.Entry.Effect is not null)
            Console.WriteLine($"       THEN: {Truncate(match.Entry.Effect, 70)}");
    }

    private static void PrintEntryDetail(EmpiricalEntry entry)
    {
        Console.WriteLine($"\n  🔍 Entry: {entry.Id}");
        Console.WriteLine($"     Kind:       {entry.Kind}");
        Console.WriteLine($"     Source:     {entry.Source}");
        Console.WriteLine($"     Confidence: {entry.Confidence:F3}");
        Console.WriteLine($"     Strength:   {entry.Strength:F3}");
        Console.WriteLine($"     Observed:   {entry.ObservationCount}x");
        Console.WriteLine($"     First seen: {entry.FirstObserved:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"     Last seen:  {entry.LastObserved:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"     Desc:       {entry.Description}");
        if (entry.Condition is not null)
            Console.WriteLine($"     Condition:  {entry.Condition}");
        if (entry.Effect is not null)
            Console.WriteLine($"     Effect:     {entry.Effect}");
        if (entry.Mechanism is not null)
            Console.WriteLine($"     Mechanism:  {entry.Mechanism}");
        if (entry.Latency is not null)
            Console.WriteLine($"     Latency:    {entry.Latency.Value.TotalSeconds:F1}s");
        if (entry.Tags.Count > 0)
            Console.WriteLine($"     Tags:       {string.Join(", ", entry.Tags)}");
        if (entry.Evidence.Count > 0)
        {
            Console.WriteLine("     Evidence:");
            foreach (var e in entry.Evidence.Take(5))
                Console.WriteLine($"       • {e}");
        }
        if (entry.Description.SemanticTags.Count > 0)
        {
            Console.WriteLine("     Semantic tags:");
            foreach (var (key, weight) in entry.Description.SemanticTags.OrderByDescending(t => t.Value).Take(8))
                Console.WriteLine($"       {key} = {weight:F1}");
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine();
        Console.WriteLine("  ╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("  ║              Log Events Explorer — Commands                      ║");
        Console.WriteLine("  ╠══════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("  ║  tail [service] [n]     Show last N log events                   ║");
        Console.WriteLine("  ║  grep <pattern>         Search logs by text                      ║");
        Console.WriteLine("  ║  timerange <start> <end>Set investigation window (HH:mm:ss)      ║");
        Console.WriteLine("  ║  correlate              Find patterns in current time window      ║");
        Console.WriteLine("  ║  investigate <id>       Deep-dive into an empirical entry         ║");
        Console.WriteLine("  ║  commits <service>      Show recent deploys for a service         ║");
        Console.WriteLine("  ║  arch [component]       Show system architecture                  ║");
        Console.WriteLine("  ║  recall <situation>     Free-text recall from memory              ║");
        Console.WriteLine("  ║  confirm <id>           Reinforce a pattern (human confirmed)     ║");
        Console.WriteLine("  ║  reject <id>            Contradict a pattern (incorrect)          ║");
        Console.WriteLine("  ║  learn                  Trigger offline learning cycle             ║");
        Console.WriteLine("  ║  status                 Show empirical memory stats                ║");
        Console.WriteLine("  ║  help                   Show this help                            ║");
        Console.WriteLine("  ║  quit                   Exit                                      ║");
        Console.WriteLine("  ╚══════════════════════════════════════════════════════════════════╝");
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : string.Concat(text.AsSpan(0, maxLen - 3), "...");
}
