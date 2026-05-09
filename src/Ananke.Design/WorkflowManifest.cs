using Ananke.Design.Tools;

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

    /// <summary>Tool declarations from the optional <c>tools:</c> section, keyed by tool key.</summary>
    public Dictionary<string, ToolManifestEntry> Tools { get; init; } = [];

    /// <summary>Raw DSL connection lines from the <c>connections:</c> section.</summary>
    public required List<string> Connections { get; init; }

    /// <summary>
    /// Deployment profiles from the optional <c>profiles:</c> section, keyed by profile name
    /// (e.g. <c>"local"</c>, <c>"azure-ai"</c>, <c>"vertex-ai"</c>).
    /// Each profile rebinds tool execution modes for a target environment.
    /// </summary>
    public Dictionary<string, ProfileDefinition> Profiles { get; init; } = [];

    /// <summary>
    /// Optional domain intent tags declared in the <c>intents:</c> section
    /// (e.g. <c>[enterprise_data, governance, agentic_loop]</c>).
    /// Used by the platform recommender to compute strength-alignment scores.
    /// When absent, the recommender treats strength alignment as neutral.
    /// </summary>
    public IReadOnlyList<string> Intents { get; init; } = [];

    /// <summary>
    /// Optional governance requirements declared in the <c>governance:</c> section.
    /// The platform recommender checks these against each candidate platform's governance flags.
    /// When absent, governance fit is treated as neutral (1.0).
    /// </summary>
    public ManifestGovernance? Governance { get; init; }

    /// <summary>
    /// Optional budget hints declared in the <c>budget:</c> section.
    /// Used by the platform recommender to weight cost-band fit.
    /// </summary>
    public ManifestBudget? Budget { get; init; }

    /// <summary>
    /// Optional SLO hints declared in the <c>slo:</c> section.
    /// Used by the platform recommender to weight latency-band fit.
    /// </summary>
    public ManifestSlo? Slo { get; init; }

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
        var tools = new Dictionary<string, ToolManifestEntry>();
        var jobs = new Dictionary<string, JobDefinition>();
        var connections = new List<string>();

        var profiles = new Dictionary<string, ProfileDefinition>();
        var intents = new List<string>();
        var governance = new ManifestGovernance();
        var budget = new ManifestBudget();
        var slo = new ManifestSlo();
        var hasGovernance = false;
        var hasBudget = false;
        var hasSlo = false;

        var section = Section.None;
        string? currentBlock = null;
        string? currentToolBinding = null;
        string? currentProfile = null;
        string? currentProfileTool = null;
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
                else if (line.StartsWith("tools:"))
                    section = Section.Tools;
                else if (line.StartsWith("jobs:"))
                    section = Section.Jobs;
                else if (line.StartsWith("connections:"))
                    section = Section.Connections;
                else if (line.StartsWith("profiles:"))
                    section = Section.Profiles;
                else if (line.StartsWith("intents:"))
                {
                    var inline = line["intents:".Length..].Trim();
                    intents.AddRange(ParseBracketList(inline));
                    section = Section.Intents;
                }
                else if (line.StartsWith("governance:"))
                {
                    hasGovernance = true;
                    section = Section.Governance;
                }
                else if (line.StartsWith("budget:"))
                {
                    hasBudget = true;
                    section = Section.Budget;
                }
                else if (line.StartsWith("slo:"))
                {
                    hasSlo = true;
                    section = Section.Slo;
                }
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

                case Section.Tools:
                    if (trimmed.EndsWith(':') && indent == 2)
                    {
                        currentBlock = trimmed[..^1];
                        tools[currentBlock] = new ToolManifestEntry
                        {
                            Key = currentBlock,
                            Name = currentBlock,
                            Description = string.Empty
                        };
                        currentToolBinding = null;
                        blockIndent = 2;
                    }
                    else if (currentBlock is not null && indent > blockIndent)
                    {
                        ApplyToolField(tools, currentBlock, trimmed, indent, ref currentToolBinding);
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

                case Section.Profiles:
                    ParseProfileLine(trimmed, indent, profiles, ref currentProfile, ref currentBlock, ref currentProfileTool);
                    break;

                case Section.Intents:
                    if (trimmed.StartsWith("- "))
                        intents.Add(trimmed[2..].Trim());
                    break;

                case Section.Governance:
                    ApplyGovernanceField(governance, trimmed);
                    break;

                case Section.Budget:
                    ApplyBudgetField(budget, trimmed);
                    break;

                case Section.Slo:
                    ApplySloField(slo, trimmed);
                    break;
            }
        }

        // Second pass: collect multi-line system_prompt blocks
        CollectMultiLineFields(lines, jobs);

        return new WorkflowManifest
        {
            Name = name ?? throw new InvalidOperationException("Manifest missing 'name' field."),
            Models = models,
            Tools = tools,
            Jobs = jobs,
            Connections = connections,
            Profiles = profiles,
            Intents = intents,
            Governance = hasGovernance ? governance : null,
            Budget = hasBudget ? budget : null,
            Slo = hasSlo ? slo : null
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
        else if (trimmed.StartsWith("semantic:"))
            def.Semantic = bool.Parse(trimmed["semantic:".Length..].Trim());
        else if (trimmed.StartsWith("max_tool_rounds:"))
            def.MaxToolRounds = int.Parse(trimmed["max_tool_rounds:".Length..].Trim());
    }

    private static void ApplyToolField(
        Dictionary<string, ToolManifestEntry> tools,
        string toolKey,
        string trimmed,
        int indent,
        ref string? currentToolBinding)
    {
        if (indent == 4 && trimmed == "binding:")
        {
            currentToolBinding = toolKey;
            return;
        }

        var tool = tools[toolKey];

        if (indent == 4 && trimmed.StartsWith("name:"))
        {
            tools[toolKey] = tool with { Name = trimmed["name:".Length..].Trim() };
            currentToolBinding = null;
        }
        else if (indent == 4 && trimmed.StartsWith("description:"))
        {
            tools[toolKey] = tool with { Description = trimmed["description:".Length..].Trim() };
            currentToolBinding = null;
        }
        else if (indent == 4 && trimmed.StartsWith("tags:"))
        {
            var inlineTags = ParseBracketList(trimmed["tags:".Length..].Trim());
            tools[toolKey] = tool with { Tags = inlineTags };
            currentToolBinding = null;
        }
        else if (indent >= 6 && currentToolBinding == toolKey && trimmed.StartsWith("kind:"))
        {
            tools[toolKey] = tool with
            {
                Binding = tool.Binding with { Kind = trimmed["kind:".Length..].Trim() }
            };
        }
        else if (indent >= 6 && currentToolBinding == toolKey && trimmed.StartsWith("reference:"))
        {
            tools[toolKey] = tool with
            {
                Binding = tool.Binding with { Reference = trimmed["reference:".Length..].Trim() }
            };
        }
    }

    private static void CollectMultiLineFields(string[] lines, Dictionary<string, JobDefinition> jobs)
    {
        string? currentJob = null;
        var inSystemPrompt = false;
        var inTools = false;
        var inRouter = false;
        var promptLines = new List<string>();
        var toolRefs = new List<string>();
        var routerStages = new List<RouterStageDescriptor>();
        Dictionary<string, string>? currentStageFields = null;
        var promptIndent = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (line.Length == 0 && !inSystemPrompt) continue;

            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;

            if (trimmed == "jobs:")
            {
                currentJob = null;
                inSystemPrompt = false;
                inTools = false;
                inRouter = false;
                promptLines.Clear();
                toolRefs.Clear();
                FinaliseRouterStage(currentStageFields, routerStages);
                currentStageFields = null;
                routerStages.Clear();
                continue;
            }

            if (indent == 0 && trimmed is "models:" or "tools:" or "connections:" or "profiles:")
            {
                if (inSystemPrompt && currentJob is not null)
                    jobs[currentJob].SystemPrompt = string.Join('\n', promptLines).Trim();

                if (inTools && currentJob is not null)
                    jobs[currentJob].Tools = [.. toolRefs];

                if (inRouter && currentJob is not null)
                {
                    FinaliseRouterStage(currentStageFields, routerStages);
                    currentStageFields = null;
                    jobs[currentJob].Router = [.. routerStages];
                }

                currentJob = null;
                inSystemPrompt = false;
                inTools = false;
                inRouter = false;
                promptLines.Clear();
                toolRefs.Clear();
                routerStages.Clear();
                continue;
            }

            // Detect job block start only while inside the jobs section.
            if (indent == 2 && currentJob is null && trimmed.EndsWith(':') && !trimmed.Contains(' ') && !jobs.ContainsKey(trimmed[..^1]))
            {
                continue;
            }

            if (indent == 2 && jobs.ContainsKey(trimmed[..^1]) && trimmed.EndsWith(':') && !trimmed.Contains(' '))
            {
                if (inSystemPrompt && currentJob is not null)
                    jobs[currentJob].SystemPrompt = string.Join('\n', promptLines).Trim();

                if (inTools && currentJob is not null)
                    jobs[currentJob].Tools = [.. toolRefs];

                if (inRouter && currentJob is not null)
                {
                    FinaliseRouterStage(currentStageFields, routerStages);
                    currentStageFields = null;
                    jobs[currentJob].Router = [.. routerStages];
                }

                currentJob = trimmed[..^1];
                inSystemPrompt = false;
                inTools = false;
                inRouter = false;
                promptLines.Clear();
                toolRefs.Clear();
                routerStages.Clear();
                continue;
            }

            if (currentJob is null)
                continue;

            if (trimmed == "tools:" && !inSystemPrompt)
            {
                if (inRouter)
                {
                    FinaliseRouterStage(currentStageFields, routerStages);
                    currentStageFields = null;
                    jobs[currentJob].Router = [.. routerStages];
                    inRouter = false;
                    routerStages.Clear();
                }
                inTools = true;
                toolRefs.Clear();
                continue;
            }

            if (trimmed == "router:" && !inSystemPrompt)
            {
                if (inTools)
                {
                    jobs[currentJob].Tools = [.. toolRefs];
                    inTools = false;
                }
                inRouter = true;
                FinaliseRouterStage(currentStageFields, routerStages);
                currentStageFields = null;
                routerStages.Clear();
                continue;
            }

            if (inTools)
            {
                if (trimmed.StartsWith("- ") && indent >= 6)
                {
                    toolRefs.Add(trimmed[2..].Trim());
                    continue;
                }

                jobs[currentJob].Tools = [.. toolRefs];
                inTools = false;
            }

            if (inRouter)
            {
                // New stage list item: "- kind: <k>" at indent 6
                if (trimmed.StartsWith("- ") && indent >= 6)
                {
                    FinaliseRouterStage(currentStageFields, routerStages);
                    currentStageFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var rest = trimmed[2..].Trim(); // e.g. "kind: semantic_recall"
                    ApplyKvPair(currentStageFields, rest);
                    continue;
                }

                // Sub-field of current stage item at indent >= 8
                if (indent >= 8 && currentStageFields is not null)
                {
                    ApplyKvPair(currentStageFields, trimmed);
                    continue;
                }

                // Something at a shallower level — flush router
                FinaliseRouterStage(currentStageFields, routerStages);
                currentStageFields = null;
                jobs[currentJob].Router = [.. routerStages];
                inRouter = false;
                routerStages.Clear();
            }

            if (trimmed.StartsWith("system_prompt:"))
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

        if (inTools && currentJob is not null)
            jobs[currentJob].Tools = [.. toolRefs];

        if (inRouter && currentJob is not null)
        {
            FinaliseRouterStage(currentStageFields, routerStages);
            jobs[currentJob].Router = [.. routerStages];
        }

        if (inSystemPrompt && currentJob is not null)
            jobs[currentJob].SystemPrompt = string.Join('\n', promptLines).Trim();
    }

    /// <summary>Parses "key: value" into the supplied dictionary.</summary>
    private static void ApplyKvPair(Dictionary<string, string> fields, string trimmed)
    {
        var colon = trimmed.IndexOf(':');
        if (colon <= 0) return;
        var key = trimmed[..colon].Trim();
        var value = trimmed[(colon + 1)..].Trim();
        fields[key] = value;
    }

    /// <summary>
    /// Converts the field bag accumulated for one <c>router:</c> list item into a
    /// <see cref="RouterStageDescriptor"/> and appends it to <paramref name="stages"/>.
    /// </summary>
    private static void FinaliseRouterStage(
        Dictionary<string, string>? fields,
        List<RouterStageDescriptor> stages)
    {
        if (fields is null || !fields.TryGetValue("kind", out var kind))
            return;

        RouterStageDescriptor descriptor = kind switch
        {
            "pinned" => new PinnedStageDescriptor
            {
                Kind = kind,
                Tools = fields.TryGetValue("tools", out var toolsStr)
                    ? ParseBracketList(toolsStr)
                    : [],
            },
            "health_filter" => new HealthFilterStageDescriptor { Kind = kind },
            "semantic_recall" => new SemanticRecallStageDescriptor
            {
                Kind = kind,
                TopK = fields.TryGetValue("top_k", out var topK) && int.TryParse(topK, out var k) ? k : 8,
            },
            "affinity_rerank" => new AffinityRerankStageDescriptor { Kind = kind },
            "heuristic_tags" => new HeuristicTagsStageDescriptor { Kind = kind },
            "llm" => new LlmStageDescriptor
            {
                Kind = kind,
                Model = fields.TryGetValue("model", out var mdl) ? mdl
                    : throw new InvalidOperationException("ANANKE_ROUTER_002: 'llm' router stage is missing required field 'model'."),
                MaxSelected = fields.TryGetValue("max_selected", out var ms) && int.TryParse(ms, out var n) ? n : 8,
            },
            _ => throw new InvalidOperationException(
                $"ANANKE_ROUTER_001: Unknown router stage kind '{kind}'."),
        };

        stages.Add(descriptor);
    }

    private static IReadOnlyList<string> ParseBracketList(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        if (value.StartsWith('[') && value.EndsWith(']'))
        {
            var content = value[1..^1].Trim();
            if (content.Length == 0)
                return [];

            return content.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }

        return [];
    }

    private static void ParseProfileLine(
        string trimmed, int indent,
        Dictionary<string, ProfileDefinition> profiles,
        ref string? currentProfile,
        ref string? currentBlock,
        ref string? currentProfileTool)
    {
        // Level 1 (indent 2): profile name
        if (indent == 2 && trimmed.EndsWith(':') && !trimmed.Contains(' '))
        {
            currentProfile = trimmed[..^1];
            profiles[currentProfile] = new ProfileDefinition();
            currentBlock = null;
            currentProfileTool = null;
            return;
        }

        if (currentProfile is null) return;

        // Level 2 (indent 4): "tools:"
        if (indent == 4 && trimmed == "tools:")
        {
            currentBlock = "tools";
            currentProfileTool = null;
            return;
        }

        if (currentBlock != "tools") return;

        // Level 3 (indent 6): tool name
        if (indent == 6 && trimmed.EndsWith(':') && !trimmed.Contains(' '))
        {
            currentProfileTool = trimmed[..^1];
            profiles[currentProfile].Tools[currentProfileTool] = new ToolBindingDefinition();
            return;
        }

        // Inline format: "search: { platform: bing_search }" or "search: { execute: local }"
        if (indent == 6 && trimmed.Contains('{') && trimmed.Contains('}'))
        {
            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx < 0) return;
            var toolName = trimmed[..colonIdx].Trim();
            var braceContent = trimmed[(trimmed.IndexOf('{') + 1)..trimmed.IndexOf('}')].Trim();
            var binding = new ToolBindingDefinition();
            foreach (var pair in braceContent.Split(',', StringSplitOptions.TrimEntries))
            {
                var parts = pair.Split(':', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2)
                    ApplyToolBindingField(binding, parts[0], parts[1]);
            }
            profiles[currentProfile].Tools[toolName] = binding;
            return;
        }

        // Level 4 (indent 8): tool binding fields
        if (indent >= 8 && currentProfileTool is not null)
        {
            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx > 0)
            {
                var key = trimmed[..colonIdx].Trim();
                var value = trimmed[(colonIdx + 1)..].Trim();
                ApplyToolBindingField(
                    profiles[currentProfile].Tools[currentProfileTool], key, value);
            }
        }
    }

    private static void ApplyToolBindingField(ToolBindingDefinition def, string key, string value)
    {
        switch (key)
        {
            case "execute":
                def.Execute = value;
                break;
            case "platform":
                def.Execute = "platform";
                def.Platform = value;
                break;
            case "endpoint":
                def.Endpoint = value;
                break;
        }
    }

    private static void ApplyGovernanceField(ManifestGovernance g, string trimmed)
    {
        if (trimmed.StartsWith("rbac:")) g.Rbac = trimmed["rbac:".Length..].Trim() == "required";
        else if (trimmed.StartsWith("privateNetworking:")) g.PrivateNetworking = trimmed["privateNetworking:".Length..].Trim() == "required";
        else if (trimmed.StartsWith("contentSafety:")) g.ContentSafety = trimmed["contentSafety:".Length..].Trim() == "required";
        else if (trimmed.StartsWith("region:")) g.Region = trimmed["region:".Length..].Trim();
    }

    private static void ApplyBudgetField(ManifestBudget b, string trimmed)
    {
        if (trimmed.StartsWith("maxCostPerRunUsd:") &&
            double.TryParse(trimmed["maxCostPerRunUsd:".Length..].Trim(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            b.MaxCostPerRunUsd = v;
    }

    private static void ApplySloField(ManifestSlo s, string trimmed)
    {
        if (trimmed.StartsWith("latencyP50Ms:") &&
            int.TryParse(trimmed["latencyP50Ms:".Length..].Trim(), out var v))
            s.LatencyP50Ms = v;
    }

    private enum Section { None, Models, Tools, Jobs, Connections, Profiles, Intents, Governance, Budget, Slo }
}

/// <summary>
/// Governance requirements from the optional <c>governance:</c> section of a manifest.
/// Used by the platform recommender to check each candidate platform's governance flags.
/// </summary>
public sealed class ManifestGovernance
{
    /// <summary>Whether RBAC is required on the target platform.</summary>
    public bool Rbac { get; set; }

    /// <summary>Whether private networking (VNet / VPC) is required.</summary>
    public bool PrivateNetworking { get; set; }

    /// <summary>Whether content safety filtering is required.</summary>
    public bool ContentSafety { get; set; }

    /// <summary>Required deployment region, or <see langword="null"/> when unrestricted.</summary>
    public string? Region { get; set; }
}

/// <summary>
/// Budget hints from the optional <c>budget:</c> section of a manifest.
/// </summary>
public sealed class ManifestBudget
{
    /// <summary>Maximum acceptable cost per workflow execution, in USD.</summary>
    public double? MaxCostPerRunUsd { get; set; }
}

/// <summary>
/// SLO hints from the optional <c>slo:</c> section of a manifest.
/// </summary>
public sealed class ManifestSlo
{
    /// <summary>Desired p50 end-to-end latency in milliseconds.</summary>
    public int? LatencyP50Ms { get; set; }
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

    /// <summary>
    /// Manifest-declared tool keys attached to this job.
    /// When empty, the job has no manifest-declared tools.
    /// </summary>
    public IReadOnlyList<string> Tools { get; set; } = [];

    /// <summary>
    /// When <see langword="true"/>, the job opts into memory-backed semantic tool routing.
    /// When <see langword="false"/>, the declared tool list is exposed eagerly for backward compatibility.
    /// </summary>
    public bool Semantic { get; set; }

    /// <summary>Maximum tool-calling rounds before forcing a final response. Default is 3.</summary>
    public int MaxToolRounds { get; set; } = 3;

    /// <summary>
    /// Declarative router chain for this job's tool kit.
    /// When non-empty, <c>WorkflowToolResolver</c> materialises a
    /// <see cref="Ananke.Orchestration.Tools.Routing.CompositeSmartToolRouter"/> and
    /// registers it on the job's <see cref="Ananke.Orchestration.Tools.ToolKit"/> via
    /// <c>WithRouter</c>
    /// </summary>
    public IReadOnlyList<RouterStageDescriptor> Router { get; set; } = [];
}

/// <summary>
/// A deployment profile from the <c>profiles:</c> section, containing
/// per-tool binding overrides for a specific target environment.
/// </summary>
/// <example>
/// <code>
/// profiles:
///   azure-ai:
///     tools:
///       search: { platform: bing_search }
///       code:   { platform: code_interpreter }
/// </code>
/// </example>
public sealed class ProfileDefinition
{
    /// <summary>Tool bindings keyed by tool name.</summary>
    public Dictionary<string, ToolBindingDefinition> Tools { get; init; } = [];
}

/// <summary>
/// Describes how a tool should be bound when deploying to a specific platform.
/// Parsed from the inline or block YAML under a profile's <c>tools:</c> key.
/// </summary>
public sealed class ToolBindingDefinition
{
    /// <summary>
    /// Execution mode: <c>"local"</c>, <c>"callback"</c>, <c>"mcp"</c>,
    /// <c>"openapi"</c>, or <c>"platform"</c>.
    /// </summary>
    public string Execute { get; set; } = "local";

    /// <summary>
    /// Platform-native capability identifier (e.g. <c>"code_interpreter"</c>).
    /// Passed through verbatim to the platform API.
    /// Only meaningful when <see cref="Execute"/> is <c>"platform"</c>.
    /// </summary>
    public string? Platform { get; set; }

    /// <summary>
    /// Endpoint URI for <c>"callback"</c>, <c>"mcp"</c>, or <c>"openapi"</c> modes.
    /// </summary>
    public string? Endpoint { get; set; }
}
