using Ananke.Design;
using Ananke.Tool.Diagnostics;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Commands;

/// <summary>
/// Handles <c>nnke validate &lt;file&gt;</c> — parses an <c>.ananke.yml</c>
/// manifest and validates its topology via <see cref="WorkflowManifest"/> and
/// <see cref="WorkflowScaffold"/>. Supports <c>--json</c> for machine-readable output.
/// </summary>
internal static class ValidateCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<FileInfo>("file") { Description = "Path to the .ananke.yml manifest file." };

        var command = new Command("validate", "Parse and validate an .ananke.yml manifest.")
        {
            fileArg
        };

        command.SetAction(parseResult =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var json = parseResult.GetValue<bool>("--json");
            Execute(file, json);
        });

        return command;
    }

    private static void Execute(FileInfo file, bool json)
    {
        if (!file.Exists)
        {
            if (json)
            {
                JsonOutput.Write(new
                {
                    status = "error",
                    errors = new[]
                    {
                        new { code = "ANANKE_IO_001", message = $"File not found: {file.FullName}", hint = "Check the file path.", docsRef = (string?)null }
                    }
                });
            }
            else
            {
                Console.Error.WriteLine($"  File not found: {file.FullName}");
            }
            return;
        }

        var (manifest, diagnostics, jobCount) = RunValidation(file.FullName);

        // Output
        if (json)
        {
            WriteJson(manifest, diagnostics, jobCount);
        }
        else
        {
            WriteHuman(manifest, diagnostics, jobCount);
        }
    }

    /// <summary>Runs validation and returns the result as a serializable dictionary. Used by MCP tools.</summary>
    internal static Dictionary<string, object?> BuildJsonResult(string filePath)
    {
        if (!File.Exists(filePath))
            return new Dictionary<string, object?>
            {
                ["status"] = "error",
                ["errors"] = new[] { new { code = "ANANKE_IO_001", message = $"File not found: {filePath}", hint = "Check the file path.", docsRef = (string?)null } }
            };

        var (manifest, diagnostics, jobCount) = RunValidation(filePath);

        var status = diagnostics.Count == 0 ? "valid" : "error";
        var result = new Dictionary<string, object?>
        {
            ["status"] = status,
            ["workflow"] = manifest?.Name,
        };

        if (manifest is not null)
        {
            result["jobs"] = manifest.Jobs.ToDictionary(j => j.Key, j => j.Value.Type);
            result["models"] = manifest.Models.Keys.ToList();
            result["topology"] = new Dictionary<string, int>
            {
                ["jobCount"] = jobCount > 0 ? jobCount : manifest.Jobs.Count,
                ["connectionCount"] = manifest.Connections.Count,
            };
        }

        result["errors"] = diagnostics.Select(d => new
        {
            code = d.Code,
            message = d.Message,
            hint = d.Hint,
            docsRef = d.DocsRef,
        }).ToList();

        return result;
    }

    private static (WorkflowManifest? Manifest, List<Diagnostic> Diagnostics, int JobCount) RunValidation(string filePath)
    {
        WorkflowManifest? manifest = null;
        var diagnostics = new List<Diagnostic>();

        // Phase 1: parse manifest
        try
        {
            manifest = WorkflowManifest.Load(filePath);
        }
        catch (Exception ex)
        {
            diagnostics.Add(DiagnosticCodes.FromException(ex, "manifest"));
        }

        // Phase 2: validate model references
        if (manifest is not null)
        {
            foreach (var (jobName, job) in manifest.Jobs)
            {
                if (job.Type == "agent" && job.ModelAlias is not null &&
                    !manifest.Models.ContainsKey(job.ModelAlias))
                {
                    diagnostics.Add(new Diagnostic
                    {
                        Code = DiagnosticCodes.UndefinedModelAlias,
                        Message = $"Job '{jobName}' references model alias '{job.ModelAlias}' which is not defined in models.",
                        Hint = $"Add '{job.ModelAlias}' to the models: section, or change the job's model alias.",
                        DocsRef = "nnke docs dsl-syntax"
                    });
                }
            }
        }

        // Phase 3: validate topology
        var jobCount = 0;
        if (manifest is not null && diagnostics.Count == 0)
        {
            try
            {
                var scaffold = WorkflowScaffold.Parse<object>(manifest.Name, manifest.Connections);
                jobCount = scaffold.JobNames.Count;
            }
            catch (Exception ex)
            {
                diagnostics.Add(DiagnosticCodes.FromException(ex, "topology"));
            }
        }

        return (manifest, diagnostics, jobCount);
    }

    private static void WriteJson(WorkflowManifest? manifest, List<Diagnostic> diagnostics, int jobCount)
    {
        var status = diagnostics.Count == 0 ? "valid" : "error";

        var result = new Dictionary<string, object?>
        {
            ["status"] = status,
            ["workflow"] = manifest?.Name,
        };

        if (manifest is not null)
        {
            result["jobs"] = manifest.Jobs.ToDictionary(j => j.Key, j => j.Value.Type);
            result["models"] = manifest.Models.Keys.ToList();
            result["topology"] = new Dictionary<string, int>
            {
                ["jobCount"] = jobCount > 0 ? jobCount : manifest.Jobs.Count,
                ["connectionCount"] = manifest.Connections.Count,
            };
        }

        result["errors"] = diagnostics.Select(d => new
        {
            code = d.Code,
            message = d.Message,
            hint = d.Hint,
            docsRef = d.DocsRef,
        }).ToList();

        JsonOutput.Write(result);
    }

    private static void WriteHuman(WorkflowManifest? manifest, List<Diagnostic> diagnostics, int jobCount)
    {
        if (manifest is not null)
        {
            Console.WriteLine($"  Workflow : {manifest.Name}");
            Console.WriteLine($"  Models   : {string.Join(", ", manifest.Models.Keys)}");
            Console.WriteLine($"  Jobs     : {string.Join(", ", manifest.Jobs.Keys)}");

            var agentJobs = manifest.Jobs.Where(j => j.Value.Type == "agent").Select(j => j.Key).ToList();
            var codeJobs = manifest.Jobs.Where(j => j.Value.Type == "code").Select(j => j.Key).ToList();

            if (agentJobs.Count > 0)
                Console.WriteLine($"  Agent    : {string.Join(", ", agentJobs)}");
            if (codeJobs.Count > 0)
                Console.WriteLine($"  Code     : {string.Join(", ", codeJobs)}");
        }

        if (diagnostics.Count > 0)
        {
            Console.WriteLine();
            foreach (var d in diagnostics)
            {
                Console.Error.WriteLine($"  ✗ [{d.Code}] {d.Message}");
                Console.Error.WriteLine($"    Hint: {d.Hint}");
                if (d.DocsRef is not null)
                    Console.Error.WriteLine($"    Docs: {d.DocsRef}");
            }
        }
        else if (manifest is not null)
        {
            Console.WriteLine($"  Topology : valid ({jobCount} jobs, {manifest.Connections.Count} connections)");
            Console.WriteLine();
            Console.WriteLine("  ✓ Manifest is valid.");
        }
    }
}
