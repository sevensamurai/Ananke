using System.Text;
using Ananke.Orchestration.Memory;

namespace Connect4Demo;

/// <summary>
/// Full-screen console renderer. Clears and redraws the entire screen
/// on every update so the board stays in place instead of scrolling.
/// </summary>
internal sealed class Display(GameStats stats, InMemoryEmpiricalMemory memory)
{
    private const int ReasoningCapacity = 6;

    private readonly List<string> _reasoning = [];
    private readonly List<string> _insights = [];
    private string _status = "";
    private string _error = "";

    internal void SetStatus(string status) => _status = status;
    internal void SetError(string error) => _error = error;
    internal void ClearError() => _error = "";

    internal void SetReasoning(List<string> lines)
    {
        _reasoning.Clear();
        _reasoning.AddRange(lines.TakeLast(ReasoningCapacity));
    }

    internal void ClearReasoning() => _reasoning.Clear();

    internal void AddInsight(string insight) => _insights.Add(insight);

    internal void ClearInsights() => _insights.Clear();

    /// <summary>Redraws the entire screen with the current board state.</summary>
    internal void Render(Board board, bool gameOver = false)
    {
        Console.Clear();
        Console.CursorVisible = false;

        var sb = new StringBuilder();

        // ── Header ───────────────────────────────────────────────
        sb.AppendLine("╔══════════════════════════════════════════════════╗");
        sb.AppendLine("║        CONNECT 4 — Empirical Learning        ║");
        sb.AppendLine("╠══════════════════════════════════════════════════╣");
        sb.AppendLine($"║  Game {stats.TotalGames + 1,-3}              " +
            $"Agent: {stats.AgentWins}W {stats.HumanWins}L {stats.Draws}D       ║");
        sb.AppendLine($"║  Memory: {memory.Count} entries" +
            $"{Pad(37 - memory.Count.ToString().Length)}║");
        sb.AppendLine("╠══════════════════════════════════════════════════╣");

        // ── Board ────────────────────────────────────────────────
        sb.AppendLine("║                                                  ║");
        sb.AppendLine("║          1   2   3   4   5   6   7               ║");
        sb.AppendLine("║        ┌───┬───┬───┬───┬───┬───┬───┐             ║");

        for (var r = Board.Rows - 1; r >= 0; r--)
        {
            sb.Append("║        │");
            for (var c = 0; c < Board.Cols; c++)
            {
                var cell = board.At(c, r) switch
                {
                    1 => " X ",
                    2 => " O ",
                    _ => "   "
                };
                sb.Append(cell);
                sb.Append('│');
            }
            sb.AppendLine("             ║");

            if (r > 0)
                sb.AppendLine("║        ├───┼───┼───┼───┼───┼───┼───┤             ║");
        }

        sb.AppendLine("║        └───┴───┴───┴───┴───┴───┴───┘             ║");
        sb.AppendLine("║                                                  ║");

        // ── Agent reasoning panel ────────────────────────────────
        sb.AppendLine("╠══════════════════════════════════════════════════╣");
        sb.AppendLine("║  Agent reasoning:                                ║");

        for (var i = 0; i < ReasoningCapacity; i++)
        {
            if (i < _reasoning.Count)
                sb.AppendLine($"║  {PadRight(_reasoning[i], 48)}║");
            else
                sb.AppendLine($"║  {Pad(48)}║");
        }

        // ── Insights panel (if any) ─────────────────────────────
        if (_insights.Count > 0)
        {
            sb.AppendLine("╠══════════════════════════════════════════════════╣");
            sb.AppendLine("║  🧠 Analysis:                                    ║");
            foreach (var insight in _insights.TakeLast(5))
                sb.AppendLine($"║  {PadRight(insight, 48)}║");
        }

        // ── Status / error line ──────────────────────────────────
        sb.AppendLine("╠══════════════════════════════════════════════════╣");

        if (_error.Length > 0)
            sb.AppendLine($"║  ⚠ {PadRight(_error, 46)}║");
        else if (_status.Length > 0)
            sb.AppendLine($"║  {PadRight(_status, 48)}║");
        else
            sb.AppendLine("║                                                  ║");

        sb.AppendLine("╚══════════════════════════════════════════════════╝");

        Console.Write(sb);
        Console.CursorVisible = true;
    }

    /// <summary>Renders the memory inspection overlay.</summary>
    internal async Task RenderMemoryAsync(Board board)
    {
        Console.Clear();
        Console.CursorVisible = false;

        var sb = new StringBuilder();
        sb.AppendLine("╔══════════════════════════════════════════════════╗");
        sb.AppendLine("║           Empirical Memory                    ║");
        sb.AppendLine("╠══════════════════════════════════════════════════╣");

        var all = await memory.RecallAsync(
            "Connect 4 strategy patterns skills heuristics",
            new RecallOptions { TopK = 12, MinConfidence = 0f });

        if (all.Count == 0)
        {
            sb.AppendLine("║  (empty — play some games first!)                ║");
        }
        else
        {
            foreach (var match in all)
            {
                var e = match.Entry;
                var icon = e.Kind switch
                {
                    EmpiricalKind.Pattern => "🔍",
                    EmpiricalKind.Skill => "🎯",
                    EmpiricalKind.Heuristic => "💡",
                    _ => "  "
                };
                var desc = Truncate(e.Description.ToString(), 42);
                sb.AppendLine($"║  {icon} [{e.Kind,-9}] {PadRight(desc, 34)}║");
                sb.AppendLine($"║     conf: {e.Confidence:F2}  obs: {e.ObservationCount,-3}" +
                    $"  score: {match.Score:F3}{Pad(13)}║");

                // Show top semantic tags if present
                var topTags = FormatTopTags(e.Description, 44);
                if (topTags.Length > 0)
                    sb.AppendLine($"║     {PadRight(topTags, 45)}║");
            }
        }

        sb.AppendLine("╠══════════════════════════════════════════════════╣");
        sb.AppendLine("║  Press any key to return to game...              ║");
        sb.AppendLine("╚══════════════════════════════════════════════════╝");

        Console.Write(sb);
        Console.CursorVisible = true;
        Console.ReadKey(intercept: true);

        // Redraw the game screen
        Render(board);
    }

    /// <summary>Renders the post-game analysis screen.</summary>
    internal void RenderAnalysis(Board board)
    {
        Render(board, gameOver: true);
    }

    /// <summary>Renders the final summary when quitting.</summary>
    internal async Task RenderFinalAsync()
    {
        Console.Clear();

        var sb = new StringBuilder();
        sb.AppendLine("╔══════════════════════════════════════════════════╗");
        sb.AppendLine("║        Thanks for playing! 🧠                    ║");
        sb.AppendLine("╠══════════════════════════════════════════════════╣");
        sb.AppendLine($"║  Games: {stats.TotalGames,-3}  " +
            $"Agent: {stats.AgentWins}W {stats.HumanWins}L {stats.Draws}D" +
            $"{Pad(20 - stats.AgentWins.ToString().Length - stats.HumanWins.ToString().Length - stats.Draws.ToString().Length)}║");
        sb.AppendLine("╠══════════════════════════════════════════════════╣");
        sb.AppendLine("║  What the agent learned:                         ║");
        sb.AppendLine("╠══════════════════════════════════════════════════╣");

        var all = await memory.RecallAsync(
            "Connect 4 strategy",
            new RecallOptions { TopK = 15, MinConfidence = 0f });

        if (all.Count == 0)
        {
            sb.AppendLine("║  (nothing — too few games!)                      ║");
        }
        else
        {
            foreach (var match in all)
            {
                var e = match.Entry;
                var icon = e.Kind switch
                {
                    EmpiricalKind.Pattern => "🔍",
                    EmpiricalKind.Skill => "🎯",
                    EmpiricalKind.Heuristic => "💡",
                    _ => "  "
                };
                sb.AppendLine($"║  {icon} {PadRight(Truncate(e.Description.ToString(), 44), 46)}║");
                sb.AppendLine($"║     conf: {e.Confidence:F2}  obs: {e.ObservationCount}" +
                    $"{Pad(33 - e.ObservationCount.ToString().Length)}║");

                var finalTags = FormatTopTags(e.Description, 44);
                if (finalTags.Length > 0)
                    sb.AppendLine($"║     {PadRight(finalTags, 45)}║");
            }
        }

        sb.AppendLine("╚══════════════════════════════════════════════════╝");
        Console.Write(sb);
    }

    private static string Pad(int width) =>
        width > 0 ? new string(' ', width) : "";

    private static string PadRight(string s, int width)
    {
        // Account for multi-byte emoji by measuring visible length
        if (s.Length >= width) return s[..width];
        return s + new string(' ', width - s.Length);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 3)] + "...";

    /// <summary>
    /// Formats the top semantic tags from a <see cref="SemanticDescription"/> as
    /// a compact string for display. Returns empty if no tags are present.
    /// </summary>
    private static string FormatTopTags(SemanticDescription description, int maxLength)
    {
        if (description.SemanticTags.Count == 0)
            return string.Empty;

        var tags = description.SemanticTags
            .OrderByDescending(t => t.Value)
            .Select(t => $"{t.Key}({t.Value:F1})")
            .ToList();

        // Build up the string, stopping when we'd exceed maxLength
        var result = new StringBuilder();
        foreach (var tag in tags)
        {
            var next = result.Length == 0 ? tag : $" {tag}";
            if (result.Length + next.Length > maxLength) break;
            result.Append(next);
        }

        return result.ToString();
    }
}
