using Ananke.Design;
using Ananke.Tool.Diagnostics;
using Ananke.Tool.Output;
using System.CommandLine;
using System.Text.RegularExpressions;

namespace Ananke.Tool.Commands;

/// <summary>
/// Handles <c>nnke inspect [dir]</c> — analyzes an Ananke project directory and
/// produces a health report covering manifest, topology, NuGet dependencies,
/// provider configuration, and pattern detection.
/// Supports <c>--json</c> for agent self-diagnosis.
/// </summary>
internal static partial class InspectCommand
{
    public static Command Create()
    {
        var dirArg = new Argument<DirectoryInfo?>("directory")
        {
            Description = "Project directory to inspect. Defaults to the current directory.",
            DefaultValueFactory = _ => null,
        };

        var command = new Command("inspect", "Analyze an Ananke project directory and produce a health report.")
        {
            dirArg
        };

        command.SetAction(parseResult =>
        {
            var dir = parseResult.GetValue(dirArg) ?? new DirectoryInfo(Directory.GetCurrentDirectory());
            var json = parseResult.GetValue<bool>("--json");
            Execute(dir, json);
        });

        return command;
    }

    private static void Execute(DirectoryInfo dir, bool json)
    {
        if (!dir.Exists)
        {
            if (json)
                JsonOutput.Write(new { status = "error", message = $"Directory not found: {dir.FullName}" });
            else
                Console.Error.WriteLine($"  Directory not found: {dir.FullName}");
            return;
        }

        var report = BuildReport(dir);

        if (json)
            WriteJson(report);
        else
            WriteHuman(report);
    }

    // ── Report building ──────────────────────────────────────────────

    /// <summary>Builds and serializes the inspect report as a JSON dictionary. Used by MCP tools.</summary>
    internal static Dictionary<string, object?> BuildJsonResult(DirectoryInfo dir)
    {
        var report = BuildReport(dir);

        var status = report.Manifests.All(m => m.IsValid) && report.ManifestFiles.Count > 0
            ? "healthy" : "issues";

        var result = new Dictionary<string, object?>
        {
            ["status"] = status,
            ["projectDir"] = report.ProjectDir,
            ["csproj"] = report.CsprojFile,
            ["manifests"] = report.Manifests.Select(m => new Dictionary<string, object?>
            {
                ["file"] = m.FileName,
                ["workflow"] = m.WorkflowName,
                ["valid"] = m.IsValid,
                ["pattern"] = m.DetectedPattern,
                ["jobs"] = m.Jobs,
                ["models"] = m.Models,
                ["topology"] = new { jobCount = m.TopologyJobCount, connectionCount = m.ConnectionCount },
                ["unboundJobs"] = m.UnboundJobs.Count > 0 ? m.UnboundJobs : null,
                ["errors"] = m.Errors.Count > 0
                    ? m.Errors.Select(e => new { code = e.Code, message = e.Message, hint = e.Hint, docsRef = e.DocsRef }).ToList() as object
                    : null,
            }).ToList(),
        };

        if (report.Dependencies is not null)
        {
            result["dependencies"] = new Dictionary<string, object?>
            {
                ["packages"] = report.Dependencies.Packages.Count > 0 ? report.Dependencies.Packages : null,
                ["projectReferences"] = report.Dependencies.ProjectReferences.Count > 0 ? report.Dependencies.ProjectReferences : null,
                ["hasOrchestration"] = report.Dependencies.HasOrchestration,
                ["hasProvider"] = report.Dependencies.HasProvider,
                ["hasDesign"] = report.Dependencies.HasDesign,
                ["hasOpenTelemetry"] = report.Dependencies.HasOpenTelemetry,
            };
        }

        result["suggestions"] = report.Suggestions.Count > 0 ? report.Suggestions : null;

        return result;
    }

    private static InspectReport BuildReport(DirectoryInfo dir)
    {
        var report = new InspectReport { ProjectDir = dir.FullName };

        // Discover .ananke.yml manifests
        var manifests = dir.GetFiles("*.ananke.yml", SearchOption.TopDirectoryOnly);
        report.ManifestFiles = manifests.Select(f => f.Name).ToList();

        // Discover .csproj
        var csproj = dir.GetFiles("*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
        report.CsprojFile = csproj?.Name;

        // Analyze each manifest
        foreach (var manifestFile in manifests)
        {
            var analysis = AnalyzeManifest(manifestFile.FullName);
            report.Manifests.Add(analysis);
        }

        // Analyze NuGet dependencies from csproj
        if (csproj is not null)
            report.Dependencies = AnalyzeDependencies(csproj.FullName);

        // Detect pattern from topology
        foreach (var m in report.Manifests.Where(m => m.IsValid))
            m.DetectedPattern = DetectPattern(m);

        // Build suggestions
        report.Suggestions = BuildSuggestions(report);

        return report;
    }

    private static ManifestAnalysis AnalyzeManifest(string path)
    {
        var analysis = new ManifestAnalysis { FileName = Path.GetFileName(path) };

        // Phase 1: parse
        WorkflowManifest? manifest;
        try
        {
            manifest = WorkflowManifest.Load(path);
            analysis.WorkflowName = manifest.Name;
            analysis.Models = manifest.Models.ToDictionary(
                m => m.Key,
                m => $"{m.Value.Provider}/{m.Value.Model}");
            analysis.Jobs = manifest.Jobs.ToDictionary(
                j => j.Key,
                j => j.Value.Type);
            analysis.ConnectionCount = manifest.Connections.Count;
        }
        catch (Exception ex)
        {
            analysis.Errors.Add(DiagnosticCodes.FromException(ex, "manifest"));
            return analysis;
        }

        // Phase 2: model alias validation
        foreach (var (jobName, job) in manifest.Jobs)
        {
            if (job.Type == "agent" && job.ModelAlias is not null &&
                !manifest.Models.ContainsKey(job.ModelAlias))
            {
                analysis.Errors.Add(new Diagnostic
                {
                    Code = DiagnosticCodes.UndefinedModelAlias,
                    Message = $"Job '{jobName}' references model alias '{job.ModelAlias}' which is not defined in models.",
                    Hint = $"Add '{job.ModelAlias}' to the models: section.",
                    DocsRef = "nnke explain ANANKE_MODEL_001"
                });
            }
        }

        // Phase 3: topology validation
        try
        {
            var scaffold = WorkflowScaffold.Parse<object>(manifest.Name, manifest.Connections);
            analysis.TopologyJobCount = scaffold.JobNames.Count;
            analysis.IsValid = analysis.Errors.Count == 0;
            analysis.RawConnections = manifest.Connections;

            // Jobs declared in manifest but not referenced in connections
            var topologyJobs = scaffold.JobNames;
            analysis.UnboundJobs = manifest.Jobs.Keys
                .Where(j => !topologyJobs.Contains(j))
                .ToList();
        }
        catch (Exception ex)
        {
            analysis.Errors.Add(DiagnosticCodes.FromException(ex, "topology"));
        }

        analysis.IsValid = analysis.Errors.Count == 0;
        return analysis;
    }

    private static DependencyReport AnalyzeDependencies(string csprojPath)
    {
        var report = new DependencyReport();
        var content = File.ReadAllText(csprojPath);

        // Extract PackageReference includes and versions
        foreach (Match match in PackageRefPattern().Matches(content))
        {
            var package = match.Groups["name"].Value;
            var version = match.Groups["version"].Value;
            report.Packages[package] = string.IsNullOrEmpty(version) ? "(central)" : version;
        }

        // Extract ProjectReference includes
        foreach (Match match in ProjectRefPattern().Matches(content))
        {
            report.ProjectReferences.Add(match.Groups["path"].Value.Replace('\\', '/'));
        }

        // Check for known Ananke packages
        report.HasOrchestration = report.Packages.Keys.Any(p =>
            p.StartsWith("Ananke.Orchestration", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("Ananke", StringComparison.OrdinalIgnoreCase));

        report.HasProvider = report.Packages.Keys.Any(p =>
            p.StartsWith("Ananke.Orchestration.OpenAI", StringComparison.OrdinalIgnoreCase) ||
            p.StartsWith("Ananke.Orchestration.Anthropic", StringComparison.OrdinalIgnoreCase) ||
            p.StartsWith("Ananke.Orchestration.Google", StringComparison.OrdinalIgnoreCase));

        report.HasDesign = report.Packages.Keys.Any(p =>
            p.Equals("Ananke.Design", StringComparison.OrdinalIgnoreCase));

        report.HasOpenTelemetry = report.Packages.Keys.Any(p =>
            p.Equals("Ananke.OpenTelemetry", StringComparison.OrdinalIgnoreCase));

        // Also check ProjectReferences for the same packages
        report.HasOrchestration = report.HasOrchestration || report.ProjectReferences.Any(p =>
            p.Contains("Ananke.Orchestration", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("Ananke.csproj", StringComparison.OrdinalIgnoreCase));

        report.HasProvider = report.HasProvider || report.ProjectReferences.Any(p =>
            p.Contains("Ananke.Orchestration.OpenAI", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("Ananke.Orchestration.Anthropic", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("Ananke.Orchestration.Google", StringComparison.OrdinalIgnoreCase));

        report.HasDesign = report.HasDesign || report.ProjectReferences.Any(p =>
            p.Contains("Ananke.Design", StringComparison.OrdinalIgnoreCase));

        report.HasOpenTelemetry = report.HasOpenTelemetry || report.ProjectReferences.Any(p =>
            p.Contains("Ananke.OpenTelemetry", StringComparison.OrdinalIgnoreCase));

        return report;
    }

    private static string? DetectPattern(ManifestAnalysis manifest)
    {
        if (manifest.Jobs is null || manifest.Jobs.Count == 0 || manifest.RawConnections is null)
            return null;

        var lines = manifest.RawConnections;
        var hasFork = lines.Any(l => l.Contains("fork(", StringComparison.OrdinalIgnoreCase));
        var hasJoin = lines.Any(l => l.TrimStart().StartsWith("join(", StringComparison.OrdinalIgnoreCase));
        var hasRouter = lines.Any(l => l.Contains("router(", StringComparison.OrdinalIgnoreCase));
        var hasSubFlow = lines.Any(l => l.TrimStart().StartsWith("subflow(", StringComparison.OrdinalIgnoreCase));
        var hasInterrupt = lines.Any(l => l.TrimStart().StartsWith("interrupt(", StringComparison.OrdinalIgnoreCase));

        if (hasRouter)
            return "router";

        if (hasInterrupt)
            return "human-in-the-loop";

        if (hasSubFlow)
            return "sub-workflow";

        if (hasFork && hasJoin)
            return "etl";

        if (hasFork)
            return "fan-out";

        return "sequential";
    }

    private static List<string> BuildSuggestions(InspectReport report)
    {
        var suggestions = new List<string>();

        if (report.ManifestFiles.Count == 0)
            suggestions.Add("No .ananke.yml manifest found. Run: nnke new manifest <name>");

        if (report.CsprojFile is null)
            suggestions.Add("No .csproj found. Run: nnke new workflow <name>");

        if (report.Dependencies is not null)
        {
            if (!report.Dependencies.HasOrchestration)
                suggestions.Add("Missing Ananke.Orchestration package. Run: dotnet add package Ananke.Orchestration");

            if (!report.Dependencies.HasProvider)
                suggestions.Add("No LLM provider package detected. Run: dotnet add package Ananke.Orchestration.OpenAI");

            if (!report.Dependencies.HasDesign && report.ManifestFiles.Count > 0)
                suggestions.Add("Manifest found but Ananke.Design is not referenced. Add: dotnet add package Ananke.Design");

            if (!report.Dependencies.HasOpenTelemetry)
                suggestions.Add("Consider adding Ananke.OpenTelemetry for workflow observability.");
        }

        foreach (var m in report.Manifests)
        {
            if (m.UnboundJobs.Count > 0)
                suggestions.Add(
                    $"Manifest '{m.FileName}': jobs [{string.Join(", ", m.UnboundJobs)}] declared but not referenced in connections.");
        }

        // Check for secrets.json
        var secretsPath = Path.Combine(report.ProjectDir, "secrets.json");
        if (report.Dependencies?.HasProvider == true && !File.Exists(secretsPath))
            suggestions.Add("Provider package detected but no secrets.json found. API keys may be missing at runtime.");

        return suggestions;
    }

    // ── Output ───────────────────────────────────────────────────────

    private static void WriteJson(InspectReport report)
    {
        // Build the result dict using the same logic exposed for MCP
        // We need the report already built, but BuildJsonResult takes a dir.
        // Just serialize directly here since WriteJson already has the report.
        var status = report.Manifests.All(m => m.IsValid) && report.ManifestFiles.Count > 0
            ? "healthy" : "issues";

        var result = new Dictionary<string, object?>
        {
            ["status"] = status,
            ["projectDir"] = report.ProjectDir,
            ["csproj"] = report.CsprojFile,
            ["manifests"] = report.Manifests.Select(m => new Dictionary<string, object?>
            {
                ["file"] = m.FileName,
                ["workflow"] = m.WorkflowName,
                ["valid"] = m.IsValid,
                ["pattern"] = m.DetectedPattern,
                ["jobs"] = m.Jobs,
                ["models"] = m.Models,
                ["topology"] = new { jobCount = m.TopologyJobCount, connectionCount = m.ConnectionCount },
                ["unboundJobs"] = m.UnboundJobs.Count > 0 ? m.UnboundJobs : null,
                ["errors"] = m.Errors.Count > 0
                    ? m.Errors.Select(e => new { code = e.Code, message = e.Message, hint = e.Hint, docsRef = e.DocsRef }).ToList() as object
                    : null,
            }).ToList(),
        };

        if (report.Dependencies is not null)
        {
            result["dependencies"] = new Dictionary<string, object?>
            {
                ["packages"] = report.Dependencies.Packages.Count > 0 ? report.Dependencies.Packages : null,
                ["projectReferences"] = report.Dependencies.ProjectReferences.Count > 0 ? report.Dependencies.ProjectReferences : null,
                ["hasOrchestration"] = report.Dependencies.HasOrchestration,
                ["hasProvider"] = report.Dependencies.HasProvider,
                ["hasDesign"] = report.Dependencies.HasDesign,
                ["hasOpenTelemetry"] = report.Dependencies.HasOpenTelemetry,
            };
        }

        result["suggestions"] = report.Suggestions.Count > 0 ? report.Suggestions : null;

        JsonOutput.Write(result);
    }

    private static void WriteHuman(InspectReport report)
    {
        Console.WriteLine($"  Project: {report.ProjectDir}");
        Console.WriteLine("  ─────────────────────────────────────────────────");

        if (report.CsprojFile is not null)
            Console.WriteLine($"  Project file: {report.CsprojFile}");

        if (report.ManifestFiles.Count == 0)
        {
            Console.WriteLine("  No .ananke.yml manifests found.");
        }
        else
        {
            Console.WriteLine();
            foreach (var m in report.Manifests)
            {
                Console.WriteLine($"  Manifest: {m.FileName}");
                if (m.WorkflowName is not null)
                    Console.WriteLine($"    Workflow : {m.WorkflowName}");
                if (m.Jobs is not null)
                    Console.WriteLine($"    Jobs     : {string.Join(", ", m.Jobs.Select(j => $"{j.Key} ({j.Value})"))}");
                if (m.Models is not null)
                    Console.WriteLine($"    Models   : {string.Join(", ", m.Models.Select(m2 => $"{m2.Key} → {m2.Value}"))}");
                if (m.TopologyJobCount > 0)
                    Console.WriteLine($"    Topology : {m.TopologyJobCount} jobs, {m.ConnectionCount} connections");
                if (m.DetectedPattern is not null)
                    Console.WriteLine($"    Pattern  : {m.DetectedPattern}");
                if (m.UnboundJobs.Count > 0)
                    Console.WriteLine($"    Unbound  : {string.Join(", ", m.UnboundJobs)}");

                if (m.IsValid)
                {
                    Console.WriteLine("    ✓ Valid");
                }
                else
                {
                    foreach (var e in m.Errors)
                    {
                        Console.Error.WriteLine($"    ✗ [{e.Code}] {e.Message}");
                        Console.Error.WriteLine($"      Hint: {e.Hint}");
                    }
                }

                Console.WriteLine();
            }
        }

        // Dependencies
        if (report.Dependencies is not null)
        {
            Console.WriteLine("  Dependencies:");
            if (report.Dependencies.Packages.Count > 0)
            {
                foreach (var (pkg, ver) in report.Dependencies.Packages)
                    Console.WriteLine($"    {pkg,-45} {ver}");
            }
            if (report.Dependencies.ProjectReferences.Count > 0)
            {
                foreach (var pr in report.Dependencies.ProjectReferences)
                    Console.WriteLine($"    → {pr}");
            }
            Console.WriteLine();
        }

        // Suggestions
        if (report.Suggestions.Count > 0)
        {
            Console.WriteLine("  Suggestions:");
            foreach (var s in report.Suggestions)
                Console.WriteLine($"    • {s}");
            Console.WriteLine();
        }

        var healthy = report.Manifests.All(m => m.IsValid) && report.ManifestFiles.Count > 0;
        Console.WriteLine(healthy ? "  ✓ Project is healthy." : "  ✗ Issues found.");
    }

    // ── Regex patterns ───────────────────────────────────────────────

    [GeneratedRegex("""<PackageReference\s+Include="(?<name>[^"]+)"(\s+Version="(?<version>[^"]+)")?""")]
    private static partial Regex PackageRefPattern();

    [GeneratedRegex("""<ProjectReference\s+Include="(?<path>[^"]+)".*?/>""")]
    private static partial Regex ProjectRefPattern();

    // ── Report models ────────────────────────────────────────────────

    private sealed class InspectReport
    {
        public string ProjectDir { get; set; } = "";
        public string? CsprojFile { get; set; }
        public List<string> ManifestFiles { get; set; } = [];
        public List<ManifestAnalysis> Manifests { get; } = [];
        public DependencyReport? Dependencies { get; set; }
        public List<string> Suggestions { get; set; } = [];
    }

    private sealed class ManifestAnalysis
    {
        public string FileName { get; set; } = "";
        public string? WorkflowName { get; set; }
        public bool IsValid { get; set; }
        public string? DetectedPattern { get; set; }
        public Dictionary<string, string>? Jobs { get; set; }
        public Dictionary<string, string>? Models { get; set; }
        public int TopologyJobCount { get; set; }
        public int ConnectionCount { get; set; }
        public List<string>? RawConnections { get; set; }
        public List<string> UnboundJobs { get; set; } = [];
        public List<Diagnostic> Errors { get; } = [];
    }

    private sealed class DependencyReport
    {
        public Dictionary<string, string> Packages { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ProjectReferences { get; } = [];
        public bool HasOrchestration { get; set; }
        public bool HasProvider { get; set; }
        public bool HasDesign { get; set; }
        public bool HasOpenTelemetry { get; set; }
    }
}
