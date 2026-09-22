using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents.Context;

/// <summary>
/// Whether specific text was still in the assembled prompt at turn N, and the turn it stopped being.
/// </summary>
/// <param name="Turn">Which assembly this is, counting from one.</param>
/// <param name="InputMessages">How many messages went in before compaction.</param>
/// <param name="Retained">Each tracked phrase, and whether the model would have seen it.</param>
public readonly record struct RetentionSample(
    int Turn,
    int InputMessages,
    IReadOnlyDictionary<string, bool> Retained)
{
    /// <summary>Share of tracked phrases that survived this assembly.</summary>
    public double RetentionRate =>
        Retained.Count == 0 ? 1 : Retained.Count(r => r.Value) / (double)Retained.Count;
}

/// <summary>
/// The shape of what a run remembers, measured across turns.
/// </summary>
/// <remarks>
/// <b>The shape is the claim, not the level.</b> Pinning is supposed to produce a
/// <em>discontinuity</em> — a long session either keeps its goal or loses it — so a pinned and an
/// unpinned arm that both decay smoothly means whatever improvement is being seen comes from
/// somewhere else. <see cref="FirstLossTurn"/> is where that shows up.
/// </remarks>
public sealed record RetentionCurve
{
    /// <summary>Every assembly, in order.</summary>
    public required IReadOnlyList<RetentionSample> Samples { get; init; }

    /// <summary>
    /// For each tracked phrase, the first turn it was missing from the prompt — or
    /// <see langword="null"/> if it never was.
    /// </summary>
    public required IReadOnlyDictionary<string, int?> FirstLossTurn { get; init; }

    /// <summary>Phrases that survived every assembly.</summary>
    public IReadOnlyList<string> Survived =>
        [.. FirstLossTurn.Where(p => p.Value is null).Select(p => p.Key)];

    /// <summary>Renders the curve as one line per turn, plus where each phrase was lost.</summary>
    public string ToReport()
    {
        var report = new System.Text.StringBuilder("Goal retention\n");

        foreach (var sample in Samples)
        {
            report.Append("  turn ").Append(sample.Turn.ToString().PadLeft(3))
                .Append("  msgs ").Append(sample.InputMessages.ToString().PadLeft(3))
                .Append("  retained ")
                .Append(string.Join(" ", sample.Retained.Select(r => r.Value ? "+" : "-")))
                .Append('\n');
        }

        foreach (var (phrase, turn) in FirstLossTurn)
        {
            report.Append("  \"").Append(Shorten(phrase)).Append("\"  ")
                .Append(turn is null ? "survived every turn" : $"lost at turn {turn}")
                .Append('\n');
        }

        return report.ToString();
    }

    private static string Shorten(string phrase) =>
        phrase.Length <= 40 ? phrase : phrase[..37] + "...";
}

/// <summary>
/// Wraps a context strategy and records whether given text survived each assembly.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measures the harness, not the model.</b> Whether a goal stated at turn one is still in the
/// prompt at turn fifty is decided entirely by what compaction kept — no model opinion is involved,
/// and none is needed to measure it. That makes this runnable against a scripted model, repeatably,
/// with no provider and no cost.
/// </para>
/// <para>
/// It does <b>not</b> answer whether a model that <em>was</em> shown the goal still acts on it. That
/// is a different question with a different answer, it needs a real model, and conflating the two is
/// how a retention result gets mistaken for a compliance result.
/// </para>
/// <para>
/// A decorator rather than an observer because the observation seam carries sizes, not text — and it
/// should stay that way. This sees the projection because it is in the assembly path already.
/// </para>
/// </remarks>
public sealed class ContextRetentionProbe : IContextStrategy
{
    private readonly IContextStrategy _inner;
    private readonly IReadOnlyList<string> _phrases;
    private readonly Lock _gate = new();
    private readonly List<RetentionSample> _samples = [];

    /// <summary>Wraps <paramref name="inner"/>, tracking each of <paramref name="phrases"/>.</summary>
    public ContextRetentionProbe(IContextStrategy inner, IEnumerable<string> phrases)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(phrases);

        _inner = inner;
        _phrases = [.. phrases];
    }

    /// <inheritdoc />
    public async Task<ContextProjection> ApplyAsync(
        IReadOnlyList<AgentMessage> messages,
        string? systemPrompt,
        ContextBudget budget,
        CancellationToken ct = default)
    {
        var projection = await _inner.ApplyAsync(messages, systemPrompt, budget, ct).ConfigureAwait(false);

        var assembled = Assemble(projection.Messages, systemPrompt);
        var retained = _phrases.ToDictionary(
            phrase => phrase,
            phrase => assembled.Contains(phrase, StringComparison.OrdinalIgnoreCase),
            StringComparer.Ordinal);

        lock (_gate)
        {
            _samples.Add(new RetentionSample(_samples.Count + 1, messages.Count, retained));
        }

        return projection;
    }

    /// <summary>The curve measured so far.</summary>
    public RetentionCurve Curve()
    {
        lock (_gate)
        {
            var firstLoss = _phrases.ToDictionary(
                phrase => phrase,
                phrase => (int?)_samples.FirstOrDefault(s => !s.Retained[phrase]).Turn switch
                {
                    0 => null,          // never missing: the default struct's Turn is zero
                    var turn => turn
                },
                StringComparer.Ordinal);

            return new RetentionCurve { Samples = [.. _samples], FirstLossTurn = firstLoss };
        }
    }

    /// <summary>
    /// The text the model would actually see. The system prompt is included because that is where a
    /// pinned contract lives — leaving it out would measure the opposite of what is intended.
    /// </summary>
    private static string Assemble(IReadOnlyList<AgentMessage> messages, string? systemPrompt)
    {
        var assembled = new System.Text.StringBuilder(systemPrompt ?? string.Empty);
        foreach (var message in messages)
        {
            if (message.Content is { } content)
                assembled.Append('\n').Append(content);
        }

        return assembled.ToString();
    }
}
