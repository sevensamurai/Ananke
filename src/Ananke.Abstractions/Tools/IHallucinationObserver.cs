namespace Ananke.Abstractions.Tools;

/// <summary>
/// Receives <see cref="HallucinatedToolCallEvent"/> notifications when the model invokes a tool
/// that is not registered in the ToolKit.
/// Register via <c>ToolKit.WithHallucinationObserver</c>.
/// </summary>
public interface IHallucinationObserver
{
    ValueTask ReportAsync(
        HallucinatedToolCallEvent @event,
        CancellationToken ct = default);
}

/// <summary>Null-object default implementation — discards all events.</summary>
public sealed class NullHallucinationObserver : IHallucinationObserver
{
    public static readonly NullHallucinationObserver Instance = new();

    public ValueTask ReportAsync(
        HallucinatedToolCallEvent @event, CancellationToken ct = default)
        => ValueTask.CompletedTask;
}
