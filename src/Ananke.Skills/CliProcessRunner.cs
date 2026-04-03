using System.Diagnostics;

namespace Ananke.Skills;

/// <summary>
/// Safe CLI process execution with timeout, output capture, and output size limits.
/// Used by catalog implementations to bridge external CLI tools as agent tools.
/// </summary>
public static class CliProcessRunner
{
    /// <summary>Default timeout for CLI tool execution.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Maximum stdout size captured (128 KB). Larger output is truncated.</summary>
    public const int MaxOutputBytes = 128 * 1024;

    /// <summary>
    /// Runs a CLI command and captures stdout/stderr.
    /// </summary>
    /// <param name="fileName">The executable to run (e.g. <c>"uvx"</c>).</param>
    /// <param name="arguments">Command-line arguments.</param>
    /// <param name="timeout">Maximum execution time. Defaults to <see cref="DefaultTimeout"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Exit code, stdout, and stderr.</returns>
    public static async Task<CliProcessResult> RunAsync(
        string fileName,
        string arguments,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;

        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
            return new CliProcessResult(-1, string.Empty, $"Failed to start process: {fileName}");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(effectiveTimeout);

        try
        {
            var stdoutTask = ReadWithLimitAsync(process.StandardOutput, MaxOutputBytes, timeoutCts.Token);
            var stderrTask = ReadWithLimitAsync(process.StandardError, MaxOutputBytes, timeoutCts.Token);

            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            return new CliProcessResult(process.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout — kill the process
            TryKill(process);
            return new CliProcessResult(-1, string.Empty,
                $"Process '{fileName}' timed out after {effectiveTimeout.TotalSeconds:F0}s");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static async Task<string> ReadWithLimitAsync(
        StreamReader reader, int maxBytes, CancellationToken ct)
    {
        var buffer = new char[8192];
        var totalChars = 0;
        var limit = maxBytes; // char ≈ byte for ASCII/UTF-8 CLI output

        using var writer = new StringWriter();

        int read;
        while ((read = await reader.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            var charsToWrite = Math.Min(read, limit - totalChars);
            if (charsToWrite > 0)
            {
                writer.Write(buffer, 0, charsToWrite);
                totalChars += charsToWrite;
            }

            if (totalChars >= limit)
                break;
        }

        return writer.ToString();
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch { /* best effort */ }
    }
}

/// <summary>
/// Result of a CLI process execution.
/// </summary>
public readonly record struct CliProcessResult(int ExitCode, string Stdout, string Stderr)
{
    /// <summary>Whether the process exited successfully (exit code 0).</summary>
    public bool Success => ExitCode == 0;
}
