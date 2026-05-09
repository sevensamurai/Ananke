using Ananke.Learning.EmpiricalMemory;

namespace Ananke.Learning.Features;

/// <summary>
/// Computes a <see cref="TagImportanceMap"/> by analyzing which semantic tags
/// correlate with positive vs. negative outcomes across empirical entries.
/// </summary>
public interface ITagImportanceTracker
{
    /// <summary>
    /// Analyzes all entries in <paramref name="memory"/> and produces a
    /// <see cref="TagImportanceMap"/>. Returns <see langword="null"/> when
    /// the number of entries with non-zero valence is below the configured
    /// minimum sample size.
    /// </summary>
    Task<TagImportanceMap?> ComputeAsync(
        IEmpiricalMemory memory, CancellationToken ct = default);
}

/// <summary>
/// Configuration for <see cref="ITagImportanceTracker"/> implementations.
/// </summary>
public sealed record TagImportanceOptions
{
    /// <summary>
    /// Minimum number of entries with non-zero <see cref="EmpiricalEntry.Valence"/>
    /// required before a map is produced. Default: 10.
    /// </summary>
    public int MinSampleSize { get; init; } = 10;
}
