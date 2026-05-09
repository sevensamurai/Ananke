using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Learning.Episodes;

namespace Connect4Demo;

/// <summary>
/// Analyzes completed games and commits board-state observations to empirical memory.
/// Each move's board snapshot is encoded as structural feature tokens via
/// <see cref="BoardFeatures"/>. No hardcoded strategic knowledge — patterns
/// emerge through semantic similarity of feature descriptions across games.
/// </summary>
internal sealed class GameAnalyzer(
    InMemoryEmpiricalMemory memory,
    IEpisodeStore? episodeStore = null,
    IRewardPropagator? rewardPropagator = null)
{
    /// <summary>
    /// Analyzes a completed game: commits key board snapshots with outcome rewards,
    /// and reinforces previously recalled entries that appeared in similar positions.
    /// </summary>
    internal async Task<List<string>> AnalyzeAsync(
        Board board, int winner, int gameNumber)
    {
        var insights = new List<string>();

        // Reward signal: +1 agent won, -1 agent lost, 0 draw
        var reward = winner switch
        {
            2 => 1.0f,   // agent won
            1 => -1.0f,  // agent lost
            _ => 0f      // draw
        };

        // ── Commit key board snapshots as observations ───────────────
        var (snapshotInsights, steps) = await CommitSnapshotsAsync(board, reward, gameNumber);
        insights.AddRange(snapshotInsights);

        // ── Commit episode and propagate rewards ─────────────────────
        if (episodeStore is not null && steps.Count > 0)
        {
            var episode = await episodeStore.CommitAsync(new Episode
            {
                Id = $"game_{gameNumber}",
                Steps = steps,
                TerminalReward = reward,
                StartedAt = DateTimeOffset.UtcNow.AddSeconds(-board.MoveCount),
                CompletedAt = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, string>
                {
                    ["opponent"] = "training",
                    ["moves"] = board.MoveCount.ToString()
                }
            });

            if (rewardPropagator is not null)
            {
                var propagated = await rewardPropagator.PropagateAsync(episode, memory);
                insights.Add($"\ud83d\udd04 Propagated reward through {propagated} steps");
            }
        }

        // ── Reinforce existing memories that match the final position ─
        insights.AddRange(await ReinforceMatchesAsync(board, reward, gameNumber));

        return insights;
    }

    /// <summary>
    /// Walks through the game and commits board snapshots at each agent move.
    /// The description captures the pre-move board features plus the column
    /// played, so different actions at the same position are distinct entries.
    /// Semantic tags decompose each snapshot into weighted dimensions for
    /// causal-aware dedup and structured recall.
    /// </summary>
    private async Task<(List<string> Insights, List<EpisodeStep> Steps)> CommitSnapshotsAsync(
        Board board, float reward, int gameNumber)
    {
        var insights = new List<string>();
        var steps = new List<EpisodeStep>();
        var replay = new Board();
        var stepIndex = 0;

        foreach (var (col, player) in board.MoveHistory)
        {
            // Describe the board BEFORE the move — this is the decision context
            if (player == 2)
            {
                var features = BoardFeatures.Describe(replay);
                var summary = $"{features} played_c{col}";
                var tags = BoardFeatures.Decompose(replay, action: col);

                var entry = await memory.CommitAsync(new EmpiricalEntry
                {
                    Id = $"obs_g{gameNumber}_m{replay.MoveCount}",
                    Kind = EmpiricalKind.Pattern,
                    Tags = [$"game_{gameNumber}", $"col_{col}", $"move_{replay.MoveCount}"],
                    Source = "game_observation",
                    Description = new SemanticDescription { Summary = summary, SemanticTags = tags },
                    Confidence = 0.3f,
                    ObservationCount = 1,
                    Evidence = [$"game {gameNumber} move {replay.MoveCount} agent played col {col + 1} reward {reward:+0.0;-0.0;0}"],
                    FirstObserved = DateTimeOffset.UtcNow,
                    LastObserved = DateTimeOffset.UtcNow,
                    EpisodeId = $"game_{gameNumber}",
                    StepIndex = stepIndex
                });

                steps.Add(new EpisodeStep
                {
                    StepIndex = stepIndex,
                    EntryId = entry.Id
                });
                stepIndex++;

                if (entry.ObservationCount > 1)
                    insights.Add($"📎 Merged into known position (observations: {entry.ObservationCount}, confidence: {entry.Confidence:F2})");
            }

            replay.Drop(col, player);
        }

        // Commit the final board state with outcome
        var finalFeatures = BoardFeatures.Describe(board);
        var finalSummary = BoardFeatures.Summarize(board);
        var outcomeLabel = reward > 0 ? "win" : reward < 0 ? "loss" : "draw";
        var outcomeTags = BoardFeatures.Decompose(board, outcome: outcomeLabel);

        await memory.CommitAsync(new EmpiricalEntry
        {
            Id = $"outcome_g{gameNumber}",
            Kind = EmpiricalKind.Pattern,
            Tags = [$"game_{gameNumber}", "outcome", outcomeLabel],
            Source = "game_outcome",
            Description = new SemanticDescription { Summary = finalFeatures, SemanticTags = outcomeTags },
            Confidence = Math.Abs(reward) > 0 ? 0.5f : 0.2f,
            ObservationCount = 1,
            Evidence = [$"game {gameNumber} {(reward > 0 ? "agent won" : reward < 0 ? "agent lost" : "draw")} {finalSummary}"],
            FirstObserved = DateTimeOffset.UtcNow,
            LastObserved = DateTimeOffset.UtcNow
        });
        insights.Add($"📊 Final position committed ({finalSummary})");

        // Synthesize a heuristic from this game's observed tags
        insights.AddRange(await SynthesizeHeuristicAsync(board, reward, gameNumber));

        return (insights, steps);
    }

    /// <summary>
    /// Generates a heuristic entry from the game outcome by analyzing which
    /// structural patterns (semantic tags) correlated with the result.
    /// No LLM needed — the tags are programmatically extracted from the board.
    /// </summary>
    private async Task<List<string>> SynthesizeHeuristicAsync(
        Board board, float reward, int gameNumber)
    {
        var insights = new List<string>();
        if (MathF.Abs(reward) < 0.5f) return insights; // skip draws — weak signal

        var tags = BoardFeatures.Decompose(board);

        // Identify the dominant tactical feature to form a heuristic around
        string? situation = null;
        string? preferred = null;

        if (reward > 0)
        {
            // Agent won — correlate with observable board features
            if (tags.TryGetValue("center:agent", out var cw) && cw > 0.3f)
            {
                situation = "Agent had multiple pieces in center column";
                preferred = "Play center column when available";
            }
            else if (tags.ContainsKey("line:a3"))
            {
                situation = "Agent had 3-in-a-row lines at game end";
                preferred = "Build lines with 3 pieces and an open cell";
            }
        }
        else
        {
            // Agent lost — correlate with observable board features
            if (tags.ContainsKey("line:e3"))
            {
                situation = "Opponent had 3-in-a-row lines at game end";
                preferred = "Reduce opponent's 3-piece lines before extending own";
            }
            else if (tags.TryGetValue("center:opponent", out var cow) && cow > 0.3f)
            {
                situation = "Opponent had multiple pieces in center column";
                preferred = "Contest center column in opening moves";
            }
        }

        if (situation is null || preferred is null) return insights;

        // Build heuristic tags: inherit the board's tactical tags + add the outcome
        var heuristicTags = new Dictionary<string, float>();
        foreach (var (key, weight) in tags)
            heuristicTags[key] = weight;
        heuristicTags[$"outcome:{(reward > 0 ? "win" : "loss")}"] = 1.0f;

        var entry = await memory.CommitAsync(new EmpiricalEntry
        {
            Id = $"heuristic_g{gameNumber}",
            Kind = EmpiricalKind.Heuristic,
            Tags = [$"game_{gameNumber}", reward > 0 ? "win" : "loss"],
            Source = "game_analysis",
            Description = new SemanticDescription
            {
                Summary = $"When {situation.ToLowerInvariant()}: {preferred.ToLowerInvariant()}",
                SemanticTags = heuristicTags
            },
            Situation = situation,
            PreferredApproach = preferred,
            Confidence = 0.3f,
            ObservationCount = 1,
            Evidence = [$"game {gameNumber}: {(reward > 0 ? "agent won" : "agent lost")}"],
            FirstObserved = DateTimeOffset.UtcNow,
            LastObserved = DateTimeOffset.UtcNow
        });

        var verb = entry.ObservationCount > 1 ? "Reinforced" : "Discovered";
        insights.Add($"💡 {verb} heuristic: {preferred}");

        return insights;
    }

    /// <summary>
    /// Recalls entries similar to the final board state and reinforces them
    /// with the actual game outcome as the reward signal.
    /// </summary>
    private async Task<List<string>> ReinforceMatchesAsync(
        Board board, float reward, int gameNumber)
    {
        var insights = new List<string>();
        var finalFeatures = BoardFeatures.Describe(board);

        var matches = await memory.RecallAsync(finalFeatures,
            new RecallOptions { TopK = 5, MinConfidence = 0.1f });

        foreach (var match in matches)
        {
            // Skip the entry we just committed this game
            if (match.Entry.Tags.Contains($"game_{gameNumber}")) continue;

            await memory.ReinforceAsync(match.Entry.Id, new Reinforcement
            {
                NewEvidence = [$"game-{gameNumber}: similar position, outcome reward {reward:+0.0;-0.0;0}"],
                Source = "game-analysis",
                Reward = reward
            });

            var delta = reward > 0 ? "📈" : reward < 0 ? "📉" : "➡️";
            insights.Add($"{delta} Reinforced similar position (confidence: {match.Entry.Confidence:F2}, score: {match.Score:F2})");
        }

        return insights;
    }
}

/// <summary>Tracks aggregate statistics across games.</summary>
internal sealed class GameStats
{
    internal int TotalGames { get; set; }
    internal int HumanWins { get; set; }
    internal int AgentWins { get; set; }
    internal int Draws { get; set; }
    internal int TotalAgentMoves { get; set; }
    internal int AgentCenterMoves { get; set; }

    internal float AgentWinRate => TotalGames > 0 ? (float)AgentWins / TotalGames : 0;
    internal float AgentCenterRate => TotalAgentMoves > 0 ? (float)AgentCenterMoves / TotalAgentMoves : 0;
}
