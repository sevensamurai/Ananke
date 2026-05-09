namespace Connect4Demo;

/// <summary>
/// Captures the outcome of one learning-curve window during Discovery Phase training.
/// </summary>
internal sealed record WindowResult(int FromGame, int ToGame, float WinRate, int MemoryCount, int NewEntries);

/// <summary>
/// Aggregates the outcome of the Validation Phase across all tested candidates.
/// </summary>
internal sealed record ValidationResult(int Total, int Reinforced, int Contradicted, int Neutral, float BaselineWinRate);
