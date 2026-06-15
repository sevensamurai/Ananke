using System.Diagnostics;
using System.Text;
using Ananke.Orchestration.Tools;
using Ananke.Federation.Execution;

namespace Ananke.Federation.LocalEmulators;

/// <summary>
/// Real emulator for the <c>bash</c> capability (Claude, Vertex AI / Gemini Enterprise).
/// Executes shell commands in a sandboxed temporary directory via
/// <c>bash</c> on Linux/macOS or <c>cmd</c> on Windows.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Privilege caveat:</strong> commands execute with the same OS privileges
/// as the host process. There is no process-level isolation — a command can read,
/// write, or delete any file the host process can access. Only enable
/// <see cref="AllowUnsafeBash"/> when you control the command source and have
/// accepted the associated risk (e.g. a supervised local design-loop, never in
/// production or multi-tenant environments).
/// </para>
/// <para>
/// The sandbox directory is created on construction and deleted automatically
/// when <see cref="Dispose"/> is called.
/// </para>
/// </remarks>
internal sealed class BashExecutor : IPlatformNativeExecutor, IDisposable
{
    private readonly string _sandboxRoot;
    private readonly int _timeoutSeconds;
    private bool _disposed;

    /// <summary>
    /// Initialises a new <see cref="BashExecutor"/>.
    /// </summary>
    /// <param name="sandboxRoot">
    /// Working directory for all commands. A temporary directory is created
    /// automatically when <see langword="null"/>.
    /// </param>
    /// <param name="timeoutSeconds">Per-command timeout in seconds (default 30).</param>
    /// <param name="allowUnsafeBash">
    /// Must be <see langword="true"/> to permit command execution.
    /// Defaults to <see langword="false"/>; see the class-level privilege caveat before enabling.
    /// </param>
    public BashExecutor(string? sandboxRoot = null, int timeoutSeconds = 30, bool allowUnsafeBash = false)
    {
        _sandboxRoot = sandboxRoot ?? Path.Combine(Path.GetTempPath(), $"ananke-bash-{Guid.NewGuid():N}");
        _timeoutSeconds = timeoutSeconds;
        AllowUnsafeBash = allowUnsafeBash;
        Directory.CreateDirectory(_sandboxRoot);
    }

    /// <summary>
    /// When <see langword="false"/> (the default), <see cref="ExecuteAsync"/> returns a fatal
    /// error without spawning a process. Set to <see langword="true"/> only after reviewing
    /// the privilege caveat in the class-level remarks.
    /// </summary>
    public bool AllowUnsafeBash { get; }

    public string Capability => "bash";
    public bool IsStub => false;

    public async Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        if (!AllowUnsafeBash)
            return ToolResult.Fatal(
                "BashExecutor requires AllowUnsafeBash = true. " +
                "Commands run with host-process privileges — review the class-level privilege caveat before enabling.");

        if (!args.TryGetValue("command", out var cmdVal) || cmdVal is null)
            return ToolResult.Fatal("Missing required argument: command");

        var command = cmdVal.ToString()!;

        var (shell, flag) = RuntimeShell();
        var psi = new ProcessStartInfo(shell, $"{flag} \"{EscapeCommand(command)}\"")
        {
            WorkingDirectory = _sandboxRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { /* best-effort kill on timeout */ }
            return ToolResult.Error($"bash: command timed out after {_timeoutSeconds}s");
        }

        var output = stdout.ToString().TrimEnd();
        var errors = stderr.ToString().TrimEnd();

        if (process.ExitCode != 0)
        {
            var detail = errors.Length > 0 ? errors : output;
            return ToolResult.Error($"bash: exit code {process.ExitCode}\n{detail}");
        }

        return ToolResult.Ok(output.Length > 0 ? output : errors);
    }

    /// <summary>Sandbox working directory for this executor instance.</summary>
    public string SandboxRoot => _sandboxRoot;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Directory.Delete(_sandboxRoot, recursive: true); } catch (IOException) { /* best-effort sandbox cleanup */ }
    }

    private static (string Shell, string Flag) RuntimeShell() =>
        OperatingSystem.IsWindows()
            ? ("cmd.exe", "/c")
            : ("/bin/bash", "-c");

    private static string EscapeCommand(string cmd) =>
        OperatingSystem.IsWindows()
            ? cmd.Replace("\"", "\\\"")
            : cmd.Replace("\"", "\\\"");
}
