namespace Connect4Demo;

/// <summary>
/// Extracts structural features from a <see cref="Board"/> as text tokens.
/// Each of the 69 possible winning lines (24 horizontal, 21 vertical,
/// 12 diagonal-ascending, 12 diagonal-descending) is encoded as a single
/// compound token using underscores (safe from <see cref="Ananke.Orchestration.Knowledge.Embeddings.InMemoryEmbedder"/>
/// separator splitting). Boards with similar threat structures produce
/// similar hash vectors via shared tokens.
/// </summary>
/// <remarks>
/// Token format examples:
/// <list type="bullet">
///   <item><c>h_a2f2</c> — horizontal line, agent has 2, 2 free cells</item>
///   <item><c>da_e3f1</c> — diagonal ascending, enemy has 3, 1 free (danger!)</item>
///   <item><c>c3h2</c> — column 3 has height 2</item>
/// </list>
/// Two boards that both contain <c>da_e3f1</c> cluster together regardless of
/// where on the board the diagonal is — the agent learns "ascending diagonal
/// with enemy 3 and 1 free is dangerous" without being told.
/// </remarks>
internal static class BoardFeatures
{
    // Pre-computed line definitions: (startCol, startRow, deltaCol, deltaRow, dirToken)
    private static readonly (int C, int R, int DC, int DR, string Dir)[] Lines = BuildLineTable();

    /// <summary>
    /// Produces a structural description of the board as space-separated tokens.
    /// Each token is a single underscore-joined word that won't be split by
    /// the embedder. Only live lines (one player + free cells) are included.
    /// </summary>
    internal static string Describe(Board board, int agentPlayer = 2)
    {
        var opponentPlayer = agentPlayer == 2 ? 1 : 2;
        var parts = new List<string>();

        // Game phase — single word
        parts.Add(board.MoveCount switch
        {
            < 6 => "opening",
            < 16 => "midgame",
            _ => "endgame"
        });

        // Column heights — one token per non-empty column
        for (var c = 0; c < Board.Cols; c++)
        {
            var h = board.Height(c);
            if (h > 0)
                parts.Add($"c{c}h{h}");
        }

        // Line threat tokens: one compound token per live line
        // Token = {dir}_{side}{count}f{free}
        // e.g. h_a2f2 = horizontal, agent 2 pieces, 2 free
        //      da_e3f1 = diag ascending, enemy 3 pieces, 1 free
        foreach (var (c, r, dc, dr, dir) in Lines)
        {
            var agent = 0;
            var opp = 0;
            var free = 0;

            for (var i = 0; i < Board.WinLength; i++)
            {
                var cell = board.At(c + dc * i, r + dr * i);
                if (cell == agentPlayer) agent++;
                else if (cell == opponentPlayer) opp++;
                else free++;
            }

            // Skip empty lines (no pieces) and dead lines (both players present)
            if (agent + opp == 0) continue;
            if (agent > 0 && opp > 0) continue;

            // Each token is one hashable word: direction + side + count + free
            if (agent > 0)
                parts.Add($"{dir}_a{agent}f{free}");
            else
                parts.Add($"{dir}_e{opp}f{free}");
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// Decomposes the board into weighted semantic tags for structured recall.
    /// Unlike <see cref="Describe"/>, which produces flat text tokens for embedding,
    /// this returns namespaced tags with relevance weights that enable causal-aware
    /// dedup and dimension-projected recall via <see cref="Ananke.Learning.SemanticDescription"/>.
    /// </summary>
    /// <remarks>
    /// Tags describe <b>raw structural observations</b>, not strategic interpretations.
    /// The memory system discovers which observations correlate with outcomes.
    /// <list type="bullet">
    ///   <item><c>phase:*</c> — game stage bucket by move count</item>
    ///   <item><c>action:col_N</c> — column played</item>
    ///   <item><c>outcome:*</c> — win/loss/draw result</item>
    ///   <item><c>center:agent</c> / <c>center:opponent</c> — piece count in center column</item>
    ///   <item><c>line:a1..a3</c> / <c>line:e1..e3</c> — live line counts by fill level</item>
    /// </list>
    /// </remarks>
    /// <param name="board">Board to analyze.</param>
    /// <param name="action">Optional column played — adds an <c>action:col_N</c> tag.</param>
    /// <param name="outcome">Optional game outcome — adds an <c>outcome:*</c> tag.</param>
    /// <param name="agentPlayer">Which player number is the agent.</param>
    internal static Dictionary<string, float> Decompose(
        Board board, int? action = null, string? outcome = null, int agentPlayer = 2)
    {
        var opponentPlayer = agentPlayer == 2 ? 1 : 2;
        var tags = new Dictionary<string, float>();

        // ── Game phase ───────────────────────────────────────────
        var phase = board.MoveCount switch
        {
            < 6 => "opening",
            < 16 => "midgame",
            _ => "endgame"
        };
        tags[$"phase:{phase}"] = 0.4f;

        // ── Action played ────────────────────────────────────────
        if (action is not null)
            tags[$"action:col_{action.Value}"] = 0.9f;

        // ── Game outcome ─────────────────────────────────────────
        if (outcome is not null)
            tags[$"outcome:{outcome}"] = 1.0f;

        // ── Center column piece counts (raw observation) ─────────
        var agentCenter = 0;
        var oppCenter = 0;
        for (var r = 0; r < Board.Rows; r++)
        {
            var cell = board.At(3, r);
            if (cell == agentPlayer) agentCenter++;
            else if (cell == opponentPlayer) oppCenter++;
        }

        if (agentCenter > 0)
            tags["center:agent"] = agentCenter / (float)Board.Rows;
        if (oppCenter > 0)
            tags["center:opponent"] = oppCenter / (float)Board.Rows;

        // ── Line fill counts (raw structural observation) ────────
        var agentLines = new int[5];
        var oppLines = new int[5];

        foreach (var (c, r, dc, dr, _) in Lines)
        {
            var ag = 0;
            var op = 0;

            for (var i = 0; i < Board.WinLength; i++)
            {
                var cell = board.At(c + dc * i, r + dr * i);
                if (cell == agentPlayer) ag++;
                else if (cell == opponentPlayer) op++;
            }

            if (ag > 0 && op == 0) agentLines[ag]++;
            if (op > 0 && ag == 0) oppLines[op]++;
        }

        // Agent line fill — weight proportional to count
        if (agentLines[3] > 0)
            tags["line:a3"] = MathF.Min(1f, agentLines[3] * 0.4f);
        if (agentLines[2] > 0)
            tags["line:a2"] = MathF.Min(1f, agentLines[2] * 0.1f);
        if (agentLines[1] > 0)
            tags["line:a1"] = MathF.Min(0.5f, agentLines[1] * 0.03f);

        // Opponent line fill — weight proportional to count
        if (oppLines[3] > 0)
            tags["line:e3"] = MathF.Min(1f, oppLines[3] * 0.4f);
        if (oppLines[2] > 0)
            tags["line:e2"] = MathF.Min(1f, oppLines[2] * 0.1f);
        if (oppLines[1] > 0)
            tags["line:e1"] = MathF.Min(0.5f, oppLines[1] * 0.03f);

        return tags;
    }

    /// <summary>
    /// Returns a compact summary of threat counts suitable for display.
    /// </summary>
    internal static string Summarize(Board board, int agentPlayer = 2)
    {
        var opponentPlayer = agentPlayer == 2 ? 1 : 2;
        var agentLines = new int[5]; // index = piece count (0-4)
        var oppLines = new int[5];

        foreach (var (c, r, dc, dr, _) in Lines)
        {
            var agent = 0;
            var opp = 0;

            for (var i = 0; i < Board.WinLength; i++)
            {
                var cell = board.At(c + dc * i, r + dr * i);
                if (cell == agentPlayer) agent++;
                else if (cell == opponentPlayer) opp++;
            }

            if (agent > 0 && opp == 0) agentLines[agent]++;
            if (opp > 0 && agent == 0) oppLines[opp]++;
        }

        return $"agent lines: 1x{agentLines[1]} 2x{agentLines[2]} 3x{agentLines[3]} 4x{agentLines[4]} | " +
               $"opp lines: 1x{oppLines[1]} 2x{oppLines[2]} 3x{oppLines[3]} 4x{oppLines[4]}";
    }

    /// <summary>
    /// Builds the table of all 69 possible winning lines on a 7×6 board.
    /// Direction tokens use underscore-safe abbreviations: h, v, da, dd.
    /// </summary>
    private static (int C, int R, int DC, int DR, string Dir)[] BuildLineTable()
    {
        var lines = new List<(int, int, int, int, string)>();

        for (var c = 0; c < Board.Cols; c++)
        {
            for (var r = 0; r < Board.Rows; r++)
            {
                if (c + 3 < Board.Cols)
                    lines.Add((c, r, 1, 0, "h"));

                if (r + 3 < Board.Rows)
                    lines.Add((c, r, 0, 1, "v"));

                if (c + 3 < Board.Cols && r + 3 < Board.Rows)
                    lines.Add((c, r, 1, 1, "da"));

                if (c + 3 < Board.Cols && r >= 3)
                    lines.Add((c, r, 1, -1, "dd"));
            }
        }

        return [.. lines];
    }
}
