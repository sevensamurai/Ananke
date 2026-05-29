using System.Collections.Concurrent;
using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Memory;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Workflows;
using Ananke.Organics.Division.Approval;
using Ananke.Organics.Division.Review;
using Ananke.Platforms;
using Ananke.Platforms.Sessions;
using Ananke.Platforms.Slack;
using Ananke.Roles.Roles;
using Microsoft.Extensions.Logging;

namespace MiniAgencyDemo;

internal sealed class MiniAgencyMessageHandler(
    IStreamingAgentModel streamingModel,
    IAgentModel reviewModel,
    IConversationMemory conversationMemory,
    ToolKit toolKit,
    IAgentRoleCatalog roleCatalog,
    InMemoryBudgetMeter budgetMeter,
    MiniAgencyBudgetMetrics budgetMetrics,
    MiniAgencyOptions options,
    ILogger<MiniAgencyMessageHandler> logger) : IPlatformMessageHandler
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<WorkReviewDecision>> _pendingReviews =
        new(StringComparer.Ordinal);

    private readonly IReadOnlyDictionary<string, string> _systemPrompts = roleCatalog.All.ToDictionary(
        role => role.Name,
        role => File.ReadAllText(role.SystemPromptPath),
        StringComparer.OrdinalIgnoreCase);

    public async Task HandleAsync(PlatformMessage message, IPlatformResponseSink sink, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(sink);

        var threadId = message.ThreadId ?? message.PlatformMessageId;

        if (budgetMeter.IsOverCap(options.WorkflowName, options.BudgetCap))
        {
            await SendBudgetExceededAsync(message, sink, ct).ConfigureAwait(false);
            return;
        }

        if (!roleCatalog.TryGet("drafter", out var drafterRole) || !roleCatalog.TryGet("reviewer", out var reviewerRole))
            throw new InvalidOperationException("MiniAgencyDemo requires roles named 'drafter' and 'reviewer'.");

        await sink.SendTypingAsync(message.ChannelId, threadId, ct).ConfigureAwait(false);

        var bridge = new StreamingMessageBridge(
            sink,
            message.ChannelId,
            threadId,
            new StreamingBridgeOptions
            {
                DebounceInterval = TimeSpan.FromMilliseconds(500),
                ThinkingPlaceholder = "Drafting response..."
            },
            logger);

        WorkflowExecution<StreamingChatState> draftExecution;
        try
        {
            draftExecution = await StreamingChatWorkflow.Create("mini-agency-drafter", streamingModel)
                .WithSystemPrompt(GetPrompt(drafterRole.Name))
                .WithTools(toolKit)
                .WithMemory(conversationMemory)
                .WithMaxToolRounds(drafterRole.MaxToolRounds)
                .OnTextDelta(delta => bridge.AppendAsync(delta, ct))
                .RunAsync(SessionKeyBuilder.Build(message, "slack"), [message.Message], ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Mini-agency drafting failed for {ChannelId}:{ThreadId}", message.ChannelId, threadId);
            await sink.SendMessageAsync(message.ChannelId, threadId,
                "The drafting pass failed before review could run. Check the demo logs and local model endpoint.",
                ct).ConfigureAwait(false);
            return;
        }
        finally
        {
            await bridge.FinalizeAsync(ct).ConfigureAwait(false);
        }

        budgetMeter.Record(
            options.WorkflowName,
            draftExecution.CumulativeUsage.InputTokens,
            draftExecution.CumulativeUsage.OutputTokens,
            estimatedUsd: 0m);
        budgetMetrics.Record(options.WorkflowName, draftExecution.CumulativeUsage);

        var draftText = ExtractDraftText(draftExecution);
        if (string.IsNullOrWhiteSpace(draftText))
        {
            await sink.SendMessageAsync(message.ChannelId, threadId,
                "The drafting pass completed without any assistant text to review.",
                ct).ConfigureAwait(false);
            return;
        }

        var reviewRequestMessageId = await sink.SendMessageAsync(
            message.ChannelId,
            threadId,
            $"Review window is open for {options.HumanReviewTimeout.TotalSeconds:0} seconds. React to this message with :white_check_mark: to count as human approval or :x: to block.",
            ct).ConfigureAwait(false);

        var workItem = new WorkItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = $"Slack response for {(message.UserName ?? message.UserId)}",
            Kind = WorkItemKind.Other,
            Payload = BuildReviewPayload(message, draftText)
        };

        var llmReviewTask = new FixedReviewerIdGate(
            "llm",
            new LlmWorkReviewGate(reviewModel, GetPrompt(reviewerRole.Name)))
            .ReviewAsync(workItem, ct);

        var humanReviewTask = new FixedReviewerIdGate(
            "any-channel-user",
            new CallbackWorkReviewGate((_, reviewCt) => AwaitReactionReviewAsync(
                message.ChannelId,
                reviewRequestMessageId,
                reviewCt)))
            .ReviewAsync(workItem, ct);

        var llmDecision = await llmReviewTask.ConfigureAwait(false);
        var humanDecision = await humanReviewTask.ConfigureAwait(false);

        var finalDecision = await new QuorumWorkReviewGate(
            [
                new FixedDecisionGate(llmDecision),
                new FixedDecisionGate(humanDecision)
            ],
            WorkReviewQuorum.RequireAllOf("llm").AndAnyOf("llm", "any-channel-user"))
            .ReviewAsync(workItem, ct)
            .ConfigureAwait(false);

        await PublishReviewOutcomeAsync(
            message.ChannelId,
            threadId,
            reviewRequestMessageId,
            finalDecision,
            sink,
            ct).ConfigureAwait(false);
    }

    public Task OnReactionAsync(PlatformReactionEvent reaction, IPlatformResponseSink sink, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reaction);

        if (!reaction.Added || string.IsNullOrWhiteSpace(reaction.MessageTs))
            return Task.CompletedTask;

        if (!_pendingReviews.TryGetValue(BuildReviewKey(reaction.ChannelId, reaction.MessageTs), out var pending))
            return Task.CompletedTask;

        WorkReviewDecision? decision = reaction.Reaction switch
        {
            "white_check_mark" => WorkReviewDecision.Approve(
                $"Approved by {reaction.UserId} via Slack reaction.",
                "any-channel-user"),
            "x" => WorkReviewDecision.Reject(
                $"Blocked by {reaction.UserId} via Slack reaction.",
                "any-channel-user"),
            _ => null
        };

        if (decision is not null)
            pending?.TrySetResult(decision);

        return Task.CompletedTask;
    }

    private async Task SendBudgetExceededAsync(PlatformMessage message, IPlatformResponseSink sink, CancellationToken ct)
    {
        var spend = budgetMeter.GetCurrentSpend(options.WorkflowName);
        var text = $"Budget cap reached for '{options.WorkflowName}'. Current rolling usage: {spend.TokensIn + spend.TokensOut} tokens in the last {options.BudgetWindow.TotalMinutes:0} minutes.";

        if (sink is ISlackResponseSink slackSink)
        {
            await slackSink.SendEphemeralAsync(message.ChannelId, message.UserId, text, ct: ct)
                .ConfigureAwait(false);
            return;
        }

        await sink.SendMessageAsync(message.ChannelId, message.ThreadId ?? message.PlatformMessageId, text, ct)
            .ConfigureAwait(false);
    }

    private async Task<WorkReviewDecision> AwaitReactionReviewAsync(
        string channelId,
        string messageId,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.HumanReviewTimeout);

        var completion = new TaskCompletionSource<WorkReviewDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        var key = BuildReviewKey(channelId, messageId);
        if (!_pendingReviews.TryAdd(key, completion))
        {
            return WorkReviewDecision.Reject(
                "A review gate is already waiting on this Slack message.",
                "any-channel-user");
        }

        using var registration = timeout.Token.Register(() => completion.TrySetResult(
            WorkReviewDecision.Reject(
                $"No human review reaction arrived within {options.HumanReviewTimeout.TotalSeconds:0} seconds.",
                "any-channel-user")));

        try
        {
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _pendingReviews.TryRemove(key, out _);
        }
    }

    private async Task PublishReviewOutcomeAsync(
        string channelId,
        string? threadId,
        string reviewRequestMessageId,
        WorkReviewDecision decision,
        IPlatformResponseSink sink,
        CancellationToken ct)
    {
        var summary = decision.Outcome switch
        {
            WorkReviewOutcome.Approved => $"Review approved. {decision.Comment}",
            WorkReviewOutcome.Revised => $"Review requested revision. {decision.Comment}",
            _ => $"Review rejected. {decision.Comment}"
        };

        var emoji = decision.Outcome switch
        {
            WorkReviewOutcome.Approved => "white_check_mark",
            WorkReviewOutcome.Revised => "warning",
            _ => "x"
        };

        await sink.AddReactionAsync(channelId, reviewRequestMessageId, emoji, ct).ConfigureAwait(false);
        await sink.SendMessageAsync(channelId, threadId, summary, ct).ConfigureAwait(false);
    }

    private string GetPrompt(string roleName) =>
        _systemPrompts.TryGetValue(roleName, out var prompt)
            ? prompt
            : throw new InvalidOperationException($"No system prompt was loaded for role '{roleName}'.");

    private static string BuildReviewPayload(PlatformMessage message, string draftText) => $"""
        Request from {(message.UserName ?? message.UserId)}:
        {message.Message.Content}

        Draft response:
        {draftText}
        """;

    private static string ExtractDraftText(WorkflowExecution<StreamingChatState> execution)
    {
        var assistant = execution.State.Messages.LastOrDefault(
            msg => msg.Role == Ananke.Abstractions.Agents.AgentRole.Assistant)?.Content;
        return string.IsNullOrWhiteSpace(assistant)
            ? execution.State.FullText
            : assistant;
    }

    private static string BuildReviewKey(string channelId, string messageId) => $"{channelId}:{messageId}";

    private sealed class FixedReviewerIdGate(string reviewerId, IWorkReviewGate inner) : IWorkReviewGate
    {
        public async Task<WorkReviewDecision> ReviewAsync(WorkItem item, CancellationToken ct = default)
        {
            var decision = await inner.ReviewAsync(item, ct).ConfigureAwait(false);
            return decision with { ReviewerId = reviewerId };
        }
    }

    private sealed class FixedDecisionGate(WorkReviewDecision decision) : IWorkReviewGate
    {
        public Task<WorkReviewDecision> ReviewAsync(WorkItem item, CancellationToken ct = default) =>
            Task.FromResult(decision);
    }
}
