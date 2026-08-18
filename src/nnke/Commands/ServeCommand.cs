using Ananke.Design;
using Ananke.Orchestration.Execution;
using Ananke.Orchestration.Streaming;
using Ananke.Orchestration.Workflows;
using Ananke.Tool.Shared;
using System.CommandLine;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ananke.Tool.Commands;

/// <summary>
/// Handles <c>nnke serve &lt;file&gt;</c> — parses an <c>.ananke.yml</c> manifest,
/// builds a stub workflow, and exposes it over HTTP on <c>localhost:&lt;port&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>HTTP interface:</b>
/// <list type="bullet">
///   <item>
///     <c>POST /run</c> with a plain-text or JSON body → runs the workflow and streams
///     job events as newline-delimited JSON (NDJSON) back to the caller.
///   </item>
///   <item>
///     <c>GET /health</c> → returns <c>{"status":"ok","workflow":"&lt;name&gt;"}</c>.
///   </item>
/// </list>
/// </para>
/// <para>
/// A platform-native guard applies: any tool declaring <c>execute: platform</c> causes
/// a fail-fast before the server starts (FED061), with a hint pointing at
/// <c>nnke-platform</c>, which owns the platform-emulated dev loop (ADR CLI-7).
/// </para>
/// <para>
/// This is a developer / local-testing endpoint — it binds only to <c>localhost</c>
/// and is not intended for production traffic.
/// </para>
/// </remarks>
internal static class ServeCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static Command Create()
    {
        var fileArg = new Argument<FileInfo>("file")
        {
            Description = "Path to the .ananke.yml manifest file."
        };

        var portOption = new Option<int>("--port")
        {
            Description = "TCP port to listen on (default: 5000).",
            DefaultValueFactory = _ => 5000
        };

        var profileOption = new Option<string>("--profile")
        {
            Description = "Deployment profile to resolve tool bindings from (default: local).",
            DefaultValueFactory = _ => "local"
        };

        var command = new Command("serve",
            "Run an .ananke.yml workflow as a local HTTP server on localhost (stub execution).")
        {
            fileArg,
            portOption,
            profileOption
        };

        command.SetAction(async (parseResult, ct) =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var port = parseResult.GetValue(portOption);
            var profile = parseResult.GetValue(profileOption)!;
            var json = parseResult.GetValue<bool>("--json");
            return await ExecuteAsync(file, port, profile, json, ct);
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(
        FileInfo file,
        int port,
        string profile,
        bool json,
        CancellationToken ct)
    {
        // ── 1. Parse manifest ─────────────────────────────────────────────────
        if (!file.Exists)
        {
            Emit(json, "error", $"File not found: {file.FullName}", hint: "Check the file path.");
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

        // ── 2. PlatformNative guard (FED061) ──────────────────────────────────
        var nativeTools = ResolvePlatformNativeTools(manifest, profile);
        if (nativeTools.Count > 0)
        {
            var toolList = string.Join(", ", nativeTools);
            Emit(json, "error",
                $"Manifest declares platform-native tool(s): {toolList}. " +
                "These cannot run locally without a platform emulator.",
                hint: "Use 'nnke-platform up --emulate <platform>' to run this manifest with platform-native capabilities.",
                diagnosticCode: "FED061");
            return 4;
        }

        // ── 3. Compile stub workflow ──────────────────────────────────────────
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

        // ── 4. Start HTTP server ──────────────────────────────────────────────
        var prefix = $"http://localhost:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            Emit(json, "error", $"Failed to start HTTP listener on port {port}: {ex.Message}",
                hint: "Try a different port with --port <n>.");
            return 1;
        }

        if (json)
            JsonOutput.Write(new { status = "listening", workflow = manifest.Name, profile, url = prefix });
        else
        {
            Console.WriteLine();
            Console.WriteLine($"  Workflow : {manifest.Name}");
            Console.WriteLine($"  Profile  : {profile}");
            Console.WriteLine($"  Serving  : {prefix}");
            Console.WriteLine();
            Console.WriteLine("  POST /run   — run workflow (body = input string)");
            Console.WriteLine("  GET  /health — health check");
            Console.WriteLine();
            Console.WriteLine("  Press Ctrl+C to stop.");
            Console.WriteLine();
        }

        await using var ctRegistration = ct.Register(() => listener.Stop());

        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested)
            {
                break;
            }

            _ = HandleRequestAsync(context, definition, manifest.Name, ct);
        }

        if (!json)
            Console.WriteLine("  Server stopped.");

        return 0;
    }

    // ── Request handling ──────────────────────────────────────────────────────

    private static async Task HandleRequestAsync(
        HttpListenerContext context,
        WorkflowDefinition<string> definition,
        string workflowName,
        CancellationToken ct)
    {
        var req = context.Request;
        var resp = context.Response;

        try
        {
            var path = req.Url?.AbsolutePath ?? "/";

            if (req.HttpMethod == "GET" && path == "/health")
            {
                await WriteJsonResponseAsync(resp, 200,
                    new { status = "ok", workflow = workflowName });
                return;
            }

            if (req.HttpMethod == "POST" && path == "/run")
            {
                await HandleRunAsync(req, resp, definition, ct);
                return;
            }

            await WriteJsonResponseAsync(resp, 404,
                new { status = "error", message = $"Unknown endpoint: {req.HttpMethod} {path}" });
        }
        catch (Exception ex)
        {
            try
            {
                await WriteJsonResponseAsync(resp, 500,
                    new { status = "error", message = ex.Message });
            }
            catch { /* response already started */ }
        }
    }

    private static async Task HandleRunAsync(
        HttpListenerRequest req,
        HttpListenerResponse resp,
        WorkflowDefinition<string> definition,
        CancellationToken ct)
    {
        // Read input from request body
        string input;
        using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
            input = await reader.ReadToEndAsync(ct);

        // Stream NDJSON events
        resp.StatusCode = 200;
        resp.ContentType = "application/x-ndjson; charset=utf-8";
        resp.SendChunked = true;

        var runner = new WorkflowRunner();
        var options = new WorkflowStreamOptions();
        await using var writer = new StreamWriter(resp.OutputStream, Encoding.UTF8, leaveOpen: true);

        await foreach (var evt in runner.StreamAsync(definition, input, options, ct))
        {
            var line = JsonSerializer.Serialize(MapEvent(evt), JsonOptions);
            await writer.WriteLineAsync(line.AsMemory(), ct);
            await writer.FlushAsync(ct);
        }
    }

    private static object MapEvent<TState>(WorkflowEvent<TState> evt) => evt switch
    {
        JobStarted<TState> e => new { type = "job_started", job = e.JobName },
        JobCompleted<TState> e => new
        {
            type = "job_completed",
            job = e.JobName,
            durationMs = (long)e.Duration.TotalMilliseconds
        },
        ForkStarted<TState> e => new { type = "fork_started", targets = e.Targets },
        JoinCompleted<TState> e => new { type = "join_completed", target = e.Target },
        WorkflowCompleted<TState> e => new
        {
            type = "completed",
            durationMs = (long)e.Result.TotalDuration.TotalMilliseconds
        },
        WorkflowFaulted<TState> e => new { type = "faulted", error = e.Exception.Message },
        _ => new { type = "event" }
    };

    // ── Shared helpers ────────────────────────────────────────────────────────

    private static IReadOnlyList<string> ResolvePlatformNativeTools(
        WorkflowManifest manifest, string profile)
    {
        var native = new List<string>();

        if (manifest.Profiles.TryGetValue(profile, out var profileDef))
        {
            foreach (var (toolKey, binding) in profileDef.Tools)
            {
                if (binding.Execute.Equals("platform", StringComparison.OrdinalIgnoreCase))
                    native.Add(toolKey);
            }
            if (native.Count > 0) return native;
        }

        foreach (var (key, entry) in manifest.Tools)
        {
            if (entry.Binding.Kind?.Equals("platform", StringComparison.OrdinalIgnoreCase) == true)
                native.Add(key);
        }

        return native;
    }

    private static WorkflowDefinition<string> BuildStubWorkflow(WorkflowManifest manifest)
    {
        var scaffold = WorkflowScaffold.Parse<string>(manifest.Name, manifest.Connections);

        foreach (var jobName in scaffold.UnboundJobs)
            scaffold.Bind(jobName, (state, _) => Task.FromResult(state));

        foreach (var joinTarget in scaffold.UnboundMerges)
            scaffold.BindMerge(joinTarget, branches => branches[0]);

        foreach (var routerJob in scaffold.UnboundRouters)
            scaffold.BindRouter(routerJob, Workflow.Decide<string>(_ => Workflow.End));

        return scaffold.Build().Build();
    }

    private static async Task WriteJsonResponseAsync(
        HttpListenerResponse resp, int statusCode, object body)
    {
        resp.StatusCode = statusCode;
        resp.ContentType = "application/json; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body, JsonOptions));
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes.AsMemory());
        resp.OutputStream.Close();
    }

    private static void Emit(
        bool json, string status, string message,
        string? hint = null, string? diagnosticCode = null)
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
}
