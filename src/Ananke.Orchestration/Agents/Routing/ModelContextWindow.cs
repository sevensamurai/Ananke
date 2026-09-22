namespace Ananke.Orchestration.Agents.Routing;

/// <summary>
/// The real context window of the model a request would be routed to.
/// </summary>
/// <param name="ModelName">The selected model's name, for per-model reporting.</param>
/// <param name="ContextTokens">
/// <see cref="ModelProfile.ContextTokens"/> — the <em>effective</em> window, which a local
/// deployment may have narrowed. <c>0</c> means unknown, matching <see cref="ModelProfile"/>'s
/// own convention.
/// </param>
public readonly record struct ModelContextWindow(string ModelName, int ContextTokens)
{
    /// <summary>Returned when the window cannot be determined.</summary>
    public static ModelContextWindow Unknown { get; } = new(string.Empty, 0);

    /// <summary>Whether a usable window was resolved.</summary>
    public bool IsKnown => ContextTokens > 0;
}
