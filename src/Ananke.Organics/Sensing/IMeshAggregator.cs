using Ananke.Organics.Division;

namespace Ananke.Organics.Sensing;

/// <summary>
/// Aggregates per-cell <see cref="MetabolicSignal"/> reports into a mesh-wide
/// <see cref="MeshSignal"/> and raises <see cref="SignalChanged"/> when the
/// stress ratio shifts materially.
/// </summary>
public interface IMeshAggregator
{
    /// <summary>
    /// Report the current metabolic state of a cell. Safe to call from any thread.
    /// </summary>
    void Report(string cellId, MetabolicSignal signal);

    /// <summary>Remove a cell from the aggregator (e.g. after it is pruned or killed).</summary>
    void Forget(string cellId);

    /// <summary>Returns the current mesh signal, computed from all reported cells.</summary>
    MeshSignal CurrentSignal();

    /// <summary>
    /// Raised when the mesh stress ratio changes by more than the configured
    /// delta (default: 0.05). Subscribers can react to mesh-wide stress events.
    /// </summary>
    event EventHandler<MeshSignal>? SignalChanged;
}
