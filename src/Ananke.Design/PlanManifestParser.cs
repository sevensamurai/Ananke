namespace Ananke.Design;

/// <summary>
/// Reads the plan manifest's YAML subset.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written for the same reason <see cref="WorkflowManifest"/>'s parser is: the schema is fixed
/// and small, and a general YAML library would add a dependency to every consumer of the package for
/// features this format deliberately does not have. It is <b>not</b> a general-purpose YAML parser.
/// </para>
/// <para>
/// <b>Supported:</b> mappings nested to any depth by two-space indent; sequences of scalars
/// (<c>- text</c>); sequences of mappings (<c>- key: value</c> with the item's remaining keys aligned
/// under it); literal (<c>|</c>) and folded (<c>&gt;</c>) block scalars; <c>#</c> comments and blank
/// lines.
/// </para>
/// <para>
/// <b>Not supported, by design:</b> anchors and aliases, flow collections, quoting, tags, multiple
/// documents, includes — and anything that computes rather than states. A plan describes work; a
/// plan that computes its own shape is a program, and the point of writing it down was to have
/// something a person can read.
/// </para>
/// <para>
/// <b>An unknown key is an error, not a silence.</b> A misspelled <c>critera:</c> that parsed to
/// nothing would produce a node with no acceptance criteria — which reads exactly like a node that
/// legitimately has none, and would be discovered when the plan passed without checking anything.
/// </para>
/// </remarks>
internal static class PlanManifestParser
{
    private readonly record struct Line(int Indent, string Text, int Number);

    public static PlanManifest Parse(string[] lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var source = Read(lines);
        var at = 0;

        string? plan = null;
        PlanNodeManifest? root = null;
        List<PlanRevisionManifest> revisions = [];

        while (at < source.Count)
        {
            var line = source[at];
            if (line.Indent != 0)
                throw Fail(line, "expected a top-level key");

            var (key, value) = Split(line);

            switch (key)
            {
                case "plan":
                    plan = Require(value, line, "plan");
                    at++;
                    break;

                case "root":
                    at++;
                    root = ParseNode(source, ref at, 2, id: null);
                    break;

                case "revisions":
                    at++;
                    revisions = ParseRevisions(source, ref at, 2);
                    break;

                default:
                    throw Fail(line, $"unknown key '{key}'");
            }
        }

        if (plan is null)
            throw new InvalidOperationException("A plan manifest needs a 'plan:' identifier.");
        if (root is null)
            throw new InvalidOperationException($"Plan '{plan}' declares no 'root:'.");

        return new PlanManifest { Plan = plan, Root = root, Revisions = revisions };
    }

    /// <summary>Strips comments and blanks, and records each line's indent.</summary>
    private static List<Line> Read(string[] lines)
    {
        var read = new List<Line>();

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var trimmed = raw.TrimStart();

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            read.Add(new Line(raw.Length - trimmed.Length, trimmed.TrimEnd(), i + 1));
        }

        return read;
    }

    private static PlanNodeManifest ParseNode(List<Line> source, ref int at, int indent, string? id)
    {
        string? goal = null;
        IReadOnlyList<string> criteria = [];
        IReadOnlyList<string> quality = [];
        IReadOnlyList<string> constraints = [];
        IReadOnlyList<PlanNodeManifest> children = [];

        while (at < source.Count && source[at].Indent >= indent)
        {
            var line = source[at];
            if (line.Indent != indent)
                throw Fail(line, "unexpected indentation");

            var (key, value) = Split(line);

            switch (key)
            {
                case "id":
                    id = Require(value, line, "id");
                    at++;
                    break;

                case "goal":
                    at++;
                    goal = value is "|" or ">"
                        ? ParseBlock(source, ref at, indent + 2, folded: value == ">")
                        : Require(value, line, "goal");
                    break;

                case "criteria":
                    at++;
                    criteria = ParseScalars(source, ref at, indent + 2);
                    break;

                case "quality":
                    at++;
                    quality = ParseScalars(source, ref at, indent + 2);
                    break;

                case "constraints":
                    at++;
                    constraints = ParseScalars(source, ref at, indent + 2);
                    break;

                case "children":
                    at++;
                    children = ParseNodes(source, ref at, indent + 2);
                    break;

                default:
                    throw Fail(line, $"unknown key '{key}'");
            }
        }

        if (goal is null)
            throw new InvalidOperationException(
                $"Node '{id ?? "root"}' declares no 'goal:'. A contract with no goal is not one.");

        return new PlanNodeManifest
        {
            Id = id,
            Goal = goal,
            Criteria = criteria,
            Quality = quality,
            Constraints = constraints,
            Children = children
        };
    }

    private static List<PlanNodeManifest> ParseNodes(List<Line> source, ref int at, int indent)
    {
        var nodes = new List<PlanNodeManifest>();

        while (at < source.Count && source[at].Indent == indent && source[at].Text.StartsWith("- "))
        {
            // The item's first key sits on the dash line; the rest align two columns further in.
            source[at] = source[at] with { Indent = indent + 2, Text = source[at].Text[2..] };
            nodes.Add(ParseNode(source, ref at, indent + 2, id: null));
        }

        return nodes;
    }

    private static List<PlanRevisionManifest> ParseRevisions(List<Line> source, ref int at, int indent)
    {
        var revisions = new List<PlanRevisionManifest>();

        while (at < source.Count && source[at].Indent == indent && source[at].Text.StartsWith("- "))
        {
            source[at] = source[at] with { Indent = indent + 2, Text = source[at].Text[2..] };
            revisions.Add(ParseRevision(source, ref at, indent + 2));
        }

        return revisions;
    }

    private static PlanRevisionManifest ParseRevision(List<Line> source, ref int at, int indent)
    {
        string? node = null;
        string? reason = null;

        // A revision is a node's replacement contract plus the two things a contract cannot say:
        // which node it replaces, and why. Those are read here and the rest is an ordinary node.
        var keys = new List<Line>();
        while (at < source.Count && source[at].Indent >= indent)
        {
            var line = source[at];

            if (line.Indent == indent)
            {
                var (key, value) = Split(line);

                if (key == "node")
                {
                    node = Require(value, line, "node");
                    at++;
                    continue;
                }

                if (key == "reason")
                {
                    at++;
                    reason = value is "|" or ">"
                        ? ParseBlock(source, ref at, indent + 2, folded: value != "|")
                        : Require(value, line, "reason");
                    continue;
                }
            }

            keys.Add(line);
            at++;
        }

        if (node is null)
            throw new InvalidOperationException("A revision needs the 'node:' it re-rules.");
        if (reason is null)
            throw new InvalidOperationException(
                $"The revision of '{node}' declares no 'reason:'. A change of plan with no stated "
                + "reason records that something changed and destroys why, which is the one thing "
                + "a lineage exists to keep.");

        var rest = 0;
        var contract = ParseNode(keys, ref rest, indent, id: node);

        if (rest < keys.Count)
            throw Fail(keys[rest], "unexpected content in revision");

        return new PlanRevisionManifest
        {
            Node = node,
            Reason = reason,
            Goal = contract.Goal,
            Criteria = contract.Criteria,
            Quality = contract.Quality,
            Constraints = contract.Constraints,
            Children = contract.Children
        };
    }

    private static List<string> ParseScalars(List<Line> source, ref int at, int indent)
    {
        var items = new List<string>();

        while (at < source.Count && source[at].Indent == indent && source[at].Text.StartsWith("- "))
        {
            items.Add(source[at].Text[2..].Trim());
            at++;
        }

        return items;
    }

    private static string ParseBlock(List<Line> source, ref int at, int indent, bool folded)
    {
        var parts = new List<string>();

        while (at < source.Count && source[at].Indent >= indent)
        {
            parts.Add(source[at].Text);
            at++;
        }

        return string.Join(folded ? " " : "\n", parts);
    }

    private static (string Key, string Value) Split(Line line)
    {
        var colon = line.Text.IndexOf(':');
        if (colon < 0)
            throw Fail(line, "expected 'key: value'");

        return (line.Text[..colon].Trim(), line.Text[(colon + 1)..].Trim());
    }

    private static string Require(string value, Line line, string key) =>
        value.Length > 0 ? value : throw Fail(line, $"'{key}' has no value");

    private static InvalidOperationException Fail(Line line, string what) =>
        new($"Plan manifest line {line.Number}: {what} — \"{line.Text}\".");
}
