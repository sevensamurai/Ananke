using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Usage;

namespace Ananke.Orchestration.Budget;

/// <summary>
/// An <see cref="IUsageRecorder"/> that persists a period's spend to a local file, so a monthly
/// ceiling survives process restarts.
/// </summary>
/// <remarks>
/// <para>
/// The shipped default for period budgets. In-box and dependency-free — an in-memory default
/// would match <c>InMemoryCheckpointStore</c> for symmetry but would silently fail the feature's
/// only purpose, since without persistence a crash-loop re-spends the same budget indefinitely.
/// </para>
/// <para>
/// <b>The period is part of the file name</b>, so rollover needs no scheduled task: on the first
/// call of a new period the old file is simply no longer the one being written.
/// </para>
/// <para>
/// Concurrency is this class's business, not the interface's. A semaphore serialises callers in
/// this process — fork branches record in parallel — and an exclusive file handle with bounded
/// retry serialises across processes.
/// </para>
/// </remarks>
public sealed class FileUsageRecorder : IUsageRecorder, IDisposable
{
    private const int MaxLockAttempts = 20;
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(25);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly BudgetId _id;
    private readonly string _directory;
    private readonly TimeProvider _timeProvider;
    private readonly int _anchorDay;
    private readonly string _fileStem;

    /// <summary>Creates a recorder for one budget key, storing period files under a directory.</summary>
    /// <param name="id">Which budget this records against.</param>
    /// <param name="directory">
    /// Where period files live. Created if absent. Required rather than defaulted — a store that
    /// silently picks its own path is one nobody can find in an incident.
    /// </param>
    /// <param name="timeProvider">Clock used to resolve the current period.</param>
    /// <param name="anchorDay">Day of month the period starts on. See <see cref="BudgetPeriod"/>.</param>
    public FileUsageRecorder(
        BudgetId id,
        string directory,
        TimeProvider timeProvider,
        int anchorDay = BudgetPeriod.CalendarMonthAnchor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(anchorDay, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(anchorDay, 31);

        _id = id;
        _directory = directory;
        _timeProvider = timeProvider;
        _anchorDay = anchorDay;
        _fileStem = DeriveFileStem(id);
    }

    /// <inheritdoc />
    public async Task RecordUsageAsync(UsageRecord record, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = CurrentPath();
            var totals = await ReadFileAsync(path, ct).ConfigureAwait(false);

            await WriteFileAsync(path, totals with
            {
                InputTokens = totals.InputTokens + record.Usage.InputTokens,
                OutputTokens = totals.OutputTokens + record.Usage.OutputTokens,
                // Tokens that arrived with no per-call rate must be kept apart, or a budget
                // cannot price them at flat rates later.
                UncostedInputTokens = totals.UncostedInputTokens +
                    (record.ModelCost is null ? record.Usage.InputTokens : 0),
                UncostedOutputTokens = totals.UncostedOutputTokens +
                    (record.ModelCost is null ? record.Usage.OutputTokens : 0),
                AccumulatedCost = totals.AccumulatedCost + (record.ModelCost ?? 0m),
                HasModelBasedCost = totals.HasModelBasedCost || record.ModelCost is not null
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<UsageSnapshot> ReadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var totals = await ReadFileAsync(CurrentPath(), ct).ConfigureAwait(false);
            return new UsageSnapshot
            {
                // TokenUsage counts are int; a period total is accumulated as long so a very
                // large month cannot silently wrap. Saturating keeps the reported figure
                // monotonic — the budget itself is enforced on cost, which is decimal.
                Usage = new TokenUsage
                {
                    InputTokens = (int)Math.Min(totals.InputTokens, int.MaxValue),
                    OutputTokens = (int)Math.Min(totals.OutputTokens, int.MaxValue)
                },
                UncostedUsage = new TokenUsage
                {
                    InputTokens = (int)Math.Min(totals.UncostedInputTokens, int.MaxValue),
                    OutputTokens = (int)Math.Min(totals.UncostedOutputTokens, int.MaxValue)
                },
                AccumulatedCost = totals.AccumulatedCost,
                HasModelBasedCost = totals.HasModelBasedCost
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task ResetAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var path = CurrentPath();
            if (File.Exists(path))
                File.Delete(path);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The file backing the current period. Public for diagnostics.</summary>
    public string CurrentPath() => Path.Combine(
        _directory,
        $"{_fileStem}_{BudgetPeriod.StartOfPeriod(_timeProvider.GetUtcNow(), _anchorDay):yyyy-MM-dd}.json");

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// A filename derived from the key rather than the key itself: a <see cref="BudgetId"/> is a
    /// storage key and may legitimately contain separators or other characters a path cannot.
    /// A readable prefix keeps a directory listing meaningful; the hash makes it unambiguous.
    /// </summary>
    private static string DeriveFileStem(BudgetId id)
    {
        var hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(id.Value)))[..16];

        var prefix = new StringBuilder();
        foreach (var c in id.Value)
        {
            if (prefix.Length == 24) break;
            prefix.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-');
        }

        return prefix.Length == 0 ? hash : $"{prefix}-{hash}";
    }

    private async Task<PeriodTotals> ReadFileAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            return PeriodTotals.Empty(_id, path);

        await using var stream = await OpenExclusiveAsync(path, FileMode.Open, FileAccess.Read, ct).ConfigureAwait(false);
        if (stream.Length == 0)
            return PeriodTotals.Empty(_id, path);

        var totals = await JsonSerializer.DeserializeAsync<PeriodTotals>(stream, cancellationToken: ct).ConfigureAwait(false);
        return totals ?? PeriodTotals.Empty(_id, path);
    }

    private async Task WriteFileAsync(string path, PeriodTotals totals, CancellationToken ct)
    {
        Directory.CreateDirectory(_directory);

        await using var stream = await OpenExclusiveAsync(path, FileMode.Create, FileAccess.Write, ct).ConfigureAwait(false);
        await JsonSerializer.SerializeAsync(stream, totals, cancellationToken: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens with no sharing, retrying briefly: another process may hold the file for its own
    /// read-modify-write. Bounded rather than indefinite — a stuck holder should surface as an
    /// error the caller can fail closed on, not as a hang.
    /// </summary>
    private static async Task<FileStream> OpenExclusiveAsync(
        string path, FileMode mode, FileAccess access, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, mode, access, FileShare.None);
            }
            catch (IOException) when (attempt < MaxLockAttempts)
            {
                await Task.Delay(LockRetryDelay, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>On-disk shape. Carries the logical key and period for readability in an incident.</summary>
    internal sealed record PeriodTotals
    {
        public required string BudgetId { get; init; }
        public required string PeriodFile { get; init; }
        public long InputTokens { get; init; }
        public long OutputTokens { get; init; }
        public long UncostedInputTokens { get; init; }
        public long UncostedOutputTokens { get; init; }
        public decimal AccumulatedCost { get; init; }
        public bool HasModelBasedCost { get; init; }

        public static PeriodTotals Empty(BudgetId id, string path) => new()
        {
            BudgetId = id.Value,
            PeriodFile = Path.GetFileName(path)
        };
    }
}
