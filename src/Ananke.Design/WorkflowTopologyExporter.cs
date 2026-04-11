using Ananke.Orchestration;
using Ananke.Orchestration.Routing;
using System.Text;

namespace Ananke.Design;

/// <summary>
/// Exports a built <see cref="WorkflowDefinition{TState}"/> back to the Ananke DSL
/// text format and/or a <c>.ananke.yml</c> manifest skeleton.
/// </summary>
/// <remarks>
/// <para>
/// This bridges the code-first and design-first worlds: build a workflow using the
/// fluent <see cref="Workflow{TState}"/> API or <see cref="AgenticPattern"/> builders,
/// then export the topology as DSL lines or a YAML manifest for documentation,
/// visualization, or as a starting point for a design-first project.
/// </para>
/// <para><b>Lossless vs. lossy:</b></para>
/// <list type="table">
///   <listheader>
///     <term>Connection type</term>
///     <description>Export fidelity</description>
///   </listheader>
///   <item>
///     <term>Direct, Fork, Join</term>
///     <description>Lossless — round-trips perfectly.</description>
///   </item>
///   <item>
///     <term>Loop</term>
///     <description>Structural only — the <c>Until</c> predicate is a <c>Func&lt;T, bool&gt;</c>
///     and cannot be serialized to text. Exported as a <c># loop</c> annotation with
///     <c>max_iterations</c>. The loop target and exit target are preserved.</description>
///   </item>
///   <item>
///     <term>Router</term>
///     <description>Structural only — the <c>IRouter&lt;T&gt;</c> logic is opaque.
///     Exported as <c>router(...)</c> with option names when available.</description>
///   </item>
///   <item>
///     <term>Interrupt</term>
///     <description>Lossless — exported as <c>interrupt(name)</c>.</description>
///   </item>
/// </list>
/// <para>
/// The manifest export does not include system prompts, model definitions, or tool
/// bindings — those are code concerns that don't exist in the
/// <see cref="WorkflowDefinition{TState}"/>. The exported YAML is a topology skeleton
/// with <c>TODO</c> placeholders.
/// </para>
/// </remarks>
public static class WorkflowTopologyExporter
{
    /// <summary>
    /// Exports the workflow topology as DSL connection lines.
    /// </summary>
    /// <example>
    /// <code>
    /// var workflow = new Workflow&lt;MyState&gt;("pipeline")
    ///     .Job("a", ...)
    ///     .Job("b", ...)
    ///     .Chain("a", "b", Workflow.End);
    ///
    /// var lines = workflow.Build().ToDsl();
    /// // ["a -> b", "b -> End"]
    /// </code>
    /// </example>
    public static IReadOnlyList<string> ToDsl<TState>(this WorkflowDefinition<TState> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var lines = new List<string>();

        foreach (var connection in definition.Connections)
        {
            switch (connection)
            {
                case DirectConnection dc:
                    lines.Add($"{dc.From} -> {dc.To}");
                    break;

                case ForkConnection fc:
                    var targets = string.Join(", ", fc.Targets);
                    lines.Add(fc.Mode == ForkMode.BestEffort
                        ? $"{fc.From} -> fork({targets}, mode: best-effort)"
                        : $"{fc.From} -> fork({targets})");
                    break;

                case LoopConnection<TState> lc:
                    // Loop predicates are opaque — emit structural annotation
                    lines.Add($"{lc.From} -> {lc.ExitTarget}  # loop({lc.From} -> {lc.LoopTarget}, max: {lc.MaxIterations})");
                    break;

                case RouterConnection<TState> rc:
                    // Router logic is opaque — emit placeholder with job name
                    lines.Add($"{rc.From} -> router(...)  # dynamic routing — bind in code");
                    break;
            }
        }

        foreach (var join in definition.Joins)
        {
            var sources = string.Join(", ", join.Sources);
            lines.Add($"join({sources}) -> {join.Target}");
        }

        // Interrupt annotations
        foreach (var (name, descriptor) in definition.Jobs)
        {
            if (descriptor.Interrupt == Ananke.Orchestration.Jobs.InterruptMode.Before)
                lines.Add($"interrupt({name})");
        }

        return lines;
    }

    /// <summary>
    /// Exports the workflow topology as a <c>.ananke.yml</c> manifest skeleton.
    /// Job types are inferred: jobs with outgoing connections are marked <c>code</c>
    /// by default. The manifest includes <c>TODO</c> placeholders for model definitions
    /// and system prompts.
    /// </summary>
    /// <example>
    /// <code>
    /// var yaml = workflow.Build().ToManifestYaml();
    /// File.WriteAllText("pipeline.ananke.yml", yaml);
    /// </code>
    /// </example>
    public static string ToManifestYaml<TState>(this WorkflowDefinition<TState> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var sb = new StringBuilder();

        // Header
        sb.AppendLine($"# {definition.Name}.ananke.yml");
        sb.AppendLine("# ────────────────────────────────────────────────");
        sb.AppendLine("# Exported from a code-first workflow definition.");
        sb.AppendLine("# Review and customize model aliases, system prompts,");
        sb.AppendLine("# and job types before use.");
        sb.AppendLine("# ────────────────────────────────────────────────");
        sb.AppendLine();

        // Name
        sb.AppendLine($"name: {definition.Name}");
        sb.AppendLine();

        // Models placeholder
        sb.AppendLine("# TODO: Define model aliases for agent jobs.");
        sb.AppendLine("models:");
        sb.AppendLine("  default:");
        sb.AppendLine("    provider: openai");
        sb.AppendLine("    model: gpt-4.1-mini");
        sb.AppendLine();

        // Jobs
        sb.AppendLine("jobs:");
        foreach (var (name, _) in definition.Jobs)
        {
            sb.AppendLine($"  {name}:");
            sb.AppendLine("    type: code  # TODO: change to 'agent' and add model/system_prompt if needed");
            sb.AppendLine();
        }

        // Connections
        sb.AppendLine("connections:");
        foreach (var line in definition.ToDsl())
        {
            // Strip inline comments for clean YAML
            var dslLine = line;
            var commentIdx = dslLine.IndexOf('#');
            if (commentIdx > 0)
            {
                var clean = dslLine[..commentIdx].TrimEnd();
                var comment = dslLine[commentIdx..];
                sb.AppendLine($"  - {clean}  {comment}");
            }
            else
            {
                sb.AppendLine($"  - {dslLine}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <inheritdoc cref="ToDsl{TState}(WorkflowDefinition{TState})"/>
    public static IReadOnlyList<string> ToDsl<TState>(this Workflow<TState> workflow) =>
        workflow.Build().ToDsl();

    /// <inheritdoc cref="ToManifestYaml{TState}(WorkflowDefinition{TState})"/>
    public static string ToManifestYaml<TState>(this Workflow<TState> workflow) =>
        workflow.Build().ToManifestYaml();
}
