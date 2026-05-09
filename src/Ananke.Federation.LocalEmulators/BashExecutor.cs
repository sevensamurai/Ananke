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
/// The sandbox root is created on first use and shared within a single executor
/// instance (one executor per local design-loop session). The directory is
/// <em>not</em> cleaned up automatically — callers managing their own session
/// lifecycle should call <see cref="Dispose"/> or delete the directory.
/// </remarks>
internal sealed class BashExecutor : IPlatformNativeExecutor, IDisposable
{
    private readonly string _sandboxRoot;
    private readonly int _timeoutSeconds;
    private bool _disposed;

    public BashExecutor(string? sandboxRoot = null, int timeoutSeconds = 30)
    {
        _sandboxRoot = sandboxRoot ?? Path.Combine(Path.GetTempPath(), $"ananke-bash-{Guid.NewGuid():N}");
        _timeoutSeconds = timeoutSeconds;
        Directory.CreateDirectory(_sandboxRoot);
    }

    public string Capability => "bash";
    public bool IsStub => false;

    public async Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
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
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
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
        try { Directory.Delete(_sandboxRoot, recursive: true); } catch { /* best-effort */ }
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
