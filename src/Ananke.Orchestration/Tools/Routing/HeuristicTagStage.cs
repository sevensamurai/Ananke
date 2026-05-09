using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tools.Routing;

namespace Ananke.Orchestration.Tools.Routing;

/// <summary>
/// Routing stage that keeps candidates whose <see cref="ToolMemoryEntry.Tags"/>
/// intersect with the tag set derived from the user message by a user-supplied
/// function.
/// </summary>
/// <remarks>
/// Returns <see cref="RoutingConfidence.Medium"/> because heuristics are not
/// authoritative — the <see cref="CompositeSmartToolRouter"/> will continue to
/// the next stage and may further narrow.
/// </remarks>
public sealed class HeuristicTagStage : ISmartToolRouter
{
    private readonly Func<string, IReadOnlySet<string>> _messageToTags;

    /// <summary>
    /// Creates the stage.
    /// </summary>
    /// <param name="messageToTags">
    /// Function that maps the user message to a set of relevant tags.
    /// Called once per routing request.
    /// </param>
    public HeuristicTagStage(Func<string, IReadOnlySet<string>> messageToTags)
    {
        ArgumentNullException.ThrowIfNull(messageToTags);
        _messageToTags = messageToTags;
    }

    /// <inheritdoc />
    public Task<ToolRoutingDecision> RouteAsync(
        ToolRoutingRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tags = _messageToTags(request.UserMessage);

        var selected = request.Candidates
            .Where(e => e.Tags.Any(t => tags.Contains(t)))
            .ToList();

        return Task.FromResult(new ToolRoutingDecision
        {
            UseTools = true,
            SelectedTools = selected,
            Confidence = RoutingConfidence.Medium,
        });
    }
}
