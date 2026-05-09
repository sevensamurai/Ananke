using Ananke.Design;
using Ananke.Orchestration.Execution;
using Ananke.Orchestration.Workflows;
using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Commands;

/// <summary>
/// Handles <c>nnke run &lt;file&gt;</c> — parses an <c>.ananke.yml</c> manifest,
/// validates it for local execution, builds a stub workflow from the topology,
/// and runs it through <see cref="WorkflowRunner"/> in a single shot.
/// </summary>
/// <remarks>
/// <para>
/// <b>What "run" means:</b> each job in the manifest is bound to an identity delegate
/// (pass-through) so the topology is exercised end-to-end without real LLM or tool calls.
/// This is intentional — <c>nnke run</c> exists to validate that a manifest is
/// <em>structurally</em> runnable locally, not to make live API calls.
/// </para>
/// <para>
/// If any tool in the manifest (or the selected profile) declares
/// <c>execute: platform</c>, the command fails fast with a diagnostic pointing
/// the user at <c>nnke-platform up --emulate &lt;platform&gt;</c> (FED061).
/// </para>
/// </remarks>
internal static class RunCommand
{
    /// <summary>
    /// Exit code emitted when one or more tools declare a <c>platform</c> execution mode
    /// that cannot be satisfied locally (diagnostic FED061).
    /// </summary>
    private const int ExitPlatformNative = 4;

    public static Command Create()
    {
        var fileArg = new Argument<FileInfo>("file")
        {
            Description = "Path to the .ananke.yml manifest file."
        };

        var inputOption = new Option<string?>("--input")
        {
            Description = "Initial input string threaded as workflow state."
        };

        var inputFileOption = new Option<FileInfo?>("--input-file")
        {
            Description = "Path to a file whose contents are used as the initial input string."
        };

        var profileOption = new Option<string>("--profile")
        {
            Description = "Deployment profile to resolve tool bindings from (default: local).",
            DefaultValueFactory = _ => "local"
        };

        var command = new Command("run",
            "Run an .ananke.yml workflow locally (stub execution — topology trace, no LLM calls).")
        {
            fileArg,
            inputOption,
            inputFileOption,
            profileOption
        };

        command.SetAction(async parseResult =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var input = parseResult.GetValue(inputOption);
            var inputFile = parseResult.GetValue(inputFileOption);
            var profile = parseResult.GetValue(profileOption)!;
            var json = parseResult.GetValue<bool>("--json");
            return await ExecuteAsync(file, input, inputFile, profile, json);
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(
        FileInfo file,
        string? input,
        FileInfo? inputFile,
        string profile,
        bool json)
    {
        // ── 1. Parse manifest ─────────────────────────────────────────────────
        if (!file.Exists)
        {
            Emit(json, "error", $"File not found: {file.FullName}",
                hint: "Check the file path.");
            return 1;
        }

        WorkflowManifest manifest;
        try
        {
            manifest = WorkflowManifest.Load(file.FullName);
        }
        catch (Exception ex)
        {
            Emit(json, "error", $"Failed to parse manifest: {ex.Message}",
                hint: "Run 'nnke validate <file>' for detailed diagnostics.");
            return 1;
        }

        // ── 2. Resolve input ──────────────────────────────────────────────────
        if (inputFile is not null)
        {
            if (!inputFile.Exists)
            {
                Emit(json, "error", $"Input file not found: {inputFile.FullName}");
                return 1;
            }
            input = await File.ReadAllTextAsync(inputFile.FullName);
        }

        input ??= string.Empty;

        // ── 3. PlatformNative guard (FED061) ──────────────────────────────────
        var nativeTools = ResolvePlatformNativeTools(manifest, profile);
        if (nativeTools.Count > 0)
        {
            var toolList = string.Join(", ", nativeTools);
            Emit(json, "error",
                $"Manifest declares platform-native tool(s): {toolList}. " +
                "These cannot run locally without a platform emulator.",
                hint: $"Use 'nnke-platform up --emulate <platform>' to run this manifest with platform-native capabilities.",
                diagnosticCode: "FED061");
            return ExitPlatformNative;
        }

        // ── 4. Build stub workflow ────────────────────────────────────────────
        WorkflowDefinition<string> definition;
        try
        {
            definition = BuildStubWorkflow(manifest);
        }
        catch (Exception ex)
        {
            Emit(json, "error", $"Failed to compile workflow topology: {ex.Message}",
                hint: "Run 'nnke validate <file>' for detailed diagnostics.");
            return 1;
        }

        // ── 5. Execute ────────────────────────────────────────────────────────
        var runner = new WorkflowRunner();
        var execution = await runner.RunAsync(definition, input);

        var result = execution.ToResult();

        // ── 6. Output ─────────────────────────────────────────────────────────
        if (json)
        {
            JsonOutput.Write(new
            {
                status = execution.IsSuccess ? "ok" : "error",
                workflow = manifest.Name,
                profile,
                input,
                output = execution.State,
                jobsExecuted = result.JobsExecuted,
                durationMs = (long)result.TotalDuration.TotalMilliseconds,
                jobs = result.History.Select(h => new
                {
                    name = h.JobName,
                    success = h.Success,
                    durationMs = (long)h.Duration.TotalMilliseconds,
                    error = h.Error
                }).ToList(),
                error = result.Error
            });
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine($"  Workflow : {manifest.Name}");
            Console.WriteLine($"  Profile  : {profile}");
            Console.WriteLine($"  Input    : {Truncate(input, 80)}");
            Console.WriteLine();

            foreach (var h in result.History)
            {
                var mark = h.Success ? "✓" : "✗";
                Console.WriteLine($"  {mark} {h.JobName,-30} {h.Duration.TotalMilliseconds,6:F0} ms");
                if (!h.Success && h.Error is not null)
                    Console.Error.WriteLine($"      error: {h.Error}");
            }

            Console.WriteLine();
            var execStatus = execution.IsSuccess ? "completed" : "failed";
            Console.WriteLine($"  Status   : {execStatus}  ({result.TotalDuration.TotalMilliseconds:F0} ms)");
            Console.WriteLine();
        }

        return execution.IsSuccess ? 0 : 2;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the names of manifest-declared tools that map to <c>platform</c> execution
    /// mode under the given profile. Also checks the top-level tool binding (<c>kind</c>).
    /// </summary>
    private static IReadOnlyList<string> ResolvePlatformNativeTools(
        WorkflowManifest manifest, string profile)
    {
        var native = new List<string>();

        // Check profile overrides first
        if (manifest.Profiles.TryGetValue(profile, out var profileDef))
        {
            foreach (var (toolKey, binding) in profileDef.Tools)
            {
                if (binding.Execute.Equals("platform", StringComparison.OrdinalIgnoreCase))
                    native.Add(toolKey);
            }
            // If we found overrides in the profile, that's the authoritative set
            if (native.Count > 0) return native;
        }

        // Fall back to top-level tool binding.kind == "platform"
        foreach (var (key, entry) in manifest.Tools)
        {
            if (entry.Binding.Kind?.Equals("platform", StringComparison.OrdinalIgnoreCase) == true)
                native.Add(key);
        }

        return native;
    }

    /// <summary>
    /// Builds a stub <see cref="WorkflowDefinition{TState}"/> from the manifest topology.
    /// Every job is an identity delegate; joins use the first branch; routers always
    /// route to the first available option.
    /// </summary>
    private static WorkflowDefinition<string> BuildStubWorkflow(WorkflowManifest manifest)
    {
        var scaffold = WorkflowScaffold.Parse<string>(manifest.Name, manifest.Connections);

        // Bind every unbound job as an identity pass-through
        foreach (var jobName in scaffold.UnboundJobs)
            scaffold.Bind(jobName, (state, _) => Task.FromResult(state));

        // Bind every unbound join-merge as first-branch selector
        foreach (var joinTarget in scaffold.UnboundMerges)
            scaffold.BindMerge(joinTarget, branches => branches[0]);

        // Bind every unbound router as a deterministic end-router
        foreach (var routerJob in scaffold.UnboundRouters)
            scaffold.BindRouter(routerJob, Workflow.Decide<string>(_ => Workflow.End));

        return scaffold.Build().Build();
    }

    private static void Emit(
        bool json,
        string status,
        string message,
        string? hint = null,
        string? diagnosticCode = null)
    {
        if (json)
            JsonOutput.Write(new { status, message, hint, diagnosticCode });
        else
        {
            Console.Error.WriteLine($"  {message}");
            if (hint is not null)
                Console.Error.WriteLine($"  Hint: {hint}");
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
