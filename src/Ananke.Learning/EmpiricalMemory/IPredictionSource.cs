namespace Ananke.Learning.EmpiricalMemory;

/// <summary>
/// Forms a prediction for an empirical entry before prediction-error computation.
/// Decouples the "what do we expect?" step from confidence (which becomes a pure
/// stability metric derived from variance). Implementations may use tag overlap
/// with reinforced neighbors, model-based forecasting, or domain heuristics.
/// </summary>
/// <remarks>
/// <para>
/// Without an <see cref="IPredictionSource"/>, the reinforcement path falls back
/// to <see cref="EmpiricalEntry.Confidence"/> as the prediction — preserving the
/// original (circular) behavior for backward compatibility.
/// </para>
/// <para>
/// When provided, the prediction source is called during
/// <see cref="IEmpiricalMemory.ReinforceAsync"/> before the prediction-error
/// computation. The returned value replaces confidence as the predicted signal,
/// breaking the circularity: confidence is now derived purely from variance
/// (prediction-error history), while the prediction is formed independently.
/// </para>
/// </remarks>
public interface IPredictionSource
{
    /// <summary>
    /// Forms a prediction for the given entry based on its context and the
    /// current state of empirical memory.
    /// </summary>
    /// <param name="entry">The entry being reinforced.</param>
    /// <param name="memory">
    /// The memory store — implementations may recall neighbors for
    /// context-aware prediction.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A predicted reward value on the same scale as <see cref="Reinforcement.Reward"/>,
    /// or <c>null</c> to keep the current prediction (or fall back to confidence).
    /// </returns>
    Task<float?> PredictAsync(
        EmpiricalEntry entry,
        IEmpiricalMemory memory,
        CancellationToken ct = default);
}
