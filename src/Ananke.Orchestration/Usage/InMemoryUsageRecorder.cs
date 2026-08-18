using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Usage;

/// <summary>
/// In-memory recorder scoped to one workflow execution. The default when no other
/// recorder is configured, so a workflow with a budget needs no infrastructure.
/// </summary>
/// <remarks>
/// The lock lives here rather than in the runner: recording happens once per model
/// response from concurrent fork branches, so serialisation belongs at the one place
/// every path funnels through. See ADR-arch-028 D9.
/// </remarks>
public sealed class InMemoryUsageRecorder : IUsageRecorder
{
    private readonly object _gate = new();

    private TokenUsage _usage = TokenUsage.Zero;
    private TokenUsage _uncostedUsage = TokenUsage.Zero;
    private decimal _accumulatedCost;
    private bool _hasModelBasedCost;

    /// <inheritdoc />
    public Task RecordUsageAsync(UsageRecord record, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _usage = _usage.Add(record.Usage);

            if (record.ModelCost is { } cost)
            {
                _accumulatedCost += cost;
                _hasModelBasedCost = true;
            }
            else
            {
                // No per-call rate for this call, so a budget has to price it at flat rates.
                _uncostedUsage = _uncostedUsage.Add(record.Usage);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<UsageSnapshot> ReadAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(new UsageSnapshot
            {
                Usage = _usage,
                UncostedUsage = _uncostedUsage,
                AccumulatedCost = _accumulatedCost,
                HasModelBasedCost = _hasModelBasedCost
            });
        }
    }

    /// <inheritdoc />
    public Task ResetAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _usage = TokenUsage.Zero;
            _uncostedUsage = TokenUsage.Zero;
            _accumulatedCost = 0m;
            _hasModelBasedCost = false;
        }

        return Task.CompletedTask;
    }
}
