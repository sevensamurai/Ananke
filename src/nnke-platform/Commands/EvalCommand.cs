using Ananke.Abstractions.Providers;
using Ananke.Design;
using Ananke.Federation.Recommendation;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Tools;
using Ananke.Tool.Shared;
using System.CommandLine;
using System.Text;
using System.Text.Json;

namespace Ananke.Tool.Platform.Commands;

/// <summary>
/// Handles <c>nnke-platform eval [&lt;manifest&gt;] [--candidates …] [--weights …] [--format text|json|markdown]</c>.
/// Scores a manifest against candidate platforms and prints a sorted ranking with top reasons.
/// </summary>
internal static class EvalCommand
{
    public static Command Create()
    {
        var fileArg = new Argument<FileInfo?>("file")
        {
            Description = "Path to the .ananke.yml manifest file. When omitted the current directory is searched.",
            Arity = ArgumentArity.ZeroOrOne
        };

        var candidatesOption = new Option<string[]>("--candidates")
        {
            Description = "Restrict evaluation to these platform identifiers (e.g. azure-ai foundry claude). Defaults to all known platforms.",
            AllowMultipleArgumentsPerToken = true
        };

        var capabilityWeightOption = new Option<double?>("--weight-capability")
        {
            Description = "Weight for the capability-coverage axis (default 1.0)."
        };

        var strengthWeightOption = new Option<double?>("--weight-strength")
        {
            Description = "Weight for the strength-alignment axis (default 1.0)."
        };

        var costLatencyWeightOption = new Option<double?>("--weight-cost-latency")
        {
            Description = "Weight for the cost-and-latency axis (default 0.5)."
        };

        var governanceWeightOption = new Option<double?>("--weight-governance")
        {
            Description = "Weight for the governance-fit axis (default 1.5)."
        };

        var formatOption = new Option<string>("--format")
        {
            Description = "Output format: text (default), json, or markdown.",
            DefaultValueFactory = _ => "text"
        };

        var liveOption = new Option<bool>("--live")
        {
            Description = "Run a live structural validation pass per candidate using the local emulator registry. Errors become Block reasons; warnings become Minus reasons."
        };

        var emitRulesOption = new Option<FileInfo?>("--emit-rules")
        {
            Description = "Write the recommended platform as a HybridRouter routing-rules JSON file to the given path."
        };

        var command = new Command("eval", "Score a manifest against candidate platforms and recommend the best fit.")
        {
            fileArg,
            candidatesOption,
            capabilityWeightOption,
            strengthWeightOption,
            costLatencyWeightOption,
            governanceWeightOption,
            formatOption,
            liveOption,
            emitRulesOption
        };

        command.SetAction(async parseResult =>
        {
            var file = parseResult.GetValue(fileArg);
            var candidates = parseResult.GetValue(candidatesOption);
            var capW = parseResult.GetValue(capabilityWeightOption);
            var strW = parseResult.GetValue(strengthWeightOption);
            var clW  = parseResult.GetValue(costLatencyWeightOption);
            var govW = parseResult.GetValue(governanceWeightOption);
            var format = parseResult.GetValue(formatOption) ?? "text";
            var live = parseResult.GetValue(liveOption);
            var emitRules = parseResult.GetValue(emitRulesOption);
            var json = parseResult.GetValue<bool>("--json");

            var weights = new RecommendationWeights
            {
                CapabilityWeight  = capW  ?? 1.0,
                StrengthWeight    = strW  ?? 1.0,
                CostLatencyWeight = clW   ?? 0.5,
                GovernanceWeight  = govW  ?? 1.5
            };

            await ExecuteAsync(file, candidates, weights, json ? "json" : format, live, emitRules);
        });

        return command;
    }

    private static async Task ExecuteAsync(
        FileInfo? file,
        string[]? candidates,
        RecommendationWeights weights,
        string format,
        bool live,
        FileInfo? emitRulesFile)
    {
        var resolved = ResolveManifestFile(file);
        if (resolved is null)
        {
            var msg = file is not null
                ? $"File not found: {file.FullName}"
                : "No .ananke.yml manifest found in the current directory.";

            if (format == "json")
                JsonOutput.Write(new { status = "error", message = msg });
            else
                Console.Error.WriteLine($"  {msg}");
            return;
        }

        WorkflowManifest manifest;
        try
        {
            manifest = WorkflowManifest.Load(resolved);
        }
        catch (Exception ex)
        {
            if (format == "json")
                JsonOutput.Write(new { status = "error", message = $"Failed to parse manifest: {ex.Message}" });
            else
                Console.Error.WriteLine($"  Failed to parse manifest: {ex.Message}");
            return;
        }

        // Build a stub toolkit reflecting PlatformNative tools declared in the manifest
        var toolKit = BuildToolKitFromManifest(manifest);

        var recommender = new PlatformRecommender();
        PlatformFitReport report;

        if (live)
        {
            // Build one LocalPlatformValidator per candidate using the offline structural
            // validator (emulator registry is empty — no real executors in CLI context, so
            // only structural FED-series checks fire, which is the intended behaviour).
            var candidateList = candidates is { Length: > 0 }
                ? (IReadOnlyList<string>)candidates
                : null;

            var validators = PlatformRecommender.KnownCanonicalPlatforms(candidateList)
                .Select(p => (IPlatformValidator)new LocalPlatformValidator(emulatedPlatform: p))
                .ToList();

            report = await recommender.EvaluateWithLiveValidationAsync(
                manifest,
                toolKit,
                validators,
                candidateList,
                weights);
        }
        else
        {
            report = recommender.Evaluate(
                manifest,
                toolKit,
                candidates is { Length: > 0 } ? candidates : null,
                weights);
        }

        switch (format)
        {
            case "json":
                RenderJson(report, manifest.Name);
                break;
            case "markdown":
                RenderMarkdown(report, manifest.Name);
                break;
            default:
                RenderText(report, manifest.Name);
                break;
        }

        if (emitRulesFile is not null)
            EmitRules(report, manifest.Name, emitRulesFile, format);
    }

    // ── Emit routing rules ────────────────────────────────────────────

    /// <summary>
    /// Serialises the top recommendation as a routing-rules JSON array
    /// suitable for passing to a <c>HybridRouter</c>.
    /// </summary>
    private static void EmitRules(PlatformFitReport report, string workflowName, FileInfo dest, string format)
    {
        if (report.Recommended is null)
        {
            var msg = "No unblocked platform to emit rules for.";
            if (format == "json")
                JsonOutput.Write(new { status = "warning", message = msg });
            else
                Console.Error.WriteLine($"  ⚠ {msg}");
            return;
        }

        // Emit a single wildcard rule routing every cell to the recommended platform
        var rules = new[]
        {
            new
            {
                targetPlatform = report.Recommended,
                exactName      = (string?)null,
                prefix         = (string?)null,
                suffix         = (string?)null
            }
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(rules, options);

        try
        {
            dest.Directory?.Create();
            File.WriteAllText(dest.FullName, json);

            if (format != "json")
                Console.WriteLine($"  ✓ Routing rules written to: {dest.FullName}");
        }
        catch (Exception ex)
        {
            if (format == "json")
                JsonOutput.Write(new { status = "error", message = $"Failed to write rules: {ex.Message}" });
            else
                Console.Error.WriteLine($"  ✗ Failed to write rules: {ex.Message}");
        }
    }

    // ── Toolkit builder from manifest tools section ───────────────────

    private static ToolKit BuildToolKitFromManifest(WorkflowManifest manifest)
    {
        var kit = new ToolKit(manifest.Name);

        foreach (var (key, entry) in manifest.Tools)
        {
            var binding = entry.Binding;
            var mode = binding.Kind?.ToLowerInvariant() switch
            {
                "platform" => ToolExecutionMode.PlatformNative,
                "callback" => ToolExecutionMode.Callback,
                "mcp"      => ToolExecutionMode.Mcp,
                "openapi"  => ToolExecutionMode.OpenApi,
                _          => ToolExecutionMode.Local
            };

            kit.AddTool(new ToolDefinition
            {
                Name             = entry.Name,
                Description      = entry.Description,
                Parameters       = [],
                ExecutionMode    = mode,
                PlatformCapability = mode == ToolExecutionMode.PlatformNative ? binding.Reference : null,
                Execute          = (_, _) => Task.FromResult(ToolResult.Ok("stub"))
            });
        }

        return kit;
    }

    // ── Rendering ─────────────────────────────────────────────────────

    private static string? ResolveManifestFile(FileInfo? file)
    {
        if (file is not null)
            return file.Exists ? file.FullName : null;

        var cwd = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.ananke.yml");
        return cwd.Length > 0 ? cwd[0] : null;
    }

    private static void RenderJson(PlatformFitReport report, string workflowName)
    {
        JsonOutput.Write(new
        {
            status = "ok",
            workflow = workflowName,
            recommended = report.Recommended,
            weights = new
            {
                capability  = report.Weights.CapabilityWeight,
                strength    = report.Weights.StrengthWeight,
                costLatency = report.Weights.CostLatencyWeight,
                governance  = report.Weights.GovernanceWeight
            },
            scores = report.Scores.Select(s => new
            {
                platform           = s.Platform,
                total              = s.Total,
                capabilityCoverage = s.CapabilityCoverage,
                strengthAlignment  = s.StrengthAlignment,
                costLatencyFit     = s.CostLatencyFit,
                governanceFit      = s.GovernanceFit,
                blocked            = s.Total == 0 && s.Reasons.Any(r => r.Kind == FitReasonKind.Block),
                reasons            = s.Reasons.Select(r => new
                {
                    kind       = r.Kind.ToString().ToLowerInvariant(),
                    message    = r.Message,
                    capability = r.Capability,
                    component  = r.Component
                })
            })
        });
    }

    private static void RenderText(PlatformFitReport report, string workflowName)
    {
        Console.WriteLine();
        Console.WriteLine($"  Platform fit for: {workflowName}");
        Console.WriteLine($"  {new string('─', 61)}");
        Console.WriteLine();

        foreach (var s in report.Scores)
        {
            var blocked = s.Total == 0 && s.Reasons.Any(r => r.Kind == FitReasonKind.Block);
            var pct = (int)Math.Round(s.Total * 100);
            var bar = ProgressBar(pct);
            var badge = s.Platform == report.Recommended ? "  ★ recommended" : blocked ? "  ⚠ blocked" : "";

            Console.WriteLine($"  {s.Platform,-22} {bar}  {pct,3}%{badge}");

            foreach (var r in s.Reasons.Where(r => r.Kind == FitReasonKind.Plus).Take(3))
                Console.WriteLine($"    + {r.Message}");

            foreach (var r in s.Reasons.Where(r => r.Kind is FitReasonKind.Minus or FitReasonKind.Block).Take(3))
            {
                var prefix = r.Kind == FitReasonKind.Block ? "  ✗" : "  −";
                Console.WriteLine($"    {prefix} {r.Message}");
            }

            Console.WriteLine();
        }
    }

    private static void RenderMarkdown(PlatformFitReport report, string workflowName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Platform fit for `{workflowName}`");
        sb.AppendLine();
        sb.AppendLine("| Platform | Score | Capability | Strength | Cost/Latency | Governance | Recommended |");
        sb.AppendLine("|---|---|---|---|---|---|---|");

        foreach (var s in report.Scores)
        {
            var rec = s.Platform == report.Recommended ? "★" : "";
            sb.AppendLine($"| {s.Platform} | {s.Total:P0} | {s.CapabilityCoverage:P0} | {s.StrengthAlignment:P0} | {s.CostLatencyFit:P0} | {s.GovernanceFit:P0} | {rec} |");
        }

        sb.AppendLine();
        foreach (var s in report.Scores)
        {
            if (s.Reasons.Count == 0) continue;
            sb.AppendLine($"### {s.Platform}");
            foreach (var r in s.Reasons)
            {
                var prefix = r.Kind switch
                {
                    FitReasonKind.Plus  => "+",
                    FitReasonKind.Minus => "−",
                    _                  => "✗"
                };
                sb.AppendLine($"- {prefix} {r.Message}");
            }
            sb.AppendLine();
        }

        Console.Write(sb.ToString());
    }

    private static string ProgressBar(int pct)
    {
        const int width = 20;
        var filled = (int)Math.Round(pct / 100.0 * width);
        var empty  = width - filled;
        return $"[{new string('█', filled)}{new string('░', empty)}]";
    }
}
