using Ananke.Organics.Division.Review;
using SlackNet.Blocks;

namespace Ananke.Platforms.Slack.Blocks;

/// <summary>
/// Factory for Block Kit approval layouts used by work-item review workflows.
/// Produces Approve / Revise / Reject action buttons wired to well-known action ids.
/// </summary>
public static class SlackApprovalBlocks
{
    /// <summary>Action id posted when a reviewer clicks <b>Approve</b>.</summary>
    public const string ApproveActionId = "ananke_approve";

    /// <summary>Action id posted when a reviewer clicks <b>Revise</b>.</summary>
    public const string ReviseActionId = "ananke_revise";

    /// <summary>Action id posted when a reviewer clicks <b>Reject</b>.</summary>
    public const string RejectActionId = "ananke_reject";

    /// <summary>
    /// Builds a Block Kit block list for the given <paramref name="workItem"/>.
    /// The list contains a header section, a divider, an optional payload context block,
    /// and an actions block with Approve / Revise / Reject buttons.
    /// The <paramref name="workItem"/> id is embedded in each button's <c>value</c> field
    /// so it is returned in the <see cref="SlackNet.Interaction.BlockActionRequest"/> payload.
    /// </summary>
    /// <param name="workItem">The work item under review.</param>
    /// <returns>An ordered, read-only list of blocks ready to pass to
    /// <see cref="ISlackResponseSink.SendBlocksAsync"/>.</returns>
    public static IReadOnlyList<Block> Build(WorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        var kindLabel = workItem.Kind switch
        {
            WorkItemKind.PullRequest => "Pull Request",
            WorkItemKind.DesignDoc => "Design Doc",
            WorkItemKind.Wireframe => "Wireframe",
            _ => "Work Item"
        };

        return
        [
            new SectionBlock
            {
                Text = new Markdown($"*{kindLabel} Review: {workItem.Title}*")
            },
            new DividerBlock(),
            new ContextBlock
            {
                Elements =
                [
                    new Markdown(workItem.Payload.Length > 300
                        ? workItem.Payload[..300] + "…"
                        : workItem.Payload)
                ]
            },
            new ActionsBlock
            {
                Elements =
                [
                    new Button
                    {
                        ActionId = ApproveActionId,
                        Text = new PlainText("Approve"),
                        Style = ButtonStyle.Primary,
                        Value = workItem.Id
                    },
                    new Button
                    {
                        ActionId = ReviseActionId,
                        Text = new PlainText("Revise"),
                        Value = workItem.Id
                    },
                    new Button
                    {
                        ActionId = RejectActionId,
                        Text = new PlainText("Reject"),
                        Style = ButtonStyle.Danger,
                        Value = workItem.Id
                    }
                ]
            }
        ];
    }
}
