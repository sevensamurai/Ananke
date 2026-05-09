using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Learning.Exploration;

namespace Connect4Demo;

/// <summary>
/// The Connect 4 agent. Starts with only the rules (legal moves, win detection).
/// Strategy comes entirely from <see cref="IEmpiricalMemory"/> — recalled board
/// positions with similar structural features and semantic tag overlap influence
/// column scoring.
/// </summary>
internal sealed class Connect4Agent(
    InMemoryEmpiricalMemory memory,
    IExplorationStrategy? explorationStrategy = null)
{
    private static readonly Random Rng = new();
    private readonly int[] _columnCounts = new int[Board.Cols];
    private int _totalMoves;

    /// <summary>
    /// Chooses a column to play. Returns the column index and a list of
    /// reasoning steps (for console display).
    /// </summary>
    internal async Task<(int Col, List<string> Reasoning)> ChooseMoveAsync(Board board)
    {
        var legal = board.LegalMoves();
        var reasoning = new List<string>();

        // ── Rule 1: Always take a win ────────────────────────────
        foreach (var col in legal)
        {
            if (board.WouldWin(col, 2))
            {
                reasoning.Add($"Column {col + 1} wins immediately!");
                return (col, reasoning);
            }
        }

        // ── Rule 2: Always block opponent's win ──────────────────
        foreach (var col in legal)
        {
            if (board.WouldWin(col, 1))
            {
                reasoning.Add($"Column {col + 1} blocks opponent's win");
                return (col, reasoning);
            }
        }

        // ── Consult empirical memory using board features ────────
        var situation = BoardFeatures.Describe(board);
        var currentTags = BoardFeatures.Decompose(board);
        var currentDescription = new SemanticDescription { Summary = situation, SemanticTags = currentTags };

        var recalled = await memory.RecallAsync(situation,
            new RecallOptions { TopK = 8, MinConfidence = 0.1f });

        var scores = new float[Board.Cols];
        var variances = new float[Board.Cols];
        var voteCounts = new int[Board.Cols];
        var hasExperience = false;

        foreach (var match in recalled)
        {
            var entry = match.Entry;

            // Compute tag overlap to boost entries with matching tactical shape
            var tagBoost = 1f + currentDescription.TagOverlap(entry.Description);

            // Extract the column played from semantic tags first, then fall back to Tags
            var recalledCol = ExtractColumn(entry);
            if (recalledCol is null) continue;
            if (recalledCol.Value < 0 || recalledCol.Value >= Board.Cols) continue;
            if (!legal.Contains(recalledCol.Value)) continue;

            // Weight: confidence × strength × match score × tag overlap
            var influence = entry.Confidence * entry.Strength * match.Score * tagBoost;

            // Track variance for exploration uncertainty
            variances[recalledCol.Value] += entry.Variance;
            voteCounts[recalledCol.Value]++;

            // For heuristics, check if situation matches and apply as general guidance
            if (entry.Kind == EmpiricalKind.Heuristic)
            {
                reasoning.Add($"💡 Heuristic (tag match: {tagBoost - 1:F2}): {entry.PreferredApproach ?? entry.Description.ToString()}");
                continue; // heuristics don't vote for specific columns
            }

            reasoning.Add($"📖 Pattern col {recalledCol.Value + 1} (conf: {entry.Confidence:F2}, tags: {tagBoost - 1:F2}, score: {match.Score:F2})");

            // Valence tells us direction: positive = led to wins, negative = led to losses
            if (entry.Valence > 0)
            {
                scores[recalledCol.Value] += influence * entry.Valence;
                hasExperience = true;
            }
            else if (entry.Valence < 0)
            {
                // Negative experience: avoid this column
                scores[recalledCol.Value] += influence * entry.Valence; // negative
                hasExperience = true;
            }
        }

        if (hasExperience)
        {
            // Normalize variances: average per column, default 1.0 for unknown columns
            for (var i = 0; i < Board.Cols; i++)
                variances[i] = voteCounts[i] > 0 ? variances[i] / voteCounts[i] : 1.0f;

            if (explorationStrategy is not null)
            {
                var candidates = legal.Select(c => new ActionCandidate
                {
                    Score = scores[c],
                    Uncertainty = variances[c],
                    SelectionCount = _columnCounts[c]
                }).ToList();

                var selectedIdx = explorationStrategy.SelectAction(candidates, _totalMoves);
                var choice = legal[selectedIdx];
                _columnCounts[choice]++;
                _totalMoves++;
                reasoning.Add($"→ Column {choice + 1} (exploration-guided, score: {scores[choice]:F2})");
                return (choice, reasoning);
            }

            var bestScore = legal.Max(c => scores[c]);
            var bestMoves = legal.Where(c => scores[c] >= bestScore && bestScore > 0).ToList();

            if (bestMoves.Count > 0)
            {
                var choice = bestMoves[Rng.Next(bestMoves.Count)];
                _columnCounts[choice]++;
                _totalMoves++;
                reasoning.Add($"→ Column {choice + 1} (experience-guided, score: {scores[choice]:F2})");
                return (choice, reasoning);
            }
        }

        // ── Fallback: random legal move ──────────────────────────
        var fallback = legal[Rng.Next(legal.Count)];
        _columnCounts[fallback]++;
        _totalMoves++;
        reasoning.Add($"→ Column {fallback + 1} (random — no relevant experience)");
        return (fallback, reasoning);
    }

    /// <summary>
    /// Extracts the column played from an entry's semantic tags (<c>action:col_N</c>)
    /// or falls back to the flat <see cref="EmpiricalEntry.Tags"/> (<c>col_N</c>).
    /// </summary>
    private static int? ExtractColumn(EmpiricalEntry entry)
    {
        // Prefer semantic tags — richer signal
        foreach (var key in entry.Description.SemanticTags.Keys)
        {
            if (key.StartsWith("action:col_") && int.TryParse(key.AsSpan(11), out var col))
                return col;
        }

        // Fall back to flat tags
        var colTag = entry.Tags.FirstOrDefault(t => t.StartsWith("col_"));
        if (colTag is not null && int.TryParse(colTag.AsSpan(4), out var fallbackCol))
            return fallbackCol;

        return null;
    }
}
