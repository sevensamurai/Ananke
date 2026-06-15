using System.Collections.Concurrent;

namespace Ananke.Abstractions.Budget;

/// <summary>
/// Thread-safe in-memory <see cref="IBudgetMeter"/> backed by a rolling time window.
/// </summary>
public sealed class InMemoryBudgetMeter(
    TimeSpan? timeWindow = null,
    TimeProvider? clock = null) : IBudgetMeter
{
    private readonly TimeSpan _timeWindow = timeWindow ?? TimeSpan.FromHours(1);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, RoleBudgetLedger> _ledgers =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Records budget usage for the specified workflow or role key.
    /// </summary>
    public void Record(string role, long tokensIn, long tokensOut, decimal estimatedUsd)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        if (tokensIn < 0)
            throw new ArgumentOutOfRangeException(nameof(tokensIn));
        if (tokensOut < 0)
            throw new ArgumentOutOfRangeException(nameof(tokensOut));
        if (estimatedUsd < 0)
            throw new ArgumentOutOfRangeException(nameof(estimatedUsd));

        var ledger = _ledgers.GetOrAdd(role, static _ => new RoleBudgetLedger());
        ledger.Record(_clock.GetUtcNow(), tokensIn, tokensOut, estimatedUsd);
    }

    /// <inheritdoc />
    public BudgetSpend GetCurrentSpend(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        if (!_ledgers.TryGetValue(role, out var ledger))
        {
            return new BudgetSpend
            {
                TokensIn = 0,
                TokensOut = 0,
                EstimatedUsd = 0m
            };
        }

        return ledger.Snapshot(_clock.GetUtcNow(), _timeWindow);
    }

    /// <inheritdoc />
    public bool IsOverCap(string role, long cap)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cap);

        var spend = GetCurrentSpend(role);
        return spend.TokensIn + spend.TokensOut >= cap;
    }

    private sealed class RoleBudgetLedger
    {
        private readonly Queue<BudgetSample> _samples = new();
        private readonly Lock _lock = new();

        public void Record(DateTimeOffset timestamp, long tokensIn, long tokensOut, decimal estimatedUsd)
        {
            lock (_lock)
            {
                _samples.Enqueue(new BudgetSample(timestamp, tokensIn, tokensOut, estimatedUsd));
            }
        }

        public BudgetSpend Snapshot(DateTimeOffset now, TimeSpan window)
        {
            lock (_lock)
            {
                Prune(now, window);

                long tokensIn = 0;
                long tokensOut = 0;
                decimal estimatedUsd = 0;

                foreach (var sample in _samples)
                {
                    tokensIn += sample.TokensIn;
                    tokensOut += sample.TokensOut;
                    estimatedUsd += sample.EstimatedUsd;
                }

                return new BudgetSpend
                {
                    TokensIn = tokensIn,
                    TokensOut = tokensOut,
                    EstimatedUsd = estimatedUsd
                };
            }
        }

        private void Prune(DateTimeOffset now, TimeSpan window)
        {
            while (_samples.Count > 0 && now - _samples.Peek().Timestamp > window)
                _samples.Dequeue();
        }
    }

    private readonly record struct BudgetSample(
        DateTimeOffset Timestamp,
        long TokensIn,
        long TokensOut,
        decimal EstimatedUsd);
}
