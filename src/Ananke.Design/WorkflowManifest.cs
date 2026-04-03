namespace Ananke.Design;

/// <summary>
/// Parsed workflow manifest from an <c>.ananke.yml</c> file.
/// Contains the workflow name, model aliases, job declarations (agent/code),
/// and topology connection lines.
/// </summary>
/// <remarks>
/// <para>
/// The manifest is the declarative complement to <see cref="WorkflowScaffold{TState}"/>.
/// The scaffold handles topology parsing and job binding; the manifest adds model definitions,
/// system prompts, and job type metadata on top.
/// </para>
/// <para>
/// API keys and secrets are never stored in the manifest — they are resolved at runtime
/// from <c>IConfiguration</c>, environment variables, or a secrets manager.
/// </para>
/// </remarks>
public sealed class WorkflowManifest
{
    /// <summary>Workflow name from the <c>name:</c> field.</summary>
    public required string Name { get; init; }

    /// <summary>Named model aliases from the <c>models:</c> section.</summary>
    public required Dictionary<string, ModelDefinition> Models { get; init; }

    /// <summary>Job declarations from the <c>jobs:</c> section, keyed by job name.</summary>
    public required Dictionary<string, JobDefinition> Jobs { get; init; }

    /// <summary>Raw DSL connection lines from the <c>connections:</c> section.</summary>
    public required List<string> Connections { get; init; }

    /// <summary>
    /// Loads and parses an <c>.ananke.yml</c> manifest file.
    /// </summary>
    public static WorkflowManifest Load(string path)
    {
        var lines = File.ReadAllLines(path);
        return Parse(lines);
    }

    /// <summary>
    /// Parses manifest content from an array of lines (e.g. from <see cref="File.ReadAllLines(string)"/>
    /// or an embedded resource).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This parser handles an intentionally minimal YAML subset — just enough for the
    /// <c>.ananke.yml</c> manifest schema. It is <b>not</b> a general-purpose YAML parser.
    /// </para>
    /// <para><b>Supported YAML features:</b></para>
    /// <list type="bullet">
    ///   <item>Top-level scalars (<c>name: value</c>)</item>
    ///   <item>Two-level nested mappings (2-space indent blocks under <c>models:</c> / <c>jobs:</c>)</item>
    ///   <item>Block scalars (<c>|</c> literal style for multi-line <c>system_prompt</c>)</item>
    ///   <item>Dash-prefixed list items (<c>- item</c> under <c>connections:</c>)</item>
    ///   <item>Comment lines (<c># ...</c>) and blank lines (skipped)</item>
    /// </list>
    /// <para><b>Not supported</b> (by design — not needed by the manifest schema):</para>
    /// <list type="bullet">
    ///   <item>Anchors / aliases (<c>&amp;</c> / <c>*</c>), merge keys (<c>&lt;&lt;</c>)</item>
    ///   <item>Flow sequences (<c>[a, b]</c>) or flow mappings (<c>{a: 1}</c>)</item>
    ///   <item>Quoted strings, tags (<c>!!str</c>), multi-document (<c>---</c>)</item>
    /// </list>
    /// <para>
    /// A general-purpose YAML library (e.g. YamlDotNet, SharpYaml) was evaluated and rejected:
    /// the manifest schema is fixed, the parser is well-tested (14 tests), and adding a
    /// dependency would increase package size with no user-facing benefit.
    /// </para>
    /// </remarks>
    public static WorkflowManifest Parse(string[] lines)
    {
        string? name = null;
        var models = new Dictionary<string, ModelDefinition>();
        var jobs = new Dictionary<string, JobDefinition>();
        var connections = new List<string>();

        var section = Section.None;
        string? currentBlock = null;
        var blockIndent = 0;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
                continue;

            // Top-level keys
            if (!char.IsWhiteSpace(line[0]))
            {
                currentBlock = null;

                if (line.StartsWith("name:"))
                {
                    name = line["name:".Length..].Trim();
                    section = Section.None;
                }
                else if (line.StartsWith("models:"))
                    section = Section.Models;
                else if (line.StartsWith("jobs:"))
                    section = Section.Jobs;
                else if (line.StartsWith("connections:"))
                    section = Section.Connections;
                else
                    section = Section.None;

                continue;
            }

            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;

            switch (section)
            {
                case Section.Models:
                    if (trimmed.EndsWith(':') && indent == 2)
                    {
                        currentBlock = trimmed[..^1];
                        models[currentBlock] = new ModelDefinition();
                        blockIndent = 2;
                    }
                    else if (currentBlock is not null && indent > blockIndent)
                    {
                        ApplyModelField(models[currentBlock], trimmed);
                    }
                    break;

                case Section.Jobs:
                    if (trimmed.EndsWith(':') && indent == 2)
                    {
                        currentBlock = trimmed[..^1];
                        jobs[currentBlock] = new JobDefinition();
                        blockIndent = 2;
                    }
                    else if (currentBlock is not null && indent > blockIndent)
                    {
                        ApplyJobField(jobs[currentBlock], trimmed);
                    }
                    break;

                case Section.Connections:
                    if (trimmed.StartsWith("- "))
                        connections.Add(trimmed[2..]);
                    break;
            }
        }

        // Second pass: collect multi-line system_prompt blocks
        CollectMultiLineFields(lines, jobs);

        return new WorkflowManifest
        {
            Name = name ?? throw new InvalidOperationException("Manifest missing 'name' field."),
            Models = models,
            Jobs = jobs,
            Connections = connections
        };
    }

    private static void ApplyModelField(ModelDefinition def, string line)
    {
        if (line.StartsWith("provider:"))
            def.Provider = line["provider:".Length..].Trim();
        else if (line.StartsWith("model:"))
            def.Model = line["model:".Length..].Trim();
        else if (line.StartsWith("endpoint:"))
            def.Endpoint = line["endpoint:".Length..].Trim();
    }

    private static void ApplyJobField(JobDefinition def, string trimmed)
    {
        if (trimmed.StartsWith("type:"))
            def.Type = trimmed["type:".Length..].Trim();
        else if (trimmed.StartsWith("model:"))
            def.ModelAlias = trimmed["model:".Length..].Trim();
        else if (trimmed.StartsWith("max_tool_rounds:"))
            def.MaxToolRounds = int.Parse(trimmed["max_tool_rounds:".Length..].Trim());
    }

    private static void CollectMultiLineFields(string[] lines, Dictionary<string, JobDefinition> jobs)
    {
        string? currentJob = null;
        var inSystemPrompt = false;
        var promptLines = new List<string>();
        var promptIndent = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (line.Length == 0 && !inSystemPrompt) continue;

            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;

            // Detect job block start
            if (indent == 2 && trimmed.EndsWith(':') && !trimmed.Contains(' '))
            {
                if (inSystemPrompt && currentJob is not null)
                    jobs[currentJob].SystemPrompt = string.Join('\n', promptLines).Trim();

                currentJob = trimmed[..^1];
                inSystemPrompt = false;
                promptLines.Clear();
                continue;
            }

            if (currentJob is not null && trimmed.StartsWith("system_prompt:"))
            {
                var inline = trimmed["system_prompt:".Length..].Trim();
                if (inline.Length > 0 && inline != "|")
                {
                    jobs[currentJob].SystemPrompt = inline;
                    inSystemPrompt = false;
                }
                else
                {
                    inSystemPrompt = true;
                    promptIndent = indent + 2;
                    promptLines.Clear();
                }
                continue;
            }

            if (inSystemPrompt)
            {
                if (indent < promptIndent && trimmed.Length > 0)
                {
                    if (indent <= promptIndent - 2 && trimmed.Contains(':'))
                    {
                        jobs[currentJob!].SystemPrompt = string.Join('\n', promptLines).Trim();
                        inSystemPrompt = false;
                        ApplyJobField(jobs[currentJob!], trimmed);
                        continue;
                    }
                }

                if (line.Length == 0)
                    promptLines.Add("");
                else if (indent >= promptIndent)
                    promptLines.Add(line[promptIndent..]);
                else
                {
                    jobs[currentJob!].SystemPrompt = string.Join('\n', promptLines).Trim();
                    inSystemPrompt = false;
                }
            }
        }

        if (inSystemPrompt && currentJob is not null)
            jobs[currentJob].SystemPrompt = string.Join('\n', promptLines).Trim();
    }

    private enum Section { None, Models, Jobs, Connections }
}

/// <summary>
/// Describes a named model alias declared in the <c>models:</c> section of a manifest.
/// </summary>
public sealed class ModelDefinition
{
    /// <summary>Provider identifier (e.g. <c>"openai"</c>, <c>"anthropic"</c>).</summary>
    public string Provider { get; set; } = "openai";

    /// <summary>Model name passed to the provider SDK (e.g. <c>"gpt-4.1-mini"</c>).</summary>
    public string Model { get; set; } = "gpt-4.1-mini";

    /// <summary>
    /// Optional custom API endpoint. When set, the provider SDK targets this URL instead of
    /// the default. Used for OpenAI-compatible servers such as Ollama (<c>http://localhost:11434/v1</c>),
    /// LM Studio, vLLM, or Azure OpenAI.
    /// </summary>
    public string? Endpoint { get; set; }
}

/// <summary>
/// Describes a job declared in the <c>jobs:</c> section of a manifest.
/// </summary>
public sealed class JobDefinition
{
    /// <summary>Job type: <c>"agent"</c> or <c>"code"</c>.</summary>
    public string Type { get; set; } = "code";

    /// <summary>
    /// Name of the model alias from the <c>models:</c> section.
    /// Only applicable when <see cref="Type"/> is <c>"agent"</c>.
    /// </summary>
    public string? ModelAlias { get; set; }

    /// <summary>
    /// System prompt for the agent. Supports YAML multi-line <c>|</c> block syntax.
    /// Only applicable when <see cref="Type"/> is <c>"agent"</c>.
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>Maximum tool-calling rounds before forcing a final response. Default is 3.</summary>
    public int MaxToolRounds { get; set; } = 3;
}
