using System.Text;

namespace Ananke.Design;

/// <summary>
/// Writes a <see cref="PlanManifest"/> back out as the YAML subset
/// <see cref="PlanManifest.Parse"/> reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>The seam only worked in the read direction.</b> A plan could be loaded and run, and a plan
/// produced by anything other than a person typing it existed only as an in-memory tree — nothing
/// could hand it on, persist it, diff it, or put it in front of somebody before the work it
/// describes is spent. That last one is the cheapest review available and it needs a file.
/// </para>
/// <para>
/// <b>The round trip is by value, not by text.</b> What comes back parses to an equal manifest; it
/// is not the bytes that went in. Comments and blank lines are stripped before parsing and cannot
/// be restored, and the parser trims every value it reads — so leading and trailing whitespace does
/// not survive a round trip either, whoever wrote it.
/// </para>
/// <para>
/// <b>Some values cannot be written at all</b>, and each throws here rather than producing a file
/// that fails to parse later, or worse, one that parses to something else. The parser drops every
/// blank line and every line whose first character is <c>#</c> before it reads anything, without an
/// error, so a value holding either would come back a different string: refused for a blank line
/// anywhere in a value and for a line starting with <c>#</c> in a literal block. A wrapped line is
/// never allowed to start with <c>#</c> — the word stays where it is and the line runs long. Also
/// refused: a goal that is blank, a child node with no id, a blank entry in a list, and a plan or
/// node name that is blank or spans lines.
/// </para>
/// </remarks>
public static class PlanManifestExporter
{
    /// <summary>
    /// How wide a line may get before a single-line value is folded over several.
    /// </summary>
    /// <remarks>
    /// Readability only: a folded value and an inline one parse to the same string. The point of
    /// writing a plan to a file is that somebody reads it, and a goal is usually a sentence.
    /// </remarks>
    private const int FoldWidth = 88;

    /// <summary>Writes <paramref name="manifest"/> as a plan manifest.</summary>
    /// <exception cref="InvalidOperationException">
    /// It holds something the manifest format cannot express — see the remarks on
    /// <see cref="PlanManifestExporter"/>.
    /// </exception>
    public static string ToYaml(this PlanManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        RequireOneLine(manifest.Plan, "plan");

        var sb = new StringBuilder();
        sb.AppendLine($"plan: {manifest.Plan.Trim()}");
        sb.AppendLine();
        sb.AppendLine("root:");

        AppendNodeBody(sb, manifest.Root, indent: 2, at: "root");

        if (manifest.Revisions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("revisions:");

            foreach (var revision in manifest.Revisions)
            {
                RequireOneLine(revision.Node, "revision node");

                // The dash carries the item's first key, and the rest align two columns further in —
                // which is the one shape ParseRevisions accepts.
                AppendValue(sb, "node", revision.Node, indent: 2, dash: true, at: revision.Node);
                AppendValue(sb, "reason", revision.Reason, indent: 4, at: revision.Node);

                AppendNodeBody(
                    sb,
                    new PlanNodeManifest
                    {
                        Goal = revision.Goal,
                        Criteria = revision.Criteria,
                        Quality = revision.Quality,
                        Constraints = revision.Constraints,
                        Children = revision.Children
                    },
                    indent: 4,
                    at: revision.Node);
            }
        }

        return sb.ToString();
    }

    /// <summary>A node's keys, without the <c>id:</c> that introduces it.</summary>
    private static void AppendNodeBody(StringBuilder sb, PlanNodeManifest node, int indent, string at)
    {
        if (string.IsNullOrWhiteSpace(node.Goal))
            throw new InvalidOperationException(
                $"'{at}' has no goal, and a manifest written without one cannot be read back: "
                + "the parser refuses a node that declares none.");

        AppendValue(sb, "goal", node.Goal, indent, at: at);
        AppendScalars(sb, "criteria", node.Criteria, indent, at);
        AppendScalars(sb, "quality", node.Quality, indent, at);
        AppendScalars(sb, "constraints", node.Constraints, indent, at);

        if (node.Children.Count == 0)
            return;

        Pad(sb, indent).AppendLine("children:");

        foreach (var child in node.Children)
        {
            var id = child.Id;

            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException(
                    $"A child of '{at}' has no id. Every node below the root is named — the plan "
                    + "refers to it by that name, and one written without it cannot be read back.");

            AppendValue(sb, "id", id, indent + 2, dash: true, at: id);
            AppendNodeBody(sb, child, indent + 4, at: id);
        }
    }

    /// <summary>
    /// An omitted key rather than an empty sequence. The parser accepts <c>criteria:</c> with nothing
    /// under it, but every plan written by hand leaves the key out, and a reader should not have to
    /// wonder whether an empty list meant something.
    /// </summary>
    private static void AppendScalars(
        StringBuilder sb, string key, IReadOnlyList<string> values, int indent, string at)
    {
        if (values.Count == 0)
            return;

        Pad(sb, indent).Append(key).AppendLine(":");

        foreach (var value in values)
        {
            // Written as "- ", which the parser trims to "-" and no longer reads as a sequence item.
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(
                    $"'{at}' has a blank {key} entry, which the manifest format cannot write: an item "
                    + "with nothing in it is not read back as an item at all.");

            // A sequence item has no block form: ParseScalars reads the rest of the line and stops.
            if (value.Contains('\n'))
                throw new InvalidOperationException(
                    $"'{at}' has a {key} entry spanning more than one line, which the manifest format "
                    + $"has no way to write: \"{value}\".");

            Pad(sb, indent + 2).Append("- ").AppendLine(value.Trim());
        }
    }

    /// <summary>
    /// One <c>key: value</c>, inline when it fits on a line and as a block when it does not.
    /// </summary>
    /// <remarks>
    /// <c>dash</c> says this key introduces a sequence item and so sits on the dash line, which also
    /// moves everything belonging to it two columns further in.
    /// </remarks>
    private static void AppendValue(
        StringBuilder sb, string key, string value, int indent, string at, bool dash = false)
    {
        var lead = dash ? "- " : string.Empty;

        // A sequence item's own indent is rewritten to just past its dash before its keys are read,
        // so anything belonging to this key sits two columns further in than it otherwise would.
        var body = dash ? indent + 4 : indent + 2;

        // Not a style choice: ParseNode reads a bare "|" or ">" as the marker that a block follows,
        // so a value that is one of them has to be written as a block holding that text.
        if (value is "|" or ">")
        {
            Pad(sb, indent).Append(lead).Append(key).AppendLine(": |");
            Pad(sb, body).AppendLine(value);
            return;
        }

        if (value.Contains('\n'))
        {
            var lines = value.Split('\n');

            if (lines.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException(
                    $"'{at}' has a {key} holding a blank line. The parser strips blank lines before "
                    + "reading, so this would come back as a different string than it went in as.");

            // A literal block keeps its line breaks, so there is no wrapping to move a '#' off the
            // start of its line, and the parser drops such a line as a comment without saying so.
            if (lines.Any(line => line.TrimStart().StartsWith('#')))
                throw new InvalidOperationException(
                    $"'{at}' has a {key} with a line that starts with '#'. The parser reads that as a "
                    + "comment and drops the line, so it would come back a different string.");

            Pad(sb, indent).Append(lead).Append(key).AppendLine(": |");

            foreach (var line in lines)
                Pad(sb, body).AppendLine(line.Trim());

            return;
        }

        var trimmed = value.Trim();

        if (Foldable(trimmed, indent + lead.Length + key.Length + 2))
        {
            Pad(sb, indent).Append(lead).Append(key).AppendLine(": >");

            foreach (var line in Fold(trimmed, body))
                Pad(sb, body).AppendLine(line);

            return;
        }

        Pad(sb, indent).Append(lead).Append(key).Append(": ").AppendLine(trimmed);
    }

    /// <summary>
    /// Whether <paramref name="value"/> is worth folding, and safe to.
    /// </summary>
    /// <remarks>
    /// <b>Safe is the harder half.</b> A folded block is rejoined with one space per line break, so
    /// a value holding two spaces in a row would come back with one — folding is refused for it and
    /// the long line is written instead. So is a value that starts with <c>#</c>, whose first folded
    /// line would begin with it and be dropped as a comment. Correctness first; the line is still
    /// readable, just wide.
    /// </remarks>
    private static bool Foldable(string value, int used) =>
        used + value.Length > FoldWidth
        && !value.Contains("  ", StringComparison.Ordinal)
        && !value.StartsWith('#')
        && value.Contains(' ');

    /// <summary>Breaks <paramref name="value"/> on spaces, so no line runs past the fold width.</summary>
    private static IEnumerable<string> Fold(string value, int indent)
    {
        var line = new StringBuilder();

        foreach (var word in value.Split(' '))
        {
            // A word starting with '#' is never the first on a line: the parser would drop the whole
            // line as a comment. It stays where it is and the line runs over the width instead.
            if (line.Length > 0
                && !word.StartsWith('#')
                && indent + line.Length + 1 + word.Length > FoldWidth)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0)
                line.Append(' ');

            line.Append(word);
        }

        if (line.Length > 0)
            yield return line.ToString();
    }

    private static StringBuilder Pad(StringBuilder sb, int indent) => sb.Append(' ', indent);

    /// <summary>A name the manifest reads as the rest of its own line.</summary>
    private static void RequireOneLine(string value, string what)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\n'))
            throw new InvalidOperationException(
                $"The {what} is blank or spans lines: \"{value}\". The manifest reads it as the rest "
                + "of one line, so nothing else can be written.");
    }
}
