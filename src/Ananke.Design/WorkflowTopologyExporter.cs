using Ananke.Design.Tools;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Routing;
using Ananke.Orchestration.Tools;
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
///   <item>
///     <term>Ask (input turn)</term>
///     <description>Lossless — exported as <c>ask(name)</c>, distinguished from a plain
///     <c>interrupt(name)</c> via <see cref="WorkflowDefinition{TState}.InputJobs"/>.</description>
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
    /// Exports a manifest directly from a parsed <see cref="WorkflowManifest"/>.
    /// Preserves tool declarations, per-job tool references, and the semantic routing hint.
    /// </summary>
    public static string ToYaml(this WorkflowManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var sb = new StringBuilder();
        sb.AppendLine($"name: {manifest.Name}");
        sb.AppendLine();

        sb.AppendLine("models:");
        foreach (var (alias, model) in manifest.Models)
        {
            sb.AppendLine($"  {alias}:");
            sb.AppendLine($"    provider: {model.Provider}");
            sb.AppendLine($"    model: {model.Model}");
            if (!string.IsNullOrWhiteSpace(model.Endpoint))
                sb.AppendLine($"    endpoint: {model.Endpoint}");
        }

        if (manifest.Tools.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("tools:");
            foreach (var (key, tool) in manifest.Tools)
            {
                sb.AppendLine($"  {key}:");
                sb.AppendLine($"    name: {tool.Name}");
                sb.AppendLine($"    description: {tool.Description}");
                sb.AppendLine("    tags:");
                foreach (var tag in tool.Tags)
                    sb.AppendLine($"      - {tag}");

                if (!string.IsNullOrWhiteSpace(tool.Binding.Kind) || !string.IsNullOrWhiteSpace(tool.Binding.Reference))
                {
                    sb.AppendLine("    binding:");
                    if (!string.IsNullOrWhiteSpace(tool.Binding.Kind))
                        sb.AppendLine($"      kind: {tool.Binding.Kind}");
                    if (!string.IsNullOrWhiteSpace(tool.Binding.Reference))
                        sb.AppendLine($"      reference: {tool.Binding.Reference}");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("jobs:");
        foreach (var (name, job) in manifest.Jobs)
        {
            sb.AppendLine($"  {name}:");
            sb.AppendLine($"    type: {job.Type}");
            if (!string.IsNullOrWhiteSpace(job.ModelAlias))
                sb.AppendLine($"    model: {job.ModelAlias}");
            if (job.Tools.Count > 0)
            {
                sb.AppendLine("    tools:");
                foreach (var tool in job.Tools)
                    sb.AppendLine($"      - {tool}");
            }
            if (job.Semantic)
                sb.AppendLine("    semantic: true");
            if (!string.IsNullOrWhiteSpace(job.SystemPrompt))
            {
                if (job.SystemPrompt.Contains('\n'))
                {
                    sb.AppendLine("    system_prompt: |");
                    foreach (var line in job.SystemPrompt.Split('\n'))
                        sb.AppendLine($"      {line}");
                }
                else
                {
                    sb.AppendLine($"    system_prompt: {job.SystemPrompt}");
                }
            }

            if (job.MaxToolRounds != 3)
                sb.AppendLine($"    max_tool_rounds: {job.MaxToolRounds}");
        }

        sb.AppendLine();
        sb.AppendLine("connections:");
        foreach (var line in manifest.Connections)
            sb.AppendLine($"  - {line}");

        if (manifest.Profiles.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("profiles:");
            foreach (var (profileName, profile) in manifest.Profiles)
            {
                sb.AppendLine($"  {profileName}:");
                sb.AppendLine("    tools:");
                foreach (var (toolName, binding) in profile.Tools)
                {
                    sb.AppendLine($"      {toolName}:");
                    sb.AppendLine($"        execute: {binding.Execute}");
                    if (!string.IsNullOrWhiteSpace(binding.Platform))
                        sb.AppendLine($"        platform: {binding.Platform}");
                    if (!string.IsNullOrWhiteSpace(binding.Endpoint))
                        sb.AppendLine($"        endpoint: {binding.Endpoint}");
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

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

        // Interrupt / ask annotations — ask(name) is an input-collecting turn, a subset of
        // InterruptMode.Before jobs (see WorkflowDefinition.InputJobs); plain interrupts get
        // interrupt(name).
        foreach (var (name, descriptor) in definition.Jobs)
        {
            if (descriptor.Interrupt != Ananke.Orchestration.Jobs.InterruptMode.Before)
                continue;

            lines.Add(definition.InputJobs.Contains(name) ? $"ask({name})" : $"interrupt({name})");
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

    /// <summary>
    /// Creates a manifest from a parsed scaffold, preserving DSL-declared tools and semantic hints.
    /// </summary>
    public static WorkflowManifest ToManifest<TState>(this WorkflowScaffold<TState> scaffold)
    {
        ArgumentNullException.ThrowIfNull(scaffold);

        var tools = scaffold.ToolDeclarations.ToDictionary(
            kvp => kvp.Key,
            kvp => new Tools.ToolManifestEntry
            {
                Key = kvp.Key,
                Name = kvp.Value.Name,
                Description = kvp.Value.Description,
                Tags = kvp.Value.Tags
            },
            StringComparer.OrdinalIgnoreCase);

        var jobs = scaffold.JobNames.ToDictionary(
            name => name,
            name =>
            {
                scaffold.JobToolDeclarations.TryGetValue(name, out var use);
                return new JobDefinition
                {
                    Tools = use?.ToolNames ?? [],
                    Semantic = use?.Semantic ?? false
                };
            },
            StringComparer.OrdinalIgnoreCase);

        List<string> connections = [];
        foreach (var line in scaffold.GetTopologyDsl())
            connections.Add(line);

        return new WorkflowManifest
        {
            Name = GetWorkflowName(scaffold),
            Models = [],
            Tools = tools,
            Jobs = jobs,
            Connections = connections,
            Profiles = []
        };
    }

    /// <summary>
    /// Exports a parsed scaffold to manifest YAML, preserving DSL tool metadata and semantic hints.
    /// </summary>
    public static string ToManifestYaml<TState>(this WorkflowScaffold<TState> scaffold) =>
        scaffold.ToManifest().ToYaml();

    private static string GetWorkflowName<TState>(WorkflowScaffold<TState> scaffold) => scaffold.Name;
}
