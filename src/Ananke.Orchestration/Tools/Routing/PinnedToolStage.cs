using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tools.Routing;

namespace Ananke.Orchestration.Tools.Routing;

/// <summary>
/// Routing stage that ensures a fixed set of tools is always placed at the front
/// of the selection, regardless of what other stages do.
/// </summary>
/// <remarks>
/// Pinned tools correspond to "autonomic reflexes" — always-on capabilities such
/// as <c>list_tools</c> or <c>help</c> that must never be dropped from the window.
/// <para>
/// By default this stage does not terminate the chain; set the <c>terminal</c> constructor
/// flag to <see langword="true"/> for the reflex-only case where no further narrowing is needed.
/// </para>
/// </remarks>
public sealed class PinnedToolStage : ISmartToolRouter
{
    private readonly IReadOnlySet<string> _alwaysOn;
    private readonly bool _terminal;

    /// <summary>
    /// Creates the stage.
    /// </summary>
    /// <param name="alwaysOn">Tool names that must always appear in the selection.</param>
    /// <param name="terminal">
    /// When <see langword="true"/> the chain stops after this stage.
    /// Defaults to <see langword="false"/>.
    /// </param>
    public PinnedToolStage(IReadOnlyList<string> alwaysOn, bool terminal = false)
    {
        ArgumentNullException.ThrowIfNull(alwaysOn);
        _alwaysOn = new HashSet<string>(alwaysOn, StringComparer.Ordinal);
        _terminal = terminal;
    }

    /// <inheritdoc />
    public Task<ToolRoutingDecision> RouteAsync(
        ToolRoutingRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pinned = request.Candidates
            .Where(e => _alwaysOn.Contains(e.ToolName))
            .ToList();

        var rest = request.Candidates
            .Where(e => !_alwaysOn.Contains(e.ToolName))
            .ToList();

        // Pinned entries go to the front
        var selected = new List<ToolMemoryEntry>(pinned.Count + rest.Count);
        selected.AddRange(pinned);
        selected.AddRange(rest);

        return Task.FromResult(new ToolRoutingDecision
        {
            UseTools = true,
            SelectedTools = selected,
            Confidence = RoutingConfidence.High,
            Terminal = _terminal,
        });
    }
}
