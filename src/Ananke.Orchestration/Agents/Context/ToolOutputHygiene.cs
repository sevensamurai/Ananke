using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents.Context;

/// <summary>
/// The two Work-tier operations on tool results: bound a new one as it arrives, and reclaim an
/// old one that is still being carried.
/// </summary>
/// <remarks>
/// Both are pure string work over the message list. Neither mutates what it is given: pruning
/// returns a new list, so the accumulated history a workflow persists is never the reduced form.
/// The reduced form is what gets <em>sent</em>, which is the only place the size mattered.
/// </remarks>
internal static class ToolOutputHygiene
{
    /// <summary>Prefix identifying a footer this type wrote, so pruning can preserve one.</summary>
    private const string FooterPrefix = "[tool output ";

    /// <summary>
    /// Bounds one tool result. Returns the value to carry inline plus a record, or the original
    /// value and <see langword="null"/> when it was already within the cap.
    /// </summary>
    internal static async Task<(string Value, ToolOutputCapRecord? Record)> CapAsync(
        ToolOutputPolicy policy,
        string toolName,
        string value,
        CancellationToken ct)
    {
        if (value.Length <= policy.MaxResultChars)
            return (value, null);

        SpilledToolOutput? spilled = null;
        if (policy.SpillStore is not null)
            spilled = await policy.SpillStore.SaveAsync(toolName, value, ct).ConfigureAwait(false);

        var preview = value[..Math.Min(policy.PreviewChars, value.Length)];
        var elided = value.Length - preview.Length;

        var footer = spilled is null
            ? $"{FooterPrefix}truncated: {elided:N0} characters elided]"
            : $"{FooterPrefix}spilled: {elided:N0} of {spilled.Bytes:N0} bytes held at " +
              $"{spilled.Locator}. {spilled.RetrievalHint}]";

        var capped = preview.Length == 0 ? footer : preview + "\n" + footer;

        return (capped, new ToolOutputCapRecord
        {
            ToolName = toolName,
            CharsBefore = value.Length,
            CharsAfter = capped.Length,
            Spilled = spilled
        });
    }

    /// <summary>
    /// Reduces tool results that are neither recent nor already small. Returns
    /// <see langword="null"/> when nothing was worth reducing, so the caller can keep using the
    /// list it already had.
    /// </summary>
    internal static (IReadOnlyList<AgentMessage> Messages, int ReplacedCount, int CharsBefore, int CharsAfter)?
        Prune(IReadOnlyList<AgentMessage> messages, ToolOutputPolicy policy)
    {
        // Walk backwards so "recent" means recent in the transcript, and so the exemption is
        // counted over tool results rather than over messages of any kind.
        var seen = 0;
        var targets = new List<int>();
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i] is not { Role: AgentRole.Tool, Content: { } content })
                continue;

            if (++seen <= policy.KeepRecentResults)
                continue;

            if (content.Length > policy.PrunedResultChars)
                targets.Add(i);
        }

        if (targets.Count == 0)
            return null;

        var pruned = new List<AgentMessage>(messages);
        var before = 0;
        var after = 0;

        foreach (var i in targets)
        {
            var original = pruned[i].Content!;
            var replacement = Shorten(original, policy.PrunedResultChars);
            before += original.Length;
            after += replacement.Length;
            pruned[i] = pruned[i] with { Content = replacement };
        }

        return (pruned, targets.Count, before, after);
    }

    /// <summary>
    /// Keeps the head, and keeps any footer this type wrote — a spill footer carries the locator,
    /// which is the whole reason the output is still recoverable. Pruning it away would turn a
    /// bounded result into a lost one.
    /// </summary>
    private static string Shorten(string content, int keep)
    {
        var footer = ExtractFooter(content);
        var body = footer is null ? content : content[..(content.Length - footer.Length)].TrimEnd('\n');

        if (body.Length <= keep && footer is not null)
            return content;

        var head = body[..Math.Min(keep, body.Length)];
        var elided = content.Length - head.Length - (footer?.Length ?? 0);
        var prunedNote = $"{FooterPrefix}pruned: {elided:N0} characters elided]";

        return footer is null
            ? head + "\n" + prunedNote
            : head + "\n" + prunedNote + "\n" + footer;
    }

    private static string? ExtractFooter(string content)
    {
        if (!content.EndsWith(']'))
            return null;

        var start = content.LastIndexOf(FooterPrefix, StringComparison.Ordinal);
        return start < 0 ? null : content[start..];
    }
}
