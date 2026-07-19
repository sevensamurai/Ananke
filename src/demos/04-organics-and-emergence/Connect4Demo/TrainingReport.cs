using System.Text;
using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;

namespace Connect4Demo;

/// <summary>
/// Renders the post-training analysis report sections for
/// <c>--analyze</c> mode. All methods are pure console output.
/// </summary>
internal static class TrainingReport
{
    internal static void PrintSection(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"  ┌─ {title} ────────────────────────────────────────");
        Console.WriteLine("  │");
    }

    internal static void PrintLearningCurve(List<WindowResult>? windows)
    {
        if (windows is null or { Count: 0 })
        {
            Console.WriteLine("  (no data)");
            return;
        }

        const int barWidth = 30;

        Console.WriteLine($"  {"Window",-16} {"WR",6}  {"Mem",5} {"New",4}  Bar");
        Console.WriteLine($"  {"──────",-16} {"──",6}  {"───",5} {"───",4}  {new string('─', barWidth)}");

        foreach (var w in windows)
        {
            var filled = (int)(w.WinRate * barWidth);
            var bar = new string('█', filled) + new string('░', barWidth - filled);
            Console.WriteLine(
                $"  Game {w.FromGame,3}-{w.ToGame,-3}     {w.WinRate,5:P0}  {w.MemoryCount,5} {w.NewEntries,3}   {bar}");
        }

        // Trend summary
        if (windows.Count >= 3)
        {
            var firstThird = windows.Take(windows.Count / 3).Average(w => w.WinRate);
            var lastThird = windows.Skip(windows.Count * 2 / 3).Average(w => w.WinRate);
            var trend = lastThird - firstThird;
            var arrow = trend > 0.05f ? "📈" : trend < -0.05f ? "📉" : "➡️";
            Console.WriteLine();
            Console.WriteLine($"  {arrow} Trend: first third {firstThird:P0} → last third {lastThird:P0} (Δ {trend:+0.0%;-0.0%;0%})");
        }

        // Convergence info
        var totalNew = windows.Sum(w => w.NewEntries);
        var lastNew = windows[^1].NewEntries;
        var staleCount = 0;
        for (var i = windows.Count - 1; i >= 0 && windows[i].NewEntries == 0; i--)
            staleCount++;

        Console.WriteLine($"  📊 Total new entries: {totalNew}, " +
            $"trailing stale windows: {staleCount}" +
            (staleCount >= 3 ? " (converged)" : ""));
    }

    internal static void PrintMemoryBreakdown(List<EmpiricalEntry> entries)
    {
        if (entries.Count == 0)
        {
            Console.WriteLine("  (empty)");
            return;
        }

        // By kind
        var byKind = entries.GroupBy(e => e.Kind)
            .OrderByDescending(g => g.Count());
        Console.WriteLine("  By kind:");
        foreach (var g in byKind)
            Console.WriteLine($"    {g.Key,-12} {g.Count(),4} entries");

        // Confidence distribution
        Console.WriteLine();
        Console.WriteLine("  Confidence distribution:");
        var buckets = new (string Label, float Min, float Max)[]
        {
            ("  0.0 – 0.2", 0f, 0.2f),
            ("  0.2 – 0.4", 0.2f, 0.4f),
            ("  0.4 – 0.6", 0.4f, 0.6f),
            ("  0.6 – 0.8", 0.6f, 0.8f),
            ("  0.8 – 1.0", 0.8f, 1.01f)
        };
        foreach (var (label, min, max) in buckets)
        {
            var count = entries.Count(e => e.Confidence >= min && e.Confidence < max);
            var pct = (float)count / entries.Count;
            var bar = new string('█', (int)(pct * 20));
            Console.WriteLine($"    {label}  {count,4}  {bar}");
        }

        // Observation count stats
        Console.WriteLine();
        var obsCounts = entries.Select(e => e.ObservationCount).OrderBy(x => x).ToList();
        Console.WriteLine($"  Observation counts:  min={obsCounts[0]}  " +
            $"median={obsCounts[obsCounts.Count / 2]}  max={obsCounts[^1]}  " +
            $"mean={obsCounts.Average():F1}");

        // Dedup rate: entries with obs > 1
        var mergedCount = entries.Count(e => e.ObservationCount > 1);
        Console.WriteLine($"  Dedup (merged):      {mergedCount} of {entries.Count} ({(float)mergedCount / entries.Count:P0})");

        // Strength stats
        Console.WriteLine();
        var strengths = entries.Select(e => e.Strength).OrderBy(x => x).ToList();
        Console.WriteLine($"  Strength:            min={strengths[0]:F2}  " +
            $"median={strengths[strengths.Count / 2]:F2}  max={strengths[^1]:F2}");

        // Valence distribution
        var positive = entries.Count(e => e.Valence > 0.1f);
        var negative = entries.Count(e => e.Valence < -0.1f);
        var neutralV = entries.Count - positive - negative;
        Console.WriteLine($"  Valence:             +{positive}  −{negative}  ≈{neutralV}");
    }

    internal static async Task PrintTopPatternsAsync(List<EmpiricalEntry> entries)
    {
        var top = entries
            .Where(e => e.ConsolidatedInto is null)
            .OrderByDescending(e => e.Confidence * e.Strength)
            .Take(10)
            .ToList();

        if (top.Count == 0)
        {
            Console.WriteLine("  (none)");
            return;
        }

        var rank = 0;
        foreach (var e in top)
        {
            rank++;
            var icon = e.Kind switch
            {
                EmpiricalKind.Pattern => "🔍",
                EmpiricalKind.Heuristic => "💡",
                EmpiricalKind.Skill => "🎯",
                _ => "  "
            };
            var desc = e.Description.Summary?.Length > 55
                ? e.Description.Summary[..52] + "..."
                : e.Description.Summary ?? "(no summary)";
            Console.WriteLine($"  {rank,2}. {icon} [{e.Kind,-9}] {desc}");
            Console.WriteLine($"      conf={e.Confidence:F2}  str={e.Strength:F2}  " +
                $"obs={e.ObservationCount}  val={e.Valence:+0.00;-0.00;0.00}  " +
                $"var={e.Variance:F2}");

            // Show top semantic tags
            var topTags = e.Description.SemanticTags
                .OrderByDescending(t => t.Value)
                .Take(5)
                .Select(t => $"{t.Key}({t.Value:F1})");
            var tagStr = string.Join(" ", topTags);
            if (tagStr.Length > 0)
                Console.WriteLine($"      tags: {tagStr}");
        }

        // Also show top heuristics specifically
        var heuristics = entries
            .Where(e => e.Kind == EmpiricalKind.Heuristic && e.ConsolidatedInto is null)
            .OrderByDescending(e => e.Confidence)
            .Take(5)
            .ToList();

        if (heuristics.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Top heuristics (actionable discoveries):");
            foreach (var h in heuristics)
            {
                var arrow = h.Valence > 0 ? "📈" : h.Valence < 0 ? "📉" : "➡️";
                Console.WriteLine($"    {arrow} \"{h.PreferredApproach ?? h.Description.Summary}\"");
                Console.WriteLine($"       conf={h.Confidence:F2}  obs={h.ObservationCount}");
            }
        }

        await Task.CompletedTask;
    }

    internal static void PrintVerdict(
        GameStats stats,
        float trainedWinRate, float baselineWinRate,
        List<EmpiricalEntry> entries,
        List<WindowResult>? windows,
        int pruned)
    {
        var sb = new StringBuilder();
        var score = 0;

        // Criterion 1: Trained > baseline
        var delta = trainedWinRate - baselineWinRate;
        if (delta > 0.1f) { sb.AppendLine("  ✅ Trained agent significantly outperforms baseline"); score += 2; }
        else if (delta > 0.02f) { sb.AppendLine("  ✅ Trained agent outperforms baseline"); score += 1; }
        else if (delta > -0.02f) { sb.AppendLine("  ⚠️  Trained agent performs similarly to baseline"); }
        else { sb.AppendLine("  ❌ Trained agent underperforms baseline"); score -= 1; }

        // Criterion 2: Learning curve trend
        if (windows is { Count: >= 3 })
        {
            var firstThird = windows.Take(windows.Count / 3).Average(w => w.WinRate);
            var lastThird = windows.Skip(windows.Count * 2 / 3).Average(w => w.WinRate);
            if (lastThird > firstThird + 0.05f) { sb.AppendLine("  ✅ Win rate improved over training"); score += 1; }
            else if (lastThird > firstThird - 0.05f) { sb.AppendLine("  ⚠️  Win rate remained stable during training"); }
            else { sb.AppendLine("  ❌ Win rate declined over training"); score -= 1; }
        }

        // Criterion 3: Pattern diversity
        var kinds = entries.Select(e => e.Kind).Distinct().Count();
        if (kinds >= 2) { sb.AppendLine($"  ✅ Discovered {kinds} knowledge kinds"); score += 1; }
        else { sb.AppendLine($"  ⚠️  Only {kinds} knowledge kind discovered"); }

        // Criterion 4: Dedup working (merges happening)
        var merged = entries.Count(e => e.ObservationCount > 1);
        if (merged > entries.Count * 0.1f) { sb.AppendLine($"  ✅ Semantic dedup active ({merged} merged entries)"); score += 1; }
        else { sb.AppendLine("  ⚠️  Low dedup rate — patterns may be too unique"); }

        // Criterion 5: Confidence spread (not all stuck at initial)
        var highConf = entries.Count(e => e.Confidence > 0.5f);
        if (highConf > 0) { sb.AppendLine($"  ✅ {highConf} entries reached confidence > 0.5"); score += 1; }
        else { sb.AppendLine("  ⚠️  No entries reached confidence > 0.5"); }

        // Criterion 6: Convergence — discovery saturated (good) vs still noisy
        if (windows is { Count: >= 2 })
        {
            var staleCount = 0;
            for (var i = windows.Count - 1; i >= 0 && windows[i].NewEntries == 0; i--)
                staleCount++;

            if (staleCount >= 2)
            {
                sb.AppendLine($"  ✅ Discovery converged (last {staleCount} windows stable)");
                score += 1;
            }
            else if (pruned > 0)
            {
                sb.AppendLine($"  ⚠️  Not fully converged — pruned {pruned} weak entries to compensate");
            }
            else
            {
                sb.AppendLine("  ⚠️  Discovery did not converge — may benefit from more iterations or higher dedup");
            }
        }

        // Overall
        Console.Write(sb);
        Console.WriteLine();
        var grade = score switch
        {
            >= 6 => "EXCELLENT — strong evidence of learning",
            >= 4 => "GOOD — clear learning signal",
            >= 2 => "FAIR — some learning, room for improvement",
            >= 0 => "INCONCLUSIVE — no clear signal",
            _ => "POOR — learning approach needs revision"
        };
        Console.WriteLine($"  Score: {score}/7  →  {grade}");
    }
}
