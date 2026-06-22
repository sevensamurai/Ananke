using System.Text.RegularExpressions;

namespace Ananke.Design.Dsl;

/// <summary>
/// Parses the workflow topology DSL into <see cref="ConnectionLine"/> entries.
/// </summary>
/// <remarks>
/// Supported syntax:
/// <list type="bullet">
///   <item><c>tool(name, tags: [a, b], description: "...")</c> — portable tool declaration</item>
///   <item><c>use(job, tool_a, tool_b, semantic: true)</c> — attach tools to a job</item>
///   <item><c>a -&gt; b</c> — direct connection</item>
///   <item><c>a -&gt; End</c> — terminal connection</item>
///   <item><c>a -&gt; fork(b, c)</c> — parallel fork (FailFast)</item>
///   <item><c>a -&gt; fork(b, c, mode: best-effort)</c> — parallel fork (BestEffort)</item>
///   <item><c>join(a, b) -&gt; c</c> — fan-in join</item>
///   <item><c>a -&gt; router(b, c, End)</c> — dynamic routing decision point</item>
///   <item><c>a -&gt; loop(target, exit: x)</c> — conditional back-edge loop</item>
///   <item><c>a -&gt; loop(target, exit: x, maxIterations: n)</c> — loop with an iteration cap (default 10)</item>
///   <item><c>subflow(name)</c> — marks a job as a nested sub-workflow</item>
///   <item><c>interrupt(name)</c> — pauses execution before the named job</item>
///   <item><c>ask(name)</c> — marks a job as a free-text, input-collecting turn</item>
/// </list>
/// </remarks>
internal static partial class WorkflowDslParser
{
    // Identifier pattern: word chars with optional hyphens (e.g. handle-request, fetch_a)
    // Anchored so hyphens don't conflict with the -> arrow operator.
    private const string Id = @"\w+(?:-\w+)*";

    // a -> fork(b, c)  or  a -> fork(b, c, mode: best-effort)
    [GeneratedRegex(
        @$"^(?<from>{Id})\s*->\s*fork\((?<args>[^)]+)\)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex ForkPattern();

    // join(a, b) -> c
    [GeneratedRegex(
        @$"^join\((?<sources>[^)]+)\)\s*->\s*(?<target>{Id})$",
        RegexOptions.IgnoreCase)]
    private static partial Regex JoinPattern();

    // a -> router(b, c, End)
    [GeneratedRegex(
        @$"^(?<from>{Id})\s*->\s*router\((?<options>[^)]+)\)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex RouterPattern();

    // a -> loop(target, exit: x)  or  a -> loop(target, exit: x, maxIterations: n)
    [GeneratedRegex(
        @$"^(?<from>{Id})\s*->\s*loop\((?<args>[^)]+)\)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex LoopPattern();

    // subflow(name)
    [GeneratedRegex(
        @$"^subflow\((?<name>{Id})\)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex SubFlowPattern();

    // interrupt(name)
    [GeneratedRegex(
        @$"^interrupt\((?<job>{Id})\)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex InterruptPattern();

    // ask(name)
    [GeneratedRegex(
        @$"^ask\((?<job>{Id})\)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex AskPattern();

    private static Regex ToolDirectivePattern() =>
        new(@"^tool\((?<args>.+)\)$", RegexOptions.IgnoreCase);

    private static Regex UseDirectivePattern() =>
        new(@"^use\((?<args>.+)\)$", RegexOptions.IgnoreCase);

    // a -> b  (simple direct, including End)
    [GeneratedRegex(
        @$"^(?<from>{Id})\s*->\s*(?<to>{Id})$",
        RegexOptions.IgnoreCase)]
    private static partial Regex DirectPattern();

    internal static List<ConnectionLine> Parse(IEnumerable<string> lines)
    {
        var results = new List<ConnectionLine>();

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            // Strip inline comments: "a -> b  # comment"
            var commentIndex = line.IndexOf('#');
            if (commentIndex > 0)
                line = line[..commentIndex].TrimEnd();

            var parsed = ParseLine(line);
            results.Add(parsed);
        }

        return results;
    }

    internal static List<ConnectionLine> Parse(string text) =>
        Parse(text.Split('\n'));

    private static ConnectionLine ParseLine(string line)
    {
        Match match;

        match = ForkPattern().Match(line);
        if (match.Success)
            return ParseFork(match, line);

        match = JoinPattern().Match(line);
        if (match.Success)
            return ParseJoin(match, line);

        match = RouterPattern().Match(line);
        if (match.Success)
            return ParseRouter(match, line);

        match = LoopPattern().Match(line);
        if (match.Success)
            return ParseLoop(match, line);

        match = SubFlowPattern().Match(line);
        if (match.Success)
            return new ConnectionLine.SubFlow(match.Groups["name"].Value);

        match = InterruptPattern().Match(line);
        if (match.Success)
            return new ConnectionLine.Interrupt(match.Groups["job"].Value);

        match = AskPattern().Match(line);
        if (match.Success)
            return new ConnectionLine.Ask(match.Groups["job"].Value);

        match = ToolDirectivePattern().Match(line);
        if (match.Success)
            return ParseTool(match, line);

        match = UseDirectivePattern().Match(line);
        if (match.Success)
            return ParseUse(match, line);

        match = DirectPattern().Match(line);
        if (match.Success)
            return new ConnectionLine.Direct(match.Groups["from"].Value, match.Groups["to"].Value);

        throw new FormatException($"Unrecognized DSL syntax: '{line}'");
    }

    private static ConnectionLine.Fork ParseFork(Match match, string line)
    {
        var from = match.Groups["from"].Value;
        var argsRaw = match.Groups["args"].Value;
        var parts = SplitArgs(argsRaw);

        string? mode = null;
        var targets = new List<string>();

        foreach (var part in parts)
        {
            if (part.StartsWith("mode:", StringComparison.OrdinalIgnoreCase))
            {
                mode = part["mode:".Length..].Trim();
            }
            else
            {
                targets.Add(part);
            }
        }

        if (targets.Count < 2)
            throw new FormatException($"Fork requires at least two targets: '{line}'");

        return new ConnectionLine.Fork(from, [.. targets], mode);
    }

    private static ConnectionLine.Join ParseJoin(Match match, string line)
    {
        var sources = SplitArgs(match.Groups["sources"].Value);
        var target = match.Groups["target"].Value;

        if (sources.Length < 2)
            throw new FormatException($"Join requires at least two sources: '{line}'");

        return new ConnectionLine.Join(sources, target);
    }

    private static ConnectionLine.Router ParseRouter(Match match, string line)
    {
        var from = match.Groups["from"].Value;
        var options = SplitArgs(match.Groups["options"].Value);

        if (options.Length < 2)
            throw new FormatException($"Router requires at least two options: '{line}'");

        return new ConnectionLine.Router(from, options);
    }

    private static ConnectionLine.Loop ParseLoop(Match match, string line)
    {
        var from = match.Groups["from"].Value;
        var parts = SplitArgs(match.Groups["args"].Value);

        if (parts.Length == 0)
            throw new FormatException($"Loop requires a target: '{line}'");

        var loopTarget = parts[0];
        string? exitTarget = null;
        int? maxIterations = null;

        foreach (var part in parts.Skip(1))
        {
            if (part.StartsWith("exit:", StringComparison.OrdinalIgnoreCase))
            {
                exitTarget = part["exit:".Length..].Trim();
            }
            else if (part.StartsWith("maxIterations:", StringComparison.OrdinalIgnoreCase))
            {
                maxIterations = int.Parse(part["maxIterations:".Length..].Trim());
            }
        }

        if (exitTarget is null)
            throw new FormatException($"Loop requires an 'exit:' target: '{line}'");

        return new ConnectionLine.Loop(from, loopTarget, exitTarget, maxIterations);
    }

    private static ConnectionLine.Tool ParseTool(Match match, string line)
    {
        var args = SplitDslArgs(match.Groups["args"].Value);
        if (args.Count == 0)
            throw new FormatException($"Tool directive requires a name: '{line}'");

        var name = args[0];
        string description = string.Empty;
        string[] tags = [];

        foreach (var part in args.Skip(1))
        {
            if (part.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
            {
                description = Unquote(part["description:".Length..].Trim());
            }
            else if (part.StartsWith("tags:", StringComparison.OrdinalIgnoreCase))
            {
                tags = ParseDslList(part["tags:".Length..].Trim());
            }
        }

        return new ConnectionLine.Tool(name, description, tags);
    }

    private static ConnectionLine.Use ParseUse(Match match, string line)
    {
        var args = SplitDslArgs(match.Groups["args"].Value);
        if (args.Count < 2)
            throw new FormatException($"Use directive requires a job name and at least one tool: '{line}'");

        var jobName = args[0];
        var semantic = false;
        var tools = new List<string>();

        foreach (var part in args.Skip(1))
        {
            if (part.StartsWith("semantic:", StringComparison.OrdinalIgnoreCase))
            {
                semantic = bool.Parse(part["semantic:".Length..].Trim());
            }
            else
            {
                tools.Add(part);
            }
        }

        if (tools.Count == 0)
            throw new FormatException($"Use directive requires at least one tool: '{line}'");

        return new ConnectionLine.Use(jobName, [.. tools], semantic);
    }

    private static string[] SplitArgs(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static List<string> SplitDslArgs(string value)
    {
        var results = new List<string>();
        var current = new System.Text.StringBuilder();
        var bracketDepth = 0;
        var inQuotes = false;

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"':
                    inQuotes = !inQuotes;
                    current.Append(ch);
                    break;
                case '[' when !inQuotes:
                    bracketDepth++;
                    current.Append(ch);
                    break;
                case ']' when !inQuotes:
                    bracketDepth--;
                    current.Append(ch);
                    break;
                case ',' when !inQuotes && bracketDepth == 0:
                    results.Add(current.ToString().Trim());
                    current.Clear();
                    break;
                default:
                    current.Append(ch);
                    break;
            }
        }

        if (current.Length > 0)
            results.Add(current.ToString().Trim());

        return results;
    }

    private static string[] ParseDslList(string value)
    {
        if (!value.StartsWith('[') || !value.EndsWith(']'))
            return [];

        var inner = value[1..^1].Trim();
        return inner.Length == 0
            ? []
            : inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"')
            ? value[1..^1]
            : value;
}
