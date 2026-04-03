using System.Text.RegularExpressions;

namespace Ananke.Design.Dsl;

/// <summary>
/// Parses the workflow topology DSL into <see cref="ConnectionLine"/> entries.
/// </summary>
/// <remarks>
/// Supported syntax:
/// <list type="bullet">
///   <item><c>a -&gt; b</c> — direct connection</item>
///   <item><c>a -&gt; End</c> — terminal connection</item>
///   <item><c>a -&gt; fork(b, c)</c> — parallel fork (FailFast)</item>
///   <item><c>a -&gt; fork(b, c, mode: best-effort)</c> — parallel fork (BestEffort)</item>
///   <item><c>join(a, b) -&gt; c</c> — fan-in join</item>
///   <item><c>a -&gt; router(b, c, End)</c> — dynamic routing decision point</item>
///   <item><c>subflow(name)</c> — marks a job as a nested sub-workflow</item>
///   <item><c>interrupt(name)</c> — pauses execution before the named job</item>
/// </list>
/// </remarks>
internal static partial class WorkflowDslParser
{
    // a -> fork(b, c)  or  a -> fork(b, c, mode: best-effort)
    [GeneratedRegex(
        @"^(?<from>\w+)\s*->\s*fork\((?<args>[^)]+)\)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex ForkPattern();

    // join(a, b) -> c
    [GeneratedRegex(
        @"^join\((?<sources>[^)]+)\)\s*->\s*(?<target>\w+)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex JoinPattern();

    // a -> router(b, c, End)
    [GeneratedRegex(
        @"^(?<from>\w+)\s*->\s*router\((?<options>[^)]+)\)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex RouterPattern();

    // subflow(name)
    [GeneratedRegex(
        @"^subflow\((?<name>\w+)\)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex SubFlowPattern();

    // interrupt(name)
    [GeneratedRegex(
        @"^interrupt\((?<job>\w+)\)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex InterruptPattern();

    // a -> b  (simple direct, including End)
    [GeneratedRegex(
        @"^(?<from>\w+)\s*->\s*(?<to>\w+)$",
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

        match = SubFlowPattern().Match(line);
        if (match.Success)
            return new ConnectionLine.SubFlow(match.Groups["name"].Value);

        match = InterruptPattern().Match(line);
        if (match.Success)
            return new ConnectionLine.Interrupt(match.Groups["job"].Value);

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

    private static string[] SplitArgs(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
