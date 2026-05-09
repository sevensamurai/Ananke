namespace Connect4Demo;

/// <summary>
/// Connect 4 board — 7 columns × 6 rows.
/// Column 0 is left, row 0 is bottom.
/// </summary>
internal sealed class Board
{
    internal const int Cols = 7;
    internal const int Rows = 6;
    internal const int WinLength = 4;

    // _grid[col, row] — 0 = empty, 1 = player 1 (human), 2 = player 2 (agent)
    private readonly int[,] _grid = new int[Cols, Rows];
    private readonly int[] _heights = new int[Cols]; // next free row per column
    private readonly List<(int Col, int Player)> _moveHistory = [];

    internal int MoveCount => _moveHistory.Count;
    internal IReadOnlyList<(int Col, int Player)> MoveHistory => _moveHistory;

    /// <summary>Returns columns that are not full.</summary>
    internal List<int> LegalMoves()
    {
        var moves = new List<int>();
        for (var c = 0; c < Cols; c++)
            if (_heights[c] < Rows) moves.Add(c);
        return moves;
    }

    /// <summary>Drops a piece into a column. Returns the row it landed on.</summary>
    internal int Drop(int col, int player)
    {
        if (col < 0 || col >= Cols) throw new ArgumentOutOfRangeException(nameof(col));
        if (_heights[col] >= Rows) throw new InvalidOperationException($"Column {col + 1} is full.");

        var row = _heights[col]++;
        _grid[col, row] = player;
        _moveHistory.Add((col, player));
        return row;
    }

    /// <summary>Checks if the last move at (col, row) created a win.</summary>
    internal bool CheckWin(int col, int row)
    {
        var player = _grid[col, row];
        if (player == 0) return false;

        // Four directions: horizontal, vertical, diagonal /, diagonal \
        ReadOnlySpan<(int dc, int dr)> directions = [(1, 0), (0, 1), (1, 1), (1, -1)];

        foreach (var (dc, dr) in directions)
        {
            var count = 1;
            count += CountDirection(col, row, dc, dr, player);
            count += CountDirection(col, row, -dc, -dr, player);
            if (count >= WinLength) return true;
        }

        return false;
    }

    /// <summary>Returns true if the board is completely full.</summary>
    internal bool IsFull() => _heights.All(h => h >= Rows);

    /// <summary>Gets the player at a cell (0 = empty, 1 = human, 2 = agent).</summary>
    internal int At(int col, int row) => _grid[col, row];

    /// <summary>Returns the current height (next free row) of a column.</summary>
    internal int Height(int col) => _heights[col];

    /// <summary>Creates a deep copy of the board for simulation and lookahead.</summary>
    internal Board Clone()
    {
        var clone = new Board();
        for (var c = 0; c < Cols; c++)
        {
            for (var r = 0; r < Rows; r++)
                clone._grid[c, r] = _grid[c, r];
            clone._heights[c] = _heights[c];
        }
        clone._moveHistory.AddRange(_moveHistory);
        return clone;
    }

    /// <summary>
    /// Checks if dropping in <paramref name="col"/> would give <paramref name="player"/> a win.
    /// </summary>
    internal bool WouldWin(int col, int player)
    {
        if (_heights[col] >= Rows) return false;
        var row = _heights[col];
        _grid[col, row] = player;
        _heights[col]++;
        var wins = CheckWin(col, row);
        _heights[col]--;
        _grid[col, row] = 0;
        return wins;
    }

    /// <summary>Renders the board as an ASCII string for console display.</summary>
    internal string Render()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("  1 2 3 4 5 6 7");

        for (var r = Rows - 1; r >= 0; r--)
        {
            sb.Append("  ");
            for (var c = 0; c < Cols; c++)
            {
                var cell = _grid[c, r] switch
                {
                    1 => "X",
                    2 => "O",
                    _ => "·"
                };
                if (c > 0) sb.Append(' ');
                sb.Append(cell);
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private int CountDirection(int col, int row, int dc, int dr, int player)
    {
        var count = 0;
        var c = col + dc;
        var r = row + dr;

        while (c >= 0 && c < Cols && r >= 0 && r < Rows && _grid[c, r] == player)
        {
            count++;
            c += dc;
            r += dr;
        }

        return count;
    }
}
