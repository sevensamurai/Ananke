using System.Text.Json;
using System.Text.Json.Nodes;
using Ananke.Tool.Diagnostics;
using Ananke.Tool.Docs;
using Ananke.Tool.Patterns;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Ananke.Tool.Mcp;

/// <summary>
/// Builds <see cref="McpServerTool"/> instances that wrap existing <c>nnke</c> command logic.
/// Each tool re-uses the same code paths as the CLI commands, ensuring output parity.
/// All tools return JSON (the MCP equivalent of <c>--json</c>).
/// </summary>
internal static class McpToolRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Returns all nnke MCP tools.
    /// </summary>
    public static IReadOnlyList<McpServerTool> CreateAll() =>
    [
        CreateDocsSearch(),
        CreateDocsRead(),
        CreateDocsList(),
        CreateExplain(),
        CreateExplainList(),
        CreatePatternsList(),
        CreatePatternsDescribe(),
        CreateInspect(),
        CreateValidate(),
        CreateScaffold(),
        CreateMeshStatus(),
        CreateCellTrace(),
        CreateMemoryInspect(),
        CreateLineage(),
    ];

    // ── ananke_docs_search ───────────────────────────────────────────

    private static McpServerTool CreateDocsSearch() => McpServerTool.Create(
        (string query) =>
        {
            var results = DocsProvider.Search(query);
            return Serialize(new
            {
                status = "ok",
                count = results.Count,
                results = results.Select(r => new
                {
                    topic = r.Topic.Key,
                    category = r.Topic.Category,
                    title = r.Topic.Title,
                    section = r.Section,
                    snippet = r.Snippet
                }).ToList()
            });
        },
        new()
        {
            Name = "ananke_docs_search",
            Description = "Search Ananke framework documentation for the given terms. Returns matching topic/section/snippet.",
            Title = "Search Ananke docs"
        });

    // ── ananke_docs_read ─────────────────────────────────────────────

    private static McpServerTool CreateDocsRead() => McpServerTool.Create(
        (string topic) =>
        {
            var found = DocsProvider.FindTopic(topic);
            if (found is null)
                return Serialize(new { status = "not_found", topic, hint = "Use ananke_docs_list to see available topics." });

            var content = DocsProvider.ReadContent(found);
            var sections = DocsProvider.ExtractSections(content);
            return Serialize(new
            {
                status = "ok",
                topic = found.Key,
                title = found.Title,
                category = found.Category,
                sections,
                content
            });
        },
        new()
        {
            Name = "ananke_docs_read",
            Description = "Read the full content of an Ananke documentation topic. Use 'ananke_docs_list' to discover topic keys.",
            Title = "Read Ananke doc topic"
        });

    // ── ananke_docs_list ─────────────────────────────────────────────

    private static McpServerTool CreateDocsList() => McpServerTool.Create(
        () =>
        {
            var topics = DocsProvider.ListTopics();
            return Serialize(new
            {
                status = "ok",
                count = topics.Count,
                topics = topics.Select(t => new
                {
                    key = t.Key,
                    category = t.Category,
                    title = t.Title,
                    path = t.RelativePath
                }).ToList()
            });
        },
        new()
        {
            Name = "ananke_docs_list",
            Description = "List all available Ananke framework documentation topics with their keys and categories.",
            Title = "List Ananke doc topics"
        });

    // ── ananke_explain ───────────────────────────────────────────────

    private static McpServerTool CreateExplain() => McpServerTool.Create(
        (string code) =>
        {
            var explanation = DiagnosticExplanations.Find(code);
            if (explanation is null)
                return Serialize(new { status = "not_found", code, hint = "Use ananke_explain_list to see all diagnostic codes." });

            return Serialize(new
            {
                status = "ok",
                code = explanation.Code,
                title = explanation.Title,
                description = explanation.Description,
                badExample = explanation.BadExample,
                fixExample = explanation.FixExample,
                docsRef = explanation.DocsRef
            });
        },
        new()
        {
            Name = "ananke_explain",
            Description = "Explain an Ananke diagnostic error code (e.g. ANANKE_TOPO_003) with examples and fix guidance.",
            Title = "Explain Ananke error code"
        });

    // ── ananke_explain_list ──────────────────────────────────────────

    private static McpServerTool CreateExplainList() => McpServerTool.Create(
        () =>
        {
            var all = DiagnosticExplanations.All();
            return Serialize(new
            {
                status = "ok",
                count = all.Count,
                codes = all.Select(e => new
                {
                    code = e.Code,
                    title = e.Title,
                    docsRef = e.DocsRef
                }).ToList()
            });
        },
        new()
        {
            Name = "ananke_explain_list",
            Description = "List all Ananke diagnostic error codes with their titles.",
            Title = "List Ananke error codes"
        });

    // ── ananke_patterns_list ─────────────────────────────────────────

    private static McpServerTool CreatePatternsList() => McpServerTool.Create(
        () =>
        {
            var all = PatternCatalog.All();
            return Serialize(new
            {
                status = "ok",
                count = all.Count,
                manifestPatterns = PatternCatalog.ManifestPatterns().Select(p => new
                {
                    key = p.Key,
                    title = p.Title,
                    topology = p.Topology
                }).ToList(),
                agenticPatterns = PatternCatalog.AgenticPatterns().Select(p => new
                {
                    key = p.Key,
                    title = p.Title,
                    topology = p.Topology
                }).ToList()
            });
        },
        new()
        {
            Name = "ananke_patterns_list",
            Description = "List all recognized Ananke workflow and agentic patterns with their topologies.",
            Title = "List Ananke patterns"
        });

    // ── ananke_patterns_describe ─────────────────────────────────────

    private static McpServerTool CreatePatternsDescribe() => McpServerTool.Create(
        (string pattern) =>
        {
            var entry = PatternCatalog.Find(pattern);
            if (entry is null)
                return Serialize(new { status = "not_found", pattern, hint = "Use ananke_patterns_list to see all patterns." });

            return Serialize(new
            {
                status = "ok",
                key = entry.Key,
                title = entry.Title,
                style = entry.Style,
                topology = entry.Topology,
                dslEquivalent = entry.DslEquivalent,
                apiEntryPoint = entry.ApiEntryPoint,
                useCases = entry.UseCases,
                scaffoldCommand = entry.ScaffoldCommand,
                docsRef = entry.DocsRef,
                description = entry.Description.Trim(),
                apiExample = entry.ApiExample.Trim()
            });
        },
        new()
        {
            Name = "ananke_patterns_describe",
            Description = "Describe an Ananke pattern in detail: topology, API entry point, use cases, scaffold command, and code example.",
            Title = "Describe Ananke pattern"
        });

    // ── ananke_inspect ───────────────────────────────────────────────

    private static McpServerTool CreateInspect() => McpServerTool.Create(
        (string directory) =>
        {
            var dir = string.IsNullOrWhiteSpace(directory)
                ? new DirectoryInfo(Directory.GetCurrentDirectory())
                : new DirectoryInfo(directory);

            if (!dir.Exists)
                return Serialize(new { status = "error", message = $"Directory not found: {dir.FullName}" });

            var result = Commands.InspectCommand.BuildJsonResult(dir);
            return Serialize(result);
        },
        new()
        {
            Name = "ananke_inspect",
            Description = "Analyze an Ananke project directory and produce a health report covering manifests, topology, NuGet dependencies, pattern detection, and suggestions. Pass the project directory path.",
            Title = "Inspect Ananke project"
        });

    // ── ananke_validate ──────────────────────────────────────────────

    private static McpServerTool CreateValidate() => McpServerTool.Create(
        (string file) =>
        {
            if (string.IsNullOrWhiteSpace(file))
                return Serialize(new { status = "error", message = "File path is required." });

            var result = Commands.ValidateCommand.BuildJsonResult(file);
            return Serialize(result);
        },
        new()
        {
            Name = "ananke_validate",
            Description = "Validate an .ananke.yml manifest file for syntax, model references, and topology errors. Returns structured diagnostics.",
            Title = "Validate Ananke manifest"
        });

    // ── ananke_scaffold ──────────────────────────────────────────────

    private static McpServerTool CreateScaffold() => McpServerTool.Create(
        (string templateName, string outputDirectory, string? projectName, string? provider) =>
        {
            var name = projectName ?? templateName;
            var prov = provider ?? "openai";
            var outDir = new DirectoryInfo(string.IsNullOrWhiteSpace(outputDirectory)
                ? Path.Combine(Directory.GetCurrentDirectory(), name)
                : outputDirectory);

            var captured = new List<string>();
            var skipped = new List<string>();

            try
            {
                Commands.NewWorkflowCommand.ExecuteForMcp(name, prov, templateName, outDir, captured, skipped);
            }
            catch (Exception ex)
            {
                return Serialize(new { status = "error", message = ex.Message });
            }

            return Serialize(new
            {
                status = "created",
                template = templateName,
                projectDir = outDir.FullName,
                files = captured,
                skipped = skipped.Count > 0 ? skipped : null
            });
        },
        new()
        {
            Name = "ananke_scaffold",
            Description = "Scaffold a new Ananke project from a template. Templates: workflow, streaming-chat, organic-host, review-critique, iterative-refinement, router, human-in-the-loop, handoff.",
            Title = "Scaffold Ananke project"
        });

    // ── ananke_mesh_status ─────────────────────────────────────────

    private static McpServerTool CreateMeshStatus() => McpServerTool.Create(
        (string snapshotFile) =>
        {
            if (!File.Exists(snapshotFile))
                return Serialize(new { status = "error", message = $"Snapshot file not found: {snapshotFile}" });
            try
            {
                var snap = Ananke.Organics.Kernel.Snapshots.HostSnapshotExporter
                    .FromYaml(File.ReadAllText(snapshotFile));
                return Serialize(new
                {
                    status = "ok",
                    kernelId = snap.KernelId,
                    version = snap.Version,
                    takenAt = snap.TakenAt,
                    cellCount = snap.Cells.Count,
                    cells = snap.Cells.Select(c => new
                    {
                        name = c.Name, domain = c.Domain, splitFrom = c.SplitFrom,
                        toolCount = c.Tools.Count, tools = c.Tools,
                        models = c.Models.Keys.ToList(), memoryDomains = c.MemoryProfile?.Domains
                    }),
                    routing = snap.RoutingTable
                });
            }
            catch (Exception ex) { return Serialize(new { status = "error", message = ex.Message }); }
        },
        new()
        {
            Name = "ananke_mesh_status",
            Description = "Show alive cells, domains, tools, models, and routing from a host snapshot YAML file.",
            Title = "Mesh cell status"
        });

    // ── ananke_cell_trace ────────────────────────────────────────────

    private static McpServerTool CreateCellTrace() => McpServerTool.Create(
        (string cellName, string snapshotFile) =>
        {
            if (!File.Exists(snapshotFile))
                return Serialize(new { status = "error", message = $"Snapshot file not found: {snapshotFile}" });
            try
            {
                var snap = Ananke.Organics.Kernel.Snapshots.HostSnapshotExporter
                    .FromYaml(File.ReadAllText(snapshotFile));
                var cell = snap.Cells.FirstOrDefault(c =>
                    c.Name.Equals(cellName, StringComparison.OrdinalIgnoreCase));
                if (cell is null)
                    return Serialize(new { status = "not_found", cellName,
                        available = snap.Cells.Select(c => c.Name).ToList() });
                var divs = snap.DivisionHistory
                    .Where(d => d.ParentWorkflow.Equals(cellName, StringComparison.OrdinalIgnoreCase)
                             || d.Children.Contains(cellName, StringComparer.OrdinalIgnoreCase))
                    .ToList();
                return Serialize(new
                {
                    status = "ok", name = cell.Name, domain = cell.Domain, splitFrom = cell.SplitFrom,
                    tools = cell.Tools, jobs = cell.Jobs.Keys.ToList(),
                    models = cell.Models.Select(m => new { alias = m.Key, provider = m.Value.Provider, model = m.Value.Model }),
                    memoryDomains = cell.MemoryProfile?.Domains,
                    divisionEvents = divs.Select(d => new { at = d.OccurredAt, parent = d.ParentWorkflow, children = d.Children, reason = d.Reason })
                });
            }
            catch (Exception ex) { return Serialize(new { status = "error", message = ex.Message }); }
        },
        new()
        {
            Name = "ananke_cell_trace",
            Description = "Show tool, job, model, and division history details for a specific cell in a host snapshot.",
            Title = "Trace cell details"
        });

    // ── ananke_memory_inspect ────────────────────────────────────────

    private static McpServerTool CreateMemoryInspect() => McpServerTool.Create(
        (string memoryFile, string? entityId, string? kind, int top) =>
        {
            if (!File.Exists(memoryFile))
                return Serialize(new { status = "error", message = $"Memory file not found: {memoryFile}" });
            try
            {
                var entries = System.Text.Json.JsonSerializer.Deserialize<
                    List<Ananke.Learning.EmpiricalMemory.EmpiricalEntry>>(
                    File.ReadAllText(memoryFile),
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
                var filtered = entries.AsEnumerable();
                if (entityId is not null)
                    filtered = filtered.Where(e => e.EntityId?.Equals(entityId, StringComparison.OrdinalIgnoreCase) == true);
                if (kind is not null && Enum.TryParse<Ananke.Learning.EmpiricalMemory.EmpiricalKind>(kind, true, out var k))
                    filtered = filtered.Where(e => e.Kind == k);
                var effectiveTop = top > 0 ? top : 20;
                var all = filtered.ToList();
                var page = all.OrderByDescending(e => e.Confidence).Take(effectiveTop).ToList();
                return Serialize(new
                {
                    status = "ok", total = all.Count, showing = page.Count,
                    entries = page.Select(e => new
                    {
                        id = e.Id, kind = e.Kind.ToString().ToLowerInvariant(),
                        entityId = e.EntityId, tags = e.Tags, confidence = e.Confidence,
                        observations = e.ObservationCount, summary = e.Description.Summary, lastObserved = e.LastObserved
                    })
                });
            }
            catch (Exception ex) { return Serialize(new { status = "error", message = ex.Message }); }
        },
        new()
        {
            Name = "ananke_memory_inspect",
            Description = "Browse entries from an exported empirical memory JSON file. Filter by entityId (cell name) and kind (pattern, skill, heuristic).",
            Title = "Inspect empirical memory"
        });

    // ── ananke_lineage ───────────────────────────────────────────────

    private static McpServerTool CreateLineage() => McpServerTool.Create(
        (string cellName, string snapshotFile) =>
        {
            if (!File.Exists(snapshotFile))
                return Serialize(new { status = "error", message = $"File not found: {snapshotFile}" });
            try
            {
                var snap = Ananke.Organics.Kernel.Snapshots.HostSnapshotExporter
                    .FromYaml(File.ReadAllText(snapshotFile));
                var lineageMap = snap.Cells.ToDictionary(c => c.Name, c => c.SplitFrom, StringComparer.OrdinalIgnoreCase);

                List<string> Ancestors(string id)
                {
                    var chain = new List<string>();
                    var cur = id;
                    while (cur is not null)
                    {
                        chain.Insert(0, cur);
                        cur = lineageMap.TryGetValue(cur, out var p) ? p : null;
                    }
                    return chain;
                }

                List<string> Descendants(string id)
                {
                    var result = new List<string>();
                    foreach (var (k, v) in lineageMap)
                        if (v?.Equals(id, StringComparison.OrdinalIgnoreCase) == true)
                        { result.Add(k); result.AddRange(Descendants(k)); }
                    return result;
                }

                if (!lineageMap.ContainsKey(cellName))
                    return Serialize(new { status = "not_found", cellName, available = lineageMap.Keys.ToList() });

                return Serialize(new { status = "ok", cell = cellName,
                    ancestors = Ancestors(cellName), descendants = Descendants(cellName) });
            }
            catch (Exception ex) { return Serialize(new { status = "error", message = ex.Message }); }
        },
        new()
        {
            Name = "ananke_lineage",
            Description = "Show the ancestor chain and all descendants for a named cell in a host snapshot.",
            Title = "Cell lineage tree"
        });

    // ── Helpers ───────────────────────────────────────────────────────

    private static string Serialize(object value) =>
        JsonSerializer.Serialize(value, JsonOptions);
}
