using System.Text;

namespace Ananke.Orchestration.Planning;

/// <summary>
/// An acceptance criterion written as something that runs: a check's name and its arguments.
/// </summary>
/// <remarks>
/// <para>
/// <b>A criterion that is a sentence can only be decided by a program that already has the
/// sentence.</b> That is not an implementation shortcut, it is what the type forces — and it is why
/// a check ends up bound to a literal list of every criterion a plan can ever contain. The moment
/// anything other than the plan's author writes one, nothing can rule on it: same claim, different
/// words, silently abstained.
/// </para>
/// <para>
/// <b>So a criterion resolves by name and arguments.</b> <c>fits(day-3a)</c> is decidable no matter
/// who invented <c>day-3a</c>, because the check takes the step as an argument rather than having
/// been told the sentence in advance. What a person reads is rendered from the check's own
/// description; the invocation is the thing itself.
/// </para>
/// <para>
/// <b>It travels as text</b>, so a criterion still round-trips through a manifest, a checkpoint and a
/// model's reply without a schema anyone has to keep in step. The cost is that it can be malformed,
/// which is what plan admission is for.
/// </para>
/// </remarks>
public sealed record Criterion
{
    /// <summary>The check to run.</summary>
    public required string Name { get; init; }

    /// <summary>What it is being asked about, in order.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>
    /// Reads <c>name(arg, arg)</c>, or <see langword="null"/> when the text is not one.
    /// </summary>
    /// <remarks>
    /// <b>Prose is not an error here, it is an answer.</b> A plan may hold criteria nothing can run —
    /// they are marked as judged and counted, rather than refused — so failing to parse is a fact
    /// about the criterion, not a fault.
    /// </remarks>
    public static Criterion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.Trim();

        if (!trimmed.EndsWith(')') || trimmed.IndexOf('(') is var open && open <= 0)
            return null;

        var name = trimmed[..open].Trim();

        if (name.Length == 0 || !name.All(IsNameChar))
            return null;

        var inside = trimmed[(open + 1)..^1].Trim();

        if (inside.Contains('(') || inside.Contains(')'))
            return null;

        var arguments = inside.Length == 0
            ? []
            : inside.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        return new Criterion { Name = name, Arguments = arguments };
    }

    /// <summary>Whether <paramref name="text"/> is an invocation rather than prose.</summary>
    public static bool IsInvocation(string? text) => Parse(text) is not null;

    /// <summary>Renders it back to the text a contract carries.</summary>
    public override string ToString()
    {
        var text = new StringBuilder(Name).Append('(');

        for (var i = 0; i < Arguments.Count; i++)
        {
            if (i > 0)
                text.Append(", ");

            text.Append(Arguments[i]);
        }

        return text.Append(')').ToString();
    }

    /// <summary>The argument at <paramref name="index"/>, or <see langword="null"/> if there is none.</summary>
    public string? Argument(int index) =>
        index >= 0 && index < Arguments.Count ? Arguments[index] : null;

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' or '.';
}
