using System.Collections.Concurrent;
using Ananke.Organics.Division;

namespace Ananke.Organics.Sensing;

/// <summary>
/// In-memory <see cref="IMeshAggregator"/>. Thread-safe. Raises
/// <see cref="SignalChanged"/> when the stress ratio changes by more than
/// <paramref name="signalDelta"/> (default: 0.05).
/// </summary>
public sealed class InMemoryMeshAggregator(double signalDelta = 0.05) : IMeshAggregator
{
    private readonly ConcurrentDictionary<string, MetabolicSignal> _states = new();
    private double _lastEmittedRatio = -1;

    /// <inheritdoc />
    public event EventHandler<MeshSignal>? SignalChanged;

    /// <inheritdoc />
    public void Report(string cellId, MetabolicSignal signal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cellId);

        _states[cellId] = signal;
        MaybeRaiseChanged();
    }

    /// <inheritdoc />
    public void Forget(string cellId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cellId);

        _states.TryRemove(cellId, out _);
        MaybeRaiseChanged();
    }

    /// <inheritdoc />
    public MeshSignal CurrentSignal()
    {
        var snapshot = _states.ToArray();
        var total = snapshot.Length;
        var stressed = snapshot.Count(kv => kv.Value == MetabolicSignal.Stressed);
        var starved = snapshot.Count(kv => kv.Value == MetabolicSignal.Starved);

        return new MeshSignal
        {
            TotalCells = total,
            StressedCells = stressed,
            StarvedCells = starved,
            SampledAt = DateTimeOffset.UtcNow
        };
    }

    private void MaybeRaiseChanged()
    {
        var signal = CurrentSignal();
        var ratio = signal.StressRatio;

        if (Math.Abs(ratio - _lastEmittedRatio) >= signalDelta)
        {
            _lastEmittedRatio = ratio;
            SignalChanged?.Invoke(this, signal);
        }
    }
}
