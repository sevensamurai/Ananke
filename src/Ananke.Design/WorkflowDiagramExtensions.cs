using Ananke.Orchestration;
using Ananke.Orchestration.Routing;
using System.Text;

namespace Ananke.Design;

/// <summary>
/// Extension methods for exporting workflow graphs as
/// <see href="https://mermaid.js.org/">Mermaid</see> diagrams.
/// </summary>
public static class WorkflowDiagramExtensions
{
    /// <summary>
    /// Exports the validated workflow graph as a Mermaid flowchart string.
    /// </summary>
    public static string ToMermaid<TState>(this WorkflowDefinition<TState> definition)
    {
        var sb = new StringBuilder();
        sb.AppendLine("graph TD");

        var routerJobs = new HashSet<string>(
            definition.Connections
                .OfType<RouterConnection<TState>>()
                .Select(c => c.From));

        // ── Nodes ──
        foreach (var name in definition.Jobs.Keys)
        {
            var id = NodeId(name);
            var label = name == definition.EntryJob ? $"▶ {name}" : name;

            sb.AppendLine(routerJobs.Contains(name)
                ? $"    {id}{{\"{label}\"}}"
                : $"    {id}[\"{label}\"]");
        }

        if (ReferencesEnd(definition))
            sb.AppendLine("    _end([\"End\"])");

        sb.AppendLine();

        // ── Edges ──
        foreach (var connection in definition.Connections)
        {
            var from = NodeId(connection.From);

            switch (connection)
            {
                case DirectConnection dc:
                    sb.AppendLine($"    {from} --> {TargetId(dc.To)}");
                    break;

                case ForkConnection fc:
                    var forkLabel = fc.Mode == ForkMode.BestEffort ? "fork / best-effort" : "fork";
                    foreach (var target in fc.Targets)
                        sb.AppendLine($"    {from} -->|{forkLabel}| {NodeId(target)}");
                    break;

                case RouterConnection<TState>:
                    // Dynamic targets resolved at runtime — the diamond shape conveys decision semantics.
                    break;
            }
        }

        foreach (var join in definition.Joins)
        {
            var target = TargetId(join.Target);
            foreach (var source in join.Sources)
                sb.AppendLine($"    {NodeId(source)} -->|join| {target}");
        }

        // ── Styling ──
        sb.AppendLine();
        sb.AppendLine($"    style {NodeId(definition.EntryJob)} fill:#4CAF50,color:#fff");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Exports the workflow graph as a Markdown document with an embedded Mermaid code block.
    /// </summary>
    public static string ToMarkdownMermaid<TState>(this WorkflowDefinition<TState> definition)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {definition.Name}");
        sb.AppendLine();
        sb.AppendLine("```mermaid");
        sb.AppendLine(definition.ToMermaid());
        sb.Append("```");
        return sb.ToString();
    }

    /// <inheritdoc cref="ToMermaid{TState}(WorkflowDefinition{TState})"/>
    public static string ToMermaid<TState>(this Workflow<TState> workflow) =>
        workflow.Build().ToMermaid();

    /// <inheritdoc cref="ToMarkdownMermaid{TState}(WorkflowDefinition{TState})"/>
    public static string ToMarkdownMermaid<TState>(this Workflow<TState> workflow) =>
        workflow.Build().ToMarkdownMermaid();

    private static string NodeId(string name) =>
        $"j_{string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'))}";

    private static string TargetId(string to) =>
        to == Workflow.End ? "_end" : NodeId(to);

    private static bool ReferencesEnd<TState>(WorkflowDefinition<TState> def) =>
        def.Connections.OfType<DirectConnection>().Any(c => c.To == Workflow.End) ||
        def.Joins.Any(j => j.Target == Workflow.End);
}
