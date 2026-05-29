using Ananke.Abstractions.Agents;
using SlackNet;
using SlackNet.Events;
using SlackNet.Interaction;
using System.Text.RegularExpressions;
namespace Ananke.Platforms.Slack;

/// <summary>
/// Maps Slack events to <see cref="PlatformMessage"/> instances.
/// </summary>
internal static class SlackMessageMapper
{
    private static readonly Regex MentionPrefixRegex =
        new("^<@[A-Z0-9]+>\\s*", RegexOptions.Compiled);

    /// <summary>
    /// Converts a Slack <see cref="MessageEvent"/> to a normalized <see cref="PlatformMessage"/>.
    /// </summary>
    internal static PlatformMessage FromSlackEvent(MessageEvent slackEvent)
    {
        return new PlatformMessage
        {
            ChannelId = slackEvent.Channel ?? string.Empty,
            ThreadId = slackEvent.ThreadTs,
            UserId = slackEvent.User ?? "unknown",
            PlatformMessageId = slackEvent.Ts,
            Message = AgentMessage.User(slackEvent.Text ?? string.Empty),
            PlatformContext = slackEvent
        };
    }

    /// <summary>
    /// Converts a Slack <see cref="AppMention"/> to a normalized <see cref="PlatformMessage"/>.
    /// </summary>
    internal static PlatformMessage FromAppMention(AppMention slackEvent)
    {
        return new PlatformMessage
        {
            ChannelId = slackEvent.Channel ?? string.Empty,
            ThreadId = slackEvent.ThreadTs,
            UserId = slackEvent.User ?? "unknown",
            PlatformMessageId = slackEvent.Ts,
            Message = AgentMessage.User(StripMentionPrefix(slackEvent.Text)),
            PlatformContext = slackEvent
        };
    }

    /// <summary>
    /// Converts a Slack <see cref="ReactionAdded"/> to a normalized <see cref="PlatformReactionEvent"/>.
    /// </summary>
    internal static PlatformReactionEvent FromReactionAdded(ReactionAdded slackEvent)
    {
        return new PlatformReactionEvent
        {
            UserId = slackEvent.User ?? "unknown",
            ChannelId = GetReactionItemProperty(slackEvent.Item, "Channel") ?? string.Empty,
            MessageTs = GetReactionItemProperty(slackEvent.Item, "Ts") ?? string.Empty,
            Reaction = slackEvent.Reaction ?? string.Empty,
            Added = true
        };
    }

    private static string StripMentionPrefix(string? text) =>
        MentionPrefixRegex.Replace(text ?? string.Empty, string.Empty, 1).TrimStart();

    private static string? GetReactionItemProperty(ReactionItem? item, string propertyName)
    {
        if (item is null)
            return null;

        return item.GetType().GetProperty(propertyName)?.GetValue(item)?.ToString();
    }

    /// <summary>
    /// Converts a Slack <see cref="SlashCommand"/> to a normalized <see cref="PlatformSlashCommand"/>.
    /// </summary>
    internal static PlatformSlashCommand FromSlashCommand(SlashCommand slackCommand)
    {
        return new PlatformSlashCommand
        {
            Command = slackCommand.Command ?? string.Empty,
            Text = slackCommand.Text?.Trim() ?? string.Empty,
            UserId = slackCommand.UserId ?? "unknown",
            ChannelId = string.Empty, // SlashCommand has no channel in this version; TriggerId only
            TriggerId = slackCommand.TriggerId,
            PlatformContext = slackCommand
        };
    }

    /// <summary>
    /// Converts a Slack <see cref="BlockActionRequest"/> to a normalized <see cref="PlatformInteractionEvent"/>.
    /// </summary>
    internal static PlatformInteractionEvent FromBlockActionRequest(BlockActionRequest request)
    {
        var action = request.Actions.Count > 0 ? request.Actions[0] : null;
        var value = action is not null
            ? action.GetType().GetProperty("Value")?.GetValue(action)?.ToString()
            : null;

        return new PlatformInteractionEvent
        {
            Kind = PlatformInteractionKind.BlockAction,
            ActionId = action?.ActionId,
            Value = value,
            UserId = request.User?.Id ?? "unknown",
            ChannelId = request.Channel?.Id,
            ThreadId = request.Message?.ThreadTs,
            TriggerId = request.TriggerId,
            PlatformContext = request
        };
    }

    /// <summary>
    /// Converts a Slack <see cref="ViewSubmission"/> to a normalized <see cref="PlatformInteractionEvent"/>.
    /// </summary>
    internal static PlatformInteractionEvent FromViewSubmission(ViewSubmission viewSubmission, InteractionRequest request)
    {
        return new PlatformInteractionEvent
        {
            Kind = PlatformInteractionKind.ViewSubmission,
            ActionId = viewSubmission.View?.CallbackId,
            Value = null,
            UserId = request.User?.Id ?? "unknown",
            ChannelId = request.Channel?.Id,
            TriggerId = null,
            PlatformContext = viewSubmission
        };
    }

    /// <summary>
    /// Converts a Slack <see cref="ViewClosed"/> to a normalized <see cref="PlatformInteractionEvent"/>.
    /// </summary>
    internal static PlatformInteractionEvent FromViewClosed(ViewClosed viewClosed, InteractionRequest request)
    {
        return new PlatformInteractionEvent
        {
            Kind = PlatformInteractionKind.ViewClosed,
            ActionId = viewClosed.View?.CallbackId,
            Value = null,
            UserId = request.User?.Id ?? "unknown",
            ChannelId = request.Channel?.Id,
            TriggerId = null,
            PlatformContext = viewClosed
        };
    }

    /// <summary>
    /// Converts a Slack <see cref="AssistantThreadStarted"/> to a normalized
    /// <see cref="PlatformAssistantThreadEvent"/>.
    /// </summary>
    internal static PlatformAssistantThreadEvent FromAssistantThreadStarted(
        AssistantThreadStarted slackEvent)
    {
        var thread = slackEvent.AssistantThread;
        return new PlatformAssistantThreadEvent
        {
            Kind = AssistantThreadEventKind.Started,
            UserId = thread.UserId ?? "unknown",
            ChannelId = thread.ChannelId ?? string.Empty,
            ThreadId = thread.ThreadTs ?? string.Empty,
            SourceContext = null,
            PlatformContext = slackEvent
        };
    }

    /// <summary>
    /// Converts a Slack <see cref="AssistantThreadContextChanged"/> to a normalized
    /// <see cref="PlatformAssistantThreadEvent"/>.
    /// </summary>
    internal static PlatformAssistantThreadEvent FromAssistantThreadContextChanged(
        AssistantThreadContextChanged slackEvent)
    {
        var thread = slackEvent.AssistantThread;
        var ctx = thread.Context;
        var sourceContext = new Dictionary<string, string>(3);
        if (!string.IsNullOrEmpty(ctx?.ChannelId))
            sourceContext["channel_id"] = ctx.ChannelId;
        if (!string.IsNullOrEmpty(ctx?.TeamId))
            sourceContext["team_id"] = ctx.TeamId;
        if (!string.IsNullOrEmpty(ctx?.EnterpriseId))
            sourceContext["enterprise_id"] = ctx.EnterpriseId;

        return new PlatformAssistantThreadEvent
        {
            Kind = AssistantThreadEventKind.ContextChanged,
            UserId = thread.UserId ?? "unknown",
            ChannelId = thread.ChannelId ?? string.Empty,
            ThreadId = thread.ThreadTs ?? string.Empty,
            SourceContext = sourceContext.Count > 0 ? sourceContext : null,
            PlatformContext = slackEvent
        };
    }
}
