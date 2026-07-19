using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Learning.Exploration;
using Ananke.Learning.Offline;

namespace Connect4Demo;

/// <summary>
/// Trains the Connect 4 agent through self-play and simulation-based validation.
/// <list type="number">
///   <item>Phase 1 (Discovery): plays self-play games using a strategic AI vs a
///   moderate opponent, committing board-state observations and heuristics to
///   empirical memory via <see cref="GameAnalyzer"/>.</item>
///   <item>Phase 2 (Validation): browses the accumulated patterns and uses
///   <see cref="ISimulationSource"/> to test each hypothesis through additional
///   self-play episodes, reinforcing confirmed patterns and contradicting weak ones.</item>
/// </list>
/// After training the agent's memory contains experience-weighted patterns
/// that improve its play during interactive games.
/// </summary>
internal sealed class Trainer(
    InMemoryEmpiricalMemory memory,
    GameAnalyzer analyzer,
    IExplorationStrategy? explorationStrategy = null)
{
    /// <summary>Window size for learning-curve buckets.</summary>
    private const int WindowSize = 10;

    internal async Task TrainAsync(int iterations, GameStats stats, CancellationToken ct = default)
    {
        Console.WriteLine($"\n  🏋️ Training mode: {iterations} iterations\n");

        await RunDiscoveryPhaseAsync(iterations, stats, windowLog: null, ct);
        await RunValidationPhaseAsync(iterations, ct);

        var pruned = await PruneWeakEntriesAsync(ct);

        Console.WriteLine(
            $"\n  🎓 Training complete! Memory: {memory.Count} entries (pruned {pruned})");
        Console.WriteLine(
            $"     Win rate during training: {stats.AgentWinRate:P0}\n");
    }

    /// <summary>
    /// Runs training with full instrumentation, prints a comprehensive
    /// analysis report evaluating the learning approach, then returns.
    /// Designed for <c>--analyze</c> mode.
    /// </summary>
    internal async Task TrainAndAnalyzeAsync(int iterations, CancellationToken ct = default)
    {
        var stats = new GameStats();
        var windowLog = new List<WindowResult>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║         Connect 4 — Learning Analysis Report                ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Iterations: {iterations,-47}║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");

        // ── Phase 1: Discovery ───────────────────────────────────────
        await RunDiscoveryPhaseAsync(iterations, stats, windowLog, ct);

        // ── Phase 2: Validation ──────────────────────────────────────
        var validation = await RunValidationPhaseAsync(iterations, ct);

        // ── Post-training pruning ────────────────────────────────────
        var pruned = await PruneWeakEntriesAsync(ct);
        Console.WriteLine($"\n  Pruning: removed {pruned} weak entries (memory: {memory.Count})");

        // ── Phase 3: Post-training evaluation ────────────────────────
        // Use the actual Connect4Agent backed by trained memory, playing
        // against the same moderate opponent used in training.
        var evalGames = Math.Max(iterations / 2, 20);
        Console.WriteLine($"\n  Phase 3: Evaluating trained agent ({evalGames} games)...");

        var trainedAgent = new Connect4Agent(memory, explorationStrategy);
        var trainedWins = 0;
        var trainedLosses = 0;
        var trainedDraws = 0;

        for (var i = 0; i < evalGames && !ct.IsCancellationRequested; i++)
        {
            var (_, winner) = PlayAgentVsModerate(trainedAgent, agentFirst: i % 2 == 0);
            if (winner == 2) trainedWins++;
            else if (winner == 1) trainedLosses++;
            else trainedDraws++;
        }

        var trainedWinRate = (float)trainedWins / evalGames;

        // ── Phase 4: Baseline (untrained) evaluation for comparison ──
        Console.WriteLine($"  Phase 4: Baseline comparison ({evalGames} games, no memory)...");

        var baselineMemory = new InMemoryEmpiricalMemory(
            new Ananke.Orchestration.Knowledge.Embeddings.InMemoryEmbedder(),
            dedupThreshold: 0.85f,
            affectOptions: new AffectOptions());

        var baselineAgent = new Connect4Agent(baselineMemory);
        var baselineWins = 0;
        var baselineLosses = 0;
        var baselineDraws = 0;

        for (var i = 0; i < evalGames && !ct.IsCancellationRequested; i++)
        {
            var (_, winner) = PlayAgentVsModerate(baselineAgent, agentFirst: i % 2 == 0);
            if (winner == 2) baselineWins++;
            else if (winner == 1) baselineLosses++;
            else baselineDraws++;
        }

        var baselineWinRate = (float)baselineWins / evalGames;
        sw.Stop();

        // ── Gather memory snapshot ───────────────────────────────────
        var allEntries = await memory.BrowseAsync(0, 500);
        var entries = allEntries.ToList();

        // ── Render report ────────────────────────────────────────────
        Console.WriteLine();
        PrintSection("1. TRAINING OVERVIEW");
        Console.WriteLine($"  Games played:       {stats.TotalGames}");
        Console.WriteLine($"  Agent record:       {stats.AgentWins}W / {stats.HumanWins}L / {stats.Draws}D");
        Console.WriteLine($"  Training win rate:  {stats.AgentWinRate:P1}");
        Console.WriteLine($"  Memory entries:     {memory.Count} (pruned {pruned} weak entries)");
        Console.WriteLine($"  Elapsed time:       {sw.Elapsed.TotalSeconds:F1}s");

        PrintSection("2. LEARNING CURVE (win rate per window)");
        PrintLearningCurve(windowLog);

        PrintSection("3. MEMORY BREAKDOWN");
        PrintMemoryBreakdown(entries);

        PrintSection("4. TOP PATTERNS (by confidence)");
        await PrintTopPatternsAsync(entries);

        PrintSection("5. VALIDATION RESULTS");
        Console.WriteLine($"  Baseline win rate:  {validation.BaselineWinRate:P0} (calibration reference)");
        Console.WriteLine($"  Candidates tested:  {validation.Total}");
        Console.WriteLine($"  ✅ Reinforced:       {validation.Reinforced} (beat baseline)");
        Console.WriteLine($"  ❌ Contradicted:     {validation.Contradicted} (below baseline)");
        Console.WriteLine($"  ➖ Neutral:          {validation.Neutral} (within baseline range)");

        PrintSection("6. POST-TRAINING EVALUATION");
        Console.WriteLine($"  Eval games:         {evalGames}");
        Console.WriteLine($"  Trained agent:      {trainedWins}W / {trainedLosses}L / {trainedDraws}D  ({trainedWinRate:P1})");
        Console.WriteLine($"  Untrained baseline: {baselineWins}W / {baselineLosses}L / {baselineDraws}D  ({baselineWinRate:P1})");
        var delta = trainedWinRate - baselineWinRate;
        var deltaSign = delta >= 0 ? "+" : "";
        Console.WriteLine($"  Δ win rate:         {deltaSign}{delta:P1}");

        PrintSection("7. VERDICT");
        PrintVerdict(stats, trainedWinRate, baselineWinRate, entries, windowLog, pruned);

        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();
    }

    // ── Shared training phases ───────────────────────────────────────

    private async Task RunDiscoveryPhaseAsync(
        int iterations, GameStats stats,
        List<WindowResult>? windowLog,
        CancellationToken ct)
    {
        var discoveryGames = Math.Max(iterations * 2 / 3, 10);
        Console.WriteLine($"\n  Phase 1: {discoveryGames} self-play games to discover patterns...");

        var windowWins = 0;
        var windowGames = 0;
        var prevMemoryCount = memory.Count;
        var staleWindows = 0;
        const int staleThreshold = 3; // stop after N consecutive windows with 0 new entries

        for (var i = 0; i < discoveryGames && !ct.IsCancellationRequested; i++)
        {
            var (board, winner) = Connect4SimulationSource.PlayGame(agentFirst: i % 2 == 0);

            stats.TotalGames++;
            if (winner == 2) { stats.AgentWins++; windowWins++; }
            else if (winner == 1) stats.HumanWins++;
            else stats.Draws++;

            windowGames++;

            await analyzer.AnalyzeAsync(board, winner, stats.TotalGames);

            // Record window bucket
            if (windowGames >= WindowSize || i == discoveryGames - 1)
            {
                var wr = windowGames > 0 ? (float)windowWins / windowGames : 0;
                var newEntries = memory.Count - prevMemoryCount;
                windowLog?.Add(new WindowResult(i + 1 - windowGames + 1, i + 1, wr, memory.Count, newEntries));

                if ((i + 1) % 10 == 0 || i == discoveryGames - 1)
                {
                    Console.WriteLine(
                        $"    Game {i + 1,4}/{discoveryGames}: " +
                        $"{stats.AgentWins}W {stats.HumanWins}L {stats.Draws}D  " +
                        $"(memory: {memory.Count} entries, +{newEntries} new, window WR: {wr:P0})");
                }

                // Convergence detection: stop when no new patterns are discovered
                if (newEntries == 0) staleWindows++;
                else staleWindows = 0;

                prevMemoryCount = memory.Count;
                windowWins = 0;
                windowGames = 0;

                if (staleWindows >= staleThreshold && i >= WindowSize * staleThreshold)
                {
                    Console.WriteLine(
                        $"    ⏹ Converged at game {i + 1} — " +
                        $"no new patterns in {staleWindows} consecutive windows");
                    break;
                }
            }
        }
    }

    private async Task<ValidationResult> RunValidationPhaseAsync(
        int iterations, CancellationToken ct)
    {
        var allEntries = await memory.BrowseAsync(0, 100);
        var candidates = allEntries
            .Where(e => e.ConsolidatedInto is null && e.ObservationCount >= 2)
            .OrderByDescending(e => e.Confidence)
            .Take(20)
            .ToList();

        var episodesPerHypothesis = Math.Clamp(
            iterations / 3 / Math.Max(candidates.Count, 1), 10, 50);

        // ── Baseline: measure how the Connect4Agent with EMPTY memory
        //    performs against the moderate opponent. This is the "no learning"
        //    reference — the actual agent mechanism, not the strategic AI.
        const int baselineEpisodes = 40;
        var emptyMemory = new InMemoryEmpiricalMemory(
            new Ananke.Orchestration.Knowledge.Embeddings.InMemoryEmbedder(),
            dedupThreshold: 0.85f,
            affectOptions: new AffectOptions { ReinforcementCooldownHours = 0.001f });
        var emptyAgent = new Connect4Agent(emptyMemory);

        var blWins = 0;
        for (var i = 0; i < baselineEpisodes; i++)
        {
            var (_, w) = PlayAgentVsModerate(emptyAgent, agentFirst: i % 2 == 0);
            if (w == 2) blWins++;
        }
        var baselineWinRate = (float)blWins / baselineEpisodes;

        Console.WriteLine(
            $"\n  Phase 2: Validating {candidates.Count} patterns " +
            $"({episodesPerHypothesis} episodes each, agent baseline WR: {baselineWinRate:P0})...");

        var reinforced = 0;
        var contradicted = 0;
        var neutral = 0;

        foreach (var pattern in candidates)
        {
            if (ct.IsCancellationRequested) break;

            // Build a focused memory with just this pattern and its neighbors,
            // then play games with a Connect4Agent backed by that memory.
            // This tests whether the pattern is useful to the recall mechanism.
            var focusedMemory = new InMemoryEmpiricalMemory(
                new Ananke.Orchestration.Knowledge.Embeddings.InMemoryEmbedder(),
                dedupThreshold: 0.85f,
                affectOptions: new AffectOptions { ReinforcementCooldownHours = 0.001f });

            await focusedMemory.CommitAsync(pattern, ct);

            // Also commit closely related patterns for context
            var related = await memory.RecallAsync(
                pattern.Description.ToEmbeddingText(),
                new RecallOptions { TopK = 5, MinConfidence = 0.1f });
            foreach (var match in related)
            {
                if (match.Entry.Id != pattern.Id)
                    await focusedMemory.CommitAsync(match.Entry, ct);
            }

            var focusedAgent = new Connect4Agent(focusedMemory);
            var wins = 0;
            for (var i = 0; i < episodesPerHypothesis; i++)
            {
                var (_, w) = PlayAgentVsModerate(focusedAgent, agentFirst: i % 2 == 0);
                if (w == 2) wins++;
            }

            var patternWinRate = (float)wins / episodesPerHypothesis;
            var delta = patternWinRate - baselineWinRate;

            if (delta > 0.05f)
            {
                await memory.ReinforceAsync(pattern.Id, new Reinforcement
                {
                    NewEvidence = [$"validation: {patternWinRate:P0} WR vs {baselineWinRate:P0} baseline (+{delta:P0})"],
                    Source = "validation",
                    Reward = Math.Clamp(0.5f + delta, 0f, 1f)
                });
                reinforced++;
            }
            else if (delta < -0.1f)
            {
                await memory.ContradictAsync(
                    pattern.Id,
                    $"validation: {patternWinRate:P0} WR vs {baselineWinRate:P0} baseline ({delta:P0})");
                contradicted++;
            }
            else
            {
                neutral++;
            }
        }

        Console.WriteLine(
            $"    ✅ Reinforced: {reinforced}  " +
            $"❌ Contradicted: {contradicted}  " +
            $"➖ Neutral: {neutral}");

        return new ValidationResult(candidates.Count, reinforced, contradicted, neutral, baselineWinRate);
    }

    // ── Agent-vs-moderate game (uses Connect4Agent with memory recall) ─

    private static (Board Board, int Winner) PlayAgentVsModerate(
        Connect4Agent agent, bool agentFirst)
    {
        var board = new Board();
        var current = agentFirst ? 2 : 1;
        var winner = 0;

        while (winner == 0 && !board.IsFull())
        {
            int col;
            if (current == 2)
            {
                var (c, _) = agent.ChooseMoveAsync(board).GetAwaiter().GetResult();
                col = c;
            }
            else
            {
                col = PickModerateMove(board, 1);
            }

            var row = board.Drop(col, current);
            if (board.CheckWin(col, row))
                winner = current;

            current = current == 1 ? 2 : 1;
        }

        return (board, winner);
    }

    private static int PickModerateMove(Board board, int player)
    {
        var legal = board.LegalMoves();
        var opponent = player == 1 ? 2 : 1;

        foreach (var c in legal)
            if (board.WouldWin(c, player)) return c;
        foreach (var c in legal)
            if (board.WouldWin(c, opponent)) return c;

        var weighted = new List<int>();
        foreach (var c in legal)
        {
            var weight = 1 + (3 - Math.Abs(c - 3));
            for (var i = 0; i < weight; i++)
                weighted.Add(c);
        }
        return weighted[Random.Shared.Next(weighted.Count)];
    }

    // ── Report rendering ─────────────────────────────────────────────

    private static void PrintSection(string title) =>
        TrainingReport.PrintSection(title);

    private static void PrintLearningCurve(List<WindowResult>? windows) =>
        TrainingReport.PrintLearningCurve(windows);

    private static void PrintMemoryBreakdown(List<EmpiricalEntry> entries) =>
        TrainingReport.PrintMemoryBreakdown(entries);

    private Task PrintTopPatternsAsync(List<EmpiricalEntry> entries) =>
        TrainingReport.PrintTopPatternsAsync(entries);

    private static void PrintVerdict(
        GameStats stats,
        float trainedWinRate, float baselineWinRate,
        List<EmpiricalEntry> entries,
        List<WindowResult>? windows,
        int pruned) =>
        TrainingReport.PrintVerdict(stats, trainedWinRate, baselineWinRate, entries, windows, pruned);

    // ── Post-training pruning ─────────────────────────────────────────

    /// <summary>
    /// Removes low-value entries from recall by marking them as consolidated.
    /// Prevents weak/noisy patterns from polluting the agent's decisions.
    /// </summary>
    private async Task<int> PruneWeakEntriesAsync(CancellationToken ct = default)
    {
        var all = await memory.BrowseAsync(0, 500);
        var pruned = 0;

        foreach (var entry in all)
        {
            if (ct.IsCancellationRequested) break;
            if (entry.ConsolidatedInto is not null) continue;

            var value = entry.Confidence * entry.Strength;

            // Prune entries with very low combined signal and few observations.
            // These are noise — seen once or twice, never confirmed by validation.
            if (value < 0.1f && entry.ObservationCount <= 2)
            {
                await memory.MarkConsolidatedAsync(entry.Id, "pruned:weak-signal");
                pruned++;
            }
        }

        return pruned;
    }

}
