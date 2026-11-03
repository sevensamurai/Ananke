using System.Text;

namespace Ananke.Orchestration.Agents;

/// <summary>
/// Watches a model's reply for its end repeating one passage over and over: a model that has stopped
/// saying anything new and goes on until its output limit.
/// </summary>
/// <remarks>
/// <para>
/// <b>Repeated means the same characters, back to back.</b> The end of the reply has to be one
/// passage of at most <see cref="LongestPassage"/> characters at least <see cref="MinRepeats"/> times
/// in a row, over at least <see cref="MinRepeatedLength"/> characters. A table's rows, a list of
/// similar items or a line of dashes differ or stop well before that.
/// </para>
/// <para>
/// <b>Not retried.</b> Asked again, the same model is as likely to repeat itself again, so
/// <see cref="TerminalProviderError.Is"/> reads <see cref="RepeatedOutputException"/> as terminal and
/// the run ends rather than paying for the same reply twice.
/// </para>
/// </remarks>
public sealed class RepeatedOutput
{
    /// <summary>The longest passage looked for.</summary>
    public const int LongestPassage = 2_000;

    /// <summary>How many times in a row a passage has to appear.</summary>
    public const int MinRepeats = 3;

    /// <summary>How long the repeated stretch has to be, whatever the passage's length.</summary>
    public const int MinRepeatedLength = 1_000;

    // A stream is checked each time this much more has arrived, not on every piece.
    private const int CheckEvery = 256;

    // The end of the reply that is searched: room for the longest passage, repeated.
    private const int Searched = LongestPassage * (MinRepeats + 1);

    // A passage is found from the places its last characters appear earlier.
    private const int Probe = 32;

    private readonly StringBuilder _end = new();
    private int _sinceChecked;
    private int _length;

    /// <summary>
    /// Fed each piece of a streamed reply as it arrives; throws <see cref="RepeatedOutputException"/>
    /// once the reply so far ends repeating one passage.
    /// </summary>
    public void Add(string? delta)
    {
        if (string.IsNullOrEmpty(delta))
            return;

        _end.Append(delta);
        _length += delta.Length;
        _sinceChecked += delta.Length;

        if (_end.Length > 2 * Searched)
            _end.Remove(0, _end.Length - Searched);

        if (_sinceChecked < CheckEvery)
            return;

        _sinceChecked = 0;

        if (Find(_end.ToString()) is { } passage)
            throw Repeating(passage, _length);
    }

    /// <summary>Throws <see cref="RepeatedOutputException"/> when a whole reply ends repeating one passage.</summary>
    public static void ThrowIfRepeating(string? text)
    {
        if (text is not null && Find(text) is { } passage)
            throw Repeating(passage, text.Length);
    }

    /// <summary>The passage the end of <paramref name="text"/> repeats, or <see langword="null"/>.</summary>
    private static string? Find(string text)
    {
        var end = text.AsSpan(Math.Max(0, text.Length - Searched));

        if (end.Length < MinRepeatedLength)
            return null;

        var probe = end[^Probe..];
        var before = end.Length - 1;

        // Nearest first, so the shortest passage is the one found.
        while (end[..before].LastIndexOf(probe) is var at and >= 0)
        {
            var passage = end.Length - Probe - at;

            if (passage > LongestPassage)
                return null;

            var repeated = passage;
            for (var i = end.Length - 1; i - passage >= 0 && end[i] == end[i - passage]; i--)
                repeated++;

            // Reported as it first appears, where it reads from its own start.
            if (repeated >= Math.Max(MinRepeats * passage, MinRepeatedLength))
                return end.Slice(end.Length - repeated, passage).ToString();

            before = at + Probe - 1;
        }

        return null;
    }

    private static RepeatedOutputException Repeating(string passage, int length) =>
        new($"The reply repeats one passage of {passage.Length} characters over and over, "
            + $"by character {length}: \"{(passage.Length > 160 ? passage[..160] + "…" : passage)}\"");
}

/// <summary>
/// A model that repeats itself: a reply that repeats one passage over and over, or rounds of tool calls
/// that only repeat calls already answered. Not retried: it ends the run.
/// </summary>
public sealed class RepeatedOutputException(string message) : Exception(message);
