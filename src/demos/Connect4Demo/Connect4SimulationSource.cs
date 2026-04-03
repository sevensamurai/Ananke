using Ananke.Learning;
using Ananke.Learning.Offline;

namespace Connect4Demo;

/// <summary>
/// Self-play simulation source for Connect 4. Tests hypotheses by having
/// a hypothesis-guided strategic AI play against a moderate baseline opponent.
/// The strategic AI knows center control, fork creation, threat avoidance,
/// and line building — patterns the agent should discover through training.
/// </summary>
internal sealed class Connect4SimulationSource : ISimulationSource
{
    private static readonly Random Rng = new();

    /// <inheritdoc />
    public Task<SimulationOutcome> SimulateAsync(
        EmpiricalEntry hypothesis,
        IReadOnlyList<EmpiricalMatch> relatedKnowledge,
        int maxEpisodes,
        CancellationToken ct = default)
    {
        var wins = 0;
        var losses = 0;
        var draws = 0;

        for (var ep = 0; ep < maxEpisodes && !ct.IsCancellationRequested; ep++)
        {
            var board = new Board();
            var agentFirst = ep % 2 == 0;
            var current = agentFirst ? 2 : 1;
            var winner = 0;

            while (winner == 0 && !board.IsFull())
            {
                var col = current == 2
                    ? PickStrategicMove(board, 2, hypothesis, relatedKnowledge)
                    : PickModerateMove(board, 1);

                var row = board.Drop(col, current);
                if (board.CheckWin(col, row))
                    winner = current;

                current = current == 1 ? 2 : 1;
            }

            if (winner == 2) wins++;
            else if (winner == 1) losses++;
            else draws++;
        }

        var winRate = (float)wins / maxEpisodes;
        var reward = Math.Clamp((winRate - 0.5f) * 2f, -1f, 1f);

        return Task.FromResult(new SimulationOutcome
        {
            Reward = reward,
            Summary = $"{wins}W/{losses}L/{draws}D ({winRate:P0}) testing: {Truncate(hypothesis.Description.Summary, 60)}",
            EpisodesRun = maxEpisodes,
            EpisodesSupported = wins
        });
    }

    /// <summary>
    /// Plays a self-play game between the strategic AI (player 2) and
    /// the moderate opponent (player 1). Used by the trainer for the
    /// discovery phase where no hypothesis is being tested yet.
    /// </summary>
    internal static (Board Board, int Winner) PlayGame(bool agentFirst)
    {
        var board = new Board();
        var current = agentFirst ? 2 : 1;
        var winner = 0;

        while (winner == 0 && !board.IsFull())
        {
            var col = current == 2
                ? PickStrategicMove(board, 2)
                : PickModerateMove(board, 1);

            var row = board.Drop(col, current);
            if (board.CheckWin(col, row))
                winner = current;

            current = current == 1 ? 2 : 1;
        }

        return (board, winner);
    }

    /// <summary>
    /// Strategic AI with Connect 4 domain knowledge. Incorporates center control,
    /// fork creation, threat avoidance, and line building. When a hypothesis and
    /// related knowledge are provided, their semantic tags influence column scoring.
    /// </summary>
    private static int PickStrategicMove(
        Board board, int player,
        EmpiricalEntry? hypothesis = null,
        IReadOnlyList<EmpiricalMatch>? related = null)
    {
        var legal = board.LegalMoves();
        var opponent = player == 2 ? 1 : 2;

        // ── Mandatory: take a win ────────────────────────────────
        foreach (var c in legal)
            if (board.WouldWin(c, player)) return c;

        // ── Mandatory: block opponent win ─────────────────────────
        foreach (var c in legal)
            if (board.WouldWin(c, opponent)) return c;

        // ── Score remaining moves ────────────────────────────────
        var scores = new float[Board.Cols];

        foreach (var c in legal)
        {
            // Center proximity: center column scores highest
            scores[c] += (3f - Math.Abs(c - 3)) * 0.3f;

            // Lookahead: avoid moves that let opponent win next turn
            var clone = board.Clone();
            clone.Drop(c, player);

            foreach (var oc in clone.LegalMoves())
            {
                if (clone.WouldWin(oc, opponent))
                    scores[c] -= 3f;
            }

            // Fork creation: count our winning threats after this move
            var threats = clone.LegalMoves().Count(oc => clone.WouldWin(oc, player));
            scores[c] += threats * 2f;

            // Line building: extend connected pieces
            scores[c] += EvaluateLineBuilding(board, c, player);
        }

        // ── Hypothesis influence via semantic tags ────────────────
        if (hypothesis is not null)
            ApplyHypothesisBoost(scores, legal, hypothesis);

        // ── Related knowledge influence ──────────────────────────
        if (related is not null)
        {
            foreach (var match in related)
            {
                foreach (var (key, _) in match.Entry.Description.SemanticTags)
                {
                    if (key.StartsWith("action:col_")
                        && int.TryParse(key.AsSpan(11), out var col)
                        && col >= 0 && col < Board.Cols
                        && legal.Contains(col))
                    {
                        scores[col] += match.Entry.Confidence * match.Score
                                     * Math.Max(match.Entry.Valence, 0.1f) * 0.5f;
                    }
                }
            }
        }

        // ── Pick best with small noise for exploration ───────────
        var bestScore = legal.Max(c => scores[c]);
        var best = legal.Where(c => scores[c] >= bestScore - 0.2f).ToList();
        return best[Rng.Next(best.Count)];
    }

    /// <summary>
    /// Moderate opponent: knows to win and block, slight center preference,
    /// otherwise random. Provides a reasonable but beatable baseline.
    /// </summary>
    private static int PickModerateMove(Board board, int player)
    {
        var legal = board.LegalMoves();
        var opponent = player == 1 ? 2 : 1;

        // Win if possible
        foreach (var c in legal)
            if (board.WouldWin(c, player)) return c;

        // Block opponent win
        foreach (var c in legal)
            if (board.WouldWin(c, opponent)) return c;

        // Weighted random with center preference
        var weighted = new List<int>();
        foreach (var c in legal)
        {
            var weight = 1 + (3 - Math.Abs(c - 3));
            for (var i = 0; i < weight; i++)
                weighted.Add(c);
        }

        return weighted[Rng.Next(weighted.Count)];
    }

    private static void ApplyHypothesisBoost(
        float[] scores, List<int> legal, EmpiricalEntry hypothesis)
    {
        var tags = hypothesis.Description.SemanticTags;

        // Center control hypothesis — strong boost so the hypothesis actually
        // changes play enough for validation to detect a difference.
        if (tags.ContainsKey("center:agent") && legal.Contains(3))
            scores[3] += 2.5f;

        // Specific column action tags
        foreach (var (key, weight) in tags)
        {
            if (key.StartsWith("action:col_")
                && int.TryParse(key.AsSpan(11), out var col)
                && col >= 0 && col < Board.Cols
                && legal.Contains(col))
            {
                scores[col] += weight * hypothesis.Confidence * 3f;
            }
        }

        // Line-building hypotheses: prefer moves that extend lines
        if (tags.ContainsKey("line:a3") || tags.ContainsKey("line:a2"))
        {
            foreach (var c in legal)
            {
                if (c >= 2 && c <= 4)
                    scores[c] += 0.8f * hypothesis.Confidence;
            }
        }
    }

    private static float EvaluateLineBuilding(Board board, int col, int player)
    {
        var row = board.Height(col);
        if (row >= Board.Rows) return 0;

        float score = 0;
        ReadOnlySpan<(int dc, int dr)> dirs = [(1, 0), (0, 1), (1, 1), (1, -1)];

        foreach (var (dc, dr) in dirs)
        {
            var connected = 0;
            var open = true;

            // Positive direction
            for (var i = 1; i <= 3; i++)
            {
                var cc = col + dc * i;
                var cr = row + dr * i;
                if (cc < 0 || cc >= Board.Cols || cr < 0 || cr >= Board.Rows) break;
                var cell = board.At(cc, cr);
                if (cell == player) connected++;
                else if (cell != 0) { open = false; break; }
                else break;
            }

            // Negative direction
            for (var i = 1; i <= 3; i++)
            {
                var cc = col - dc * i;
                var cr = row - dr * i;
                if (cc < 0 || cc >= Board.Cols || cr < 0 || cr >= Board.Rows) break;
                var cell = board.At(cc, cr);
                if (cell == player) connected++;
                else if (cell != 0) { open = false; break; }
                else break;
            }

            if (connected > 0)
                score += connected * (open ? 0.4f : 0.15f);
        }

        return score;
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..(max - 3)] + "...";
    }
}
