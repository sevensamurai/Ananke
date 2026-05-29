using Ananke.Organics.Sensing;

namespace Ananke.Roles.Studio;

/// <summary>
/// Studio-aware <see cref="IRequestRouter"/> that checks keyword overrides before delegating to an inner router.
/// </summary>
public sealed class StudioRouter(
    IRequestRouter inner,
    IReadOnlyDictionary<string, string> keywordToWorkflowMap,
    string defaultWorkflowName) : IRequestRouter
{
    /// <inheritdoc />
    public async Task<string> RouteAsync(string userMessage, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(keywordToWorkflowMap);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultWorkflowName);

        foreach (var entry in keywordToWorkflowMap)
        {
            if (userMessage.Contains(entry.Key, StringComparison.OrdinalIgnoreCase))
                return entry.Value;
        }

        try
        {
            var routed = await inner.RouteAsync(userMessage, ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(routed) ? defaultWorkflowName : routed;
        }
        catch (InvalidOperationException)
        {
            return defaultWorkflowName;
        }
    }
}
