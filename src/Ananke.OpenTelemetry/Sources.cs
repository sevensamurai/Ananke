namespace Ananke.OpenTelemetry;

/// <summary>
/// Well-known <see cref="System.Diagnostics.ActivitySource"/> names used across Ananke packages.
/// Pass these to <see cref="OtelTracingOptions.AddSource"/> or use the defaults.
/// </summary>
public static class Sources
{
    /// <summary>Activity source for <c>Ananke.Orchestration</c> workflow spans.</summary>
    public const string Orchestration = "Ananke.Orchestration";

    /// <summary>Activity source for <c>Ananke.StateMachine</c> transition spans.</summary>
    public const string StateMachine = "Ananke.StateMachine";
}
