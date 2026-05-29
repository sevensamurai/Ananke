namespace Ananke.OpenTelemetry.Budget;

/// <summary>
/// Configuration for <see cref="OpenTelemetryBudgetMeter"/>.
/// </summary>
public sealed record BudgetMeterOptions
{
    /// <summary>
    /// Rolling window used when aggregating budget samples. Defaults to one hour.
    /// </summary>
    public TimeSpan TimeWindow { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Default token cap used when callers query configured caps without supplying an explicit value.
    /// </summary>
    public long DefaultTokenCap { get; set; }

    /// <summary>
    /// Per-role token cap overrides.
    /// </summary>
    public IReadOnlyDictionary<string, long> PerRoleCaps { get; set; } =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
}
