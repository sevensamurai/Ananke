using System.Globalization;
using System.Text;
using Ananke.Organics.Division;

namespace Ananke.Organics.Kernel.Snapshots;

/// <summary>
/// Exports a <see cref="HostSnapshot"/> to a portable YAML format and parses
/// it back. The YAML extends the <c>.ananke.yml</c> manifest convention with
/// kernel-level metadata (routing, division history, memory profiles).
/// </summary>
/// <remarks>
/// <para>
/// The exported YAML is human-readable and diffable — ideal for reviewing
/// structural changes before/after division in version control.
/// </para>
/// <para>
/// Like <see cref="Ananke.Design.WorkflowManifest"/>, this uses a minimal
/// hand-written YAML emitter/parser rather than a general-purpose library.
/// The schema is fixed and well-scoped.
/// </para>
/// </remarks>
public static class HostSnapshotExporter
{
    /// <summary>
    /// Exports a <see cref="HostSnapshot"/> to YAML text.
    /// </summary>
    public static string ToYaml(HostSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var sb = new StringBuilder();

        sb.AppendLine($"# Kernel snapshot — {snapshot.KernelId} v{snapshot.Version}");
        sb.AppendLine($"# Taken at {snapshot.TakenAt:O}");
        sb.AppendLine();

        sb.AppendLine($"kernel: {snapshot.KernelId}");
        sb.AppendLine($"version: {snapshot.Version}");
        sb.AppendLine($"taken_at: {snapshot.TakenAt:O}");
        sb.AppendLine();

        // ── Cells ──────────────────────────────────────────────────
        sb.AppendLine("cells:");
        foreach (var cell in snapshot.Cells)
        {
            sb.AppendLine($"  {cell.Name}:");
            sb.AppendLine($"    domain: {cell.Domain}");

            if (cell.SplitFrom is not null)
                sb.AppendLine($"    divided_from: {cell.SplitFrom}");

            // Tools
            if (cell.Tools.Count > 0)
            {
                sb.AppendLine("    tools:");
                foreach (var tool in cell.Tools)
                    sb.AppendLine($"      - {tool}");
            }

            // Models
            if (cell.Models.Count > 0)
            {
                sb.AppendLine("    models:");
                foreach (var (alias, model) in cell.Models)
                {
                    sb.AppendLine($"      {alias}:");
                    sb.AppendLine($"        provider: {model.Provider}");
                    sb.AppendLine($"        model: {model.Model}");
                    if (model.Endpoint is not null)
                        sb.AppendLine($"        endpoint: {model.Endpoint}");
                }
            }

            // Jobs
            if (cell.Jobs.Count > 0)
            {
                sb.AppendLine("    jobs:");
                foreach (var (name, job) in cell.Jobs)
                {
                    sb.AppendLine($"      {name}:");
                    sb.AppendLine($"        type: {job.Type}");
                    if (job.ModelAlias is not null)
                        sb.AppendLine($"        model: {job.ModelAlias}");
                    if (job.MaxToolRounds != 3)
                        sb.AppendLine($"        max_tool_rounds: {job.MaxToolRounds}");
                    if (job.SystemPrompt is not null)
                    {
                        sb.AppendLine("        system_prompt: |");
                        foreach (var line in job.SystemPrompt.Split('\n'))
                            sb.AppendLine($"          {line}");
                    }
                }
            }

            // Connections
            if (cell.Connections.Count > 0)
            {
                sb.AppendLine("    connections:");
                foreach (var conn in cell.Connections)
                    sb.AppendLine($"      - {conn}");
            }

            // Memory profile
            if (cell.MemoryProfile is not null)
            {
                sb.AppendLine("    memory:");
                sb.AppendLine($"      domains: [{string.Join(", ", cell.MemoryProfile.Domains)}]");
                if (cell.MemoryProfile.LineageTags.Count > 0)
                    sb.AppendLine($"      lineage: [{string.Join(", ", cell.MemoryProfile.LineageTags)}]");
            }

            sb.AppendLine();
        }

        // ── Routing ────────────────────────────────────────────────
        if (snapshot.RoutingTable.Count > 0)
        {
            sb.AppendLine("routing:");
            foreach (var (domain, cellName) in snapshot.RoutingTable)
                sb.AppendLine($"  {domain}: {cellName}");
            sb.AppendLine();
        }

        // ── Division history ───────────────────────────────────────
        if (snapshot.DivisionHistory.Count > 0)
        {
            sb.AppendLine("history:");
            foreach (var record in snapshot.DivisionHistory)
            {
                sb.AppendLine($"  - parent: {record.ParentWorkflow}");
                sb.AppendLine($"    children: [{string.Join(", ", record.Children)}]");
                sb.AppendLine($"    reason: \"{EscapeYaml(record.Reason)}\"");
                sb.AppendLine($"    occurred_at: {record.OccurredAt:O}");
                if (record.ApprovedBy is not null)
                    sb.AppendLine($"    approved_by: {record.ApprovedBy}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Parses a kernel snapshot from YAML text previously exported by <see cref="ToYaml"/>.
    /// </summary>
    public static HostSnapshot FromYaml(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var lines = yaml.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        return FromYaml(lines);
    }

    /// <summary>
    /// Parses a kernel snapshot from YAML lines previously exported by <see cref="ToYaml"/>.
    /// </summary>
    public static HostSnapshot FromYaml(string[] lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        string? KernelId = null;
        var version = 0;
        var takenAt = DateTimeOffset.MinValue;
        var cells = new List<WorkflowSnapshot>();
        var routing = new Dictionary<string, string>();
        var history = new List<DivisionRecord>();

        var section = Section.None;
        string? currentWorkflow = null;
        string? currentSubSection = null;
        string? currentBlock = null;

        // Per-cell accumulators
        string? workflowDomain = null;
        string? cellSplitFrom = null;
        var workflowTools = new List<string>();
        var workflowModels = new Dictionary<string, ModelSnapshot>();
        var workflowJobs = new Dictionary<string, JobSnapshot>();
        var workflowConnections = new List<string>();
        var memoryDomains = new List<string>();
        var memoryLineage = new List<string>();
        var hasMemory = false;

        // Per-model accumulators
        string? modelProvider = null;
        string? modelName = null;
        string? modelEndpoint = null;

        // Per-job accumulators
        string? jobType = null;
        string? jobModelAlias = null;
        string? jobSystemPrompt = null;
        var jobMaxToolRounds = 3;
        var inSystemPrompt = false;
        var promptLines = new List<string>();

        // History accumulators
        string? histParent = null;
        List<string>? histChildren = null;
        string? histReason = null;
        DateTimeOffset histOccurred = default;
        string? histApproved = null;
        var inHistoryItem = false;

        void FlushModel()
        {
            if (currentBlock is not null && modelProvider is not null && modelName is not null)
                workflowModels[currentBlock] = new ModelSnapshot
                {
                    Provider = modelProvider, Model = modelName, Endpoint = modelEndpoint
                };
            modelProvider = null;
            modelName = null;
            modelEndpoint = null;
        }

        void FlushJob()
        {
            if (inSystemPrompt && promptLines.Count > 0)
                jobSystemPrompt = string.Join('\n', promptLines).TrimEnd();
            inSystemPrompt = false;
            promptLines.Clear();

            if (currentBlock is not null && jobType is not null)
                workflowJobs[currentBlock] = new JobSnapshot
                {
                    Type = jobType,
                    ModelAlias = jobModelAlias,
                    SystemPrompt = jobSystemPrompt,
                    MaxToolRounds = jobMaxToolRounds
                };
            jobType = null;
            jobModelAlias = null;
            jobSystemPrompt = null;
            jobMaxToolRounds = 3;
        }

        void FlushCell()
        {
            FlushModel();
            FlushJob();
            if (currentWorkflow is not null && workflowDomain is not null)
            {
                MemoryProfile? profile = hasMemory
                    ? new MemoryProfile { Domains = memoryDomains.ToList(), LineageTags = memoryLineage.ToList() }
                    : null;

                cells.Add(new WorkflowSnapshot
                {
                    Name = currentWorkflow,
                    Domain = workflowDomain,
                    SplitFrom = cellSplitFrom,
                    Tools = workflowTools.ToList(),
                    Models = new Dictionary<string, ModelSnapshot>(workflowModels),
                    Jobs = new Dictionary<string, JobSnapshot>(workflowJobs),
                    Connections = workflowConnections.ToList(),
                    MemoryProfile = profile
                });
            }

            currentWorkflow = null;
            workflowDomain = null;
            cellSplitFrom = null;
            workflowTools.Clear();
            workflowModels.Clear();
            workflowJobs.Clear();
            workflowConnections.Clear();
            memoryDomains.Clear();
            memoryLineage.Clear();
            hasMemory = false;
            currentSubSection = null;
            currentBlock = null;
        }

        void FlushHistoryItem()
        {
            if (inHistoryItem && histParent is not null && histChildren is not null)
                history.Add(new DivisionRecord
                {
                    ParentWorkflow = histParent,
                    Children = histChildren,
                    Reason = histReason ?? "",
                    OccurredAt = histOccurred,
                    ApprovedBy = histApproved
                });
            histParent = null;
            histChildren = null;
            histReason = null;
            histOccurred = default;
            histApproved = null;
            inHistoryItem = false;
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
                continue;

            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;

            // Handle multi-line system_prompt
            if (inSystemPrompt)
            {
                if (indent >= 10) // 10 = 6(job field) + 4(prompt body)
                {
                    promptLines.Add(trimmed);
                    continue;
                }
                inSystemPrompt = false;
                jobSystemPrompt = string.Join('\n', promptLines).TrimEnd();
                promptLines.Clear();
            }

            // Top-level keys (indent 0)
            if (indent == 0)
            {
                if (section == Section.Cells) FlushCell();
                if (section == Section.History) FlushHistoryItem();

                if (trimmed.StartsWith("kernel:"))
                {
                    KernelId = trimmed["kernel:".Length..].Trim();
                    section = Section.None;
                }
                else if (trimmed.StartsWith("version:"))
                {
                    version = int.Parse(trimmed["version:".Length..].Trim(), CultureInfo.InvariantCulture);
                    section = Section.None;
                }
                else if (trimmed.StartsWith("taken_at:"))
                {
                    takenAt = DateTimeOffset.Parse(trimmed["taken_at:".Length..].Trim(), CultureInfo.InvariantCulture);
                    section = Section.None;
                }
                else if (trimmed == "cells:")
                    section = Section.Cells;
                else if (trimmed == "routing:")
                    section = Section.Routing;
                else if (trimmed == "history:")
                    section = Section.History;
                continue;
            }

            switch (section)
            {
                case Section.Cells:
                    ParseCellLine(indent, trimmed);
                    break;
                case Section.Routing when indent == 2 && trimmed.Contains(':'):
                    var rParts = trimmed.Split(':', 2);
                    routing[rParts[0].Trim()] = rParts[1].Trim();
                    break;
                case Section.History:
                    ParseHistoryLine(indent, trimmed);
                    break;
            }
        }

        // Flush final
        if (section == Section.Cells) FlushCell();
        if (section == Section.History) FlushHistoryItem();

        return new HostSnapshot
        {
            KernelId = KernelId ?? throw new InvalidOperationException("Missing 'kernel' field."),
            Version = version,
            TakenAt = takenAt,
            Cells = cells,
            RoutingTable = routing,
            DivisionHistory = history
        };

        // ── Nested parsers (closures over accumulators) ────────────

        void ParseCellLine(int indent, string trimmed)
        {
            // Cell name (indent 2, ends with ':')
            if (indent == 2 && trimmed.EndsWith(':') && !trimmed.Contains(' '))
            {
                FlushCell();
                currentWorkflow = trimmed[..^1];
                return;
            }

            if (currentWorkflow is null) return;

            // Cell fields (indent 4)
            if (indent == 4)
            {
                if (trimmed.StartsWith("domain:"))
                {
                    workflowDomain = trimmed["domain:".Length..].Trim();
                    currentSubSection = null;
                }
                else if (trimmed.StartsWith("divided_from:"))
                {
                    cellSplitFrom = trimmed["divided_from:".Length..].Trim();
                    currentSubSection = null;
                }
                else if (trimmed == "tools:")
                    currentSubSection = "tools";
                else if (trimmed == "models:")
                    currentSubSection = "models";
                else if (trimmed == "jobs:")
                {
                    FlushModel();
                    currentSubSection = "jobs";
                    currentBlock = null;
                }
                else if (trimmed == "connections:")
                {
                    FlushJob();
                    currentSubSection = "connections";
                    currentBlock = null;
                }
                else if (trimmed == "memory:")
                {
                    currentSubSection = "memory";
                    hasMemory = true;
                }
                return;
            }

            // Sub-items (indent 6+)
            switch (currentSubSection)
            {
                case "tools" when indent == 6 && trimmed.StartsWith("- "):
                    workflowTools.Add(trimmed[2..].Trim());
                    break;
                case "connections" when indent == 6 && trimmed.StartsWith("- "):
                    workflowConnections.Add(trimmed[2..].Trim());
                    break;
                case "models" when indent == 6 && trimmed.EndsWith(':'):
                    FlushModel();
                    currentBlock = trimmed[..^1];
                    break;
                case "models" when indent == 8 && currentBlock is not null:
                    if (trimmed.StartsWith("provider:"))
                        modelProvider = trimmed["provider:".Length..].Trim();
                    else if (trimmed.StartsWith("model:"))
                        modelName = trimmed["model:".Length..].Trim();
                    else if (trimmed.StartsWith("endpoint:"))
                        modelEndpoint = trimmed["endpoint:".Length..].Trim();
                    break;
                case "jobs" when indent == 6 && trimmed.EndsWith(':'):
                    FlushJob();
                    currentBlock = trimmed[..^1];
                    break;
                case "jobs" when indent == 8 && currentBlock is not null:
                    if (trimmed.StartsWith("type:"))
                        jobType = trimmed["type:".Length..].Trim();
                    else if (trimmed.StartsWith("model:"))
                        jobModelAlias = trimmed["model:".Length..].Trim();
                    else if (trimmed.StartsWith("max_tool_rounds:"))
                        jobMaxToolRounds = int.Parse(trimmed["max_tool_rounds:".Length..].Trim(), CultureInfo.InvariantCulture);
                    else if (trimmed.StartsWith("system_prompt:"))
                    {
                        var inline = trimmed["system_prompt:".Length..].Trim();
                        if (inline == "|")
                        {
                            inSystemPrompt = true;
                            promptLines.Clear();
                        }
                        else
                            jobSystemPrompt = inline;
                    }
                    break;
                case "memory" when indent == 6:
                    if (trimmed.StartsWith("domains:"))
                        memoryDomains.AddRange(ParseBracketList(trimmed["domains:".Length..]));
                    else if (trimmed.StartsWith("lineage:"))
                        memoryLineage.AddRange(ParseBracketList(trimmed["lineage:".Length..]));
                    break;
            }
        }

        void ParseHistoryLine(int indent, string trimmed)
        {
            if (indent == 2 && trimmed.StartsWith("- "))
            {
                FlushHistoryItem();
                inHistoryItem = true;
                var field = trimmed[2..];
                ApplyHistoryField(field);
                return;
            }

            if (inHistoryItem && indent == 4)
                ApplyHistoryField(trimmed);
        }

        void ApplyHistoryField(string field)
        {
            if (field.StartsWith("parent:"))
                histParent = field["parent:".Length..].Trim();
            else if (field.StartsWith("children:"))
                histChildren = ParseBracketList(field["children:".Length..]).ToList();
            else if (field.StartsWith("reason:"))
                histReason = UnescapeYaml(field["reason:".Length..].Trim());
            else if (field.StartsWith("occurred_at:"))
                histOccurred = DateTimeOffset.Parse(field["occurred_at:".Length..].Trim(), CultureInfo.InvariantCulture);
            else if (field.StartsWith("approved_by:"))
                histApproved = field["approved_by:".Length..].Trim();
        }
    }

    // ── Utilities ──────────────────────────────────────────────────

    private static IEnumerable<string> ParseBracketList(string value)
    {
        var trimmed = value.Trim().TrimStart('[').TrimEnd(']');
        if (string.IsNullOrWhiteSpace(trimmed)) yield break;
        foreach (var item in trimmed.Split(','))
        {
            var cleaned = item.Trim();
            if (cleaned.Length > 0)
                yield return cleaned;
        }
    }

    private static string EscapeYaml(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string UnescapeYaml(string value)
    {
        var v = value;
        if (v.StartsWith('"') && v.EndsWith('"'))
            v = v[1..^1];
        return v.Replace("\\\"", "\"").Replace("\\\\", "\\");
    }

    private enum Section { None, Cells, Routing, History }
}
