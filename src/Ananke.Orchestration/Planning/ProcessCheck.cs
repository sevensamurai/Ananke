using System.Diagnostics;
using System.Text;

namespace Ananke.Orchestration.Planning;

/// <summary>How a <see cref="ProcessCheck"/> runs its command.</summary>
public sealed record ProcessCheckOptions
{
    /// <summary>The executable to run — <c>dotnet</c>, <c>make</c>, a script.</summary>
    public required string FileName { get; init; }

    /// <summary>Its arguments, as one command line.</summary>
    public string Arguments { get; init; } = "";

    /// <summary>The criteria this check is prepared to decide.</summary>
    public required IReadOnlyList<string> Criteria { get; init; }

    /// <summary>
    /// Directory the command runs in. <see langword="null"/> inherits the current process's.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>How long the command may take before it is killed. Defaults to five minutes.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Exit codes that mean the criterion holds. Defaults to <c>0</c> — the convention every build
    /// and test runner already follows.
    /// </summary>
    public IReadOnlyList<int> SuccessExitCodes { get; init; } = [0];
}

/// <summary>
/// A check that decides named criteria by running a command and reading its exit code — a build, a
/// test suite, a linter.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only three outcomes exist, and two of them are not verdicts.</b> The command exits with a
/// success code and the criterion holds; it exits with any other code and the criterion does not.
/// Anything else — the executable is missing, the command outruns its timeout — <em>throws</em>,
/// because "the check could not run" is not the same statement as "the criterion is not met", and
/// returning a failing <see cref="Finding"/> for it would put a fabricated verdict in the tree under
/// the authority of something that never ran.
/// </para>
/// <para>
/// <b>The command's output is kept only when the criterion fails</b>, up to <see cref="DetailCap"/>
/// characters of each stream — long enough that a real compiler error or a failing assertion is
/// legible, short enough that a verdict does not become the whole build log. It is captured verbatim
/// rather than summarised: this check does not read what it ran, it reports it. A pass discards both
/// streams, because a check that held needs nothing explained. <see cref="Oracle"/> is the command
/// line itself, so the record says how to run it again by hand.
/// </para>
/// </remarks>
public sealed class ProcessCheck : IDeterministicCheck
{
    /// <summary>
    /// How much of a failing command's output reaches a <see cref="Finding"/>, per stream.
    /// </summary>
    /// <remarks>
    /// A guess, not a budget — deliberately unrefined, and left for whatever reads a
    /// <see cref="CriterionVerdict.Basis"/> downstream (a prompt, an event) to apply its own budget on
    /// top of this. This cap exists only to keep one runaway process from holding megabytes of console
    /// output in memory before anything downstream gets a chance to trim it.
    /// </remarks>
    private const int DetailCap = 4_000;

    private readonly ProcessCheckOptions _options;
    private readonly HashSet<string> _criteria;

    /// <summary>Creates a check from <paramref name="options"/>.</summary>
    public ProcessCheck(ProcessCheckOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.FileName);

        if (options.SuccessExitCodes.Count == 0)
        {
            throw new ArgumentException(
                "A check with no success exit code can never pass.", nameof(options));
        }

        _options = options;
        _criteria = new HashSet<string>(options.Criteria, StringComparer.Ordinal);
        Oracle = options.Arguments.Length == 0
            ? options.FileName
            : $"{options.FileName} {options.Arguments}";
    }

    /// <summary>Creates a check that runs <paramref name="fileName"/> with its defaults.</summary>
    /// <param name="fileName">The executable to run.</param>
    /// <param name="arguments">Its arguments, as one command line.</param>
    /// <param name="criteria">The criteria this check is prepared to decide.</param>
    public ProcessCheck(string fileName, string arguments, IEnumerable<string> criteria)
        : this(new ProcessCheckOptions
        {
            FileName = fileName,
            Arguments = arguments,
            Criteria = [.. criteria ?? throw new ArgumentNullException(nameof(criteria))]
        })
    {
    }

    /// <inheritdoc />
    /// <remarks>The command line, so that re-running it later is a concrete instruction.</remarks>
    public string Oracle { get; }

    /// <inheritdoc />
    public bool CanRule(string criterion) => _criteria.Contains(criterion);

    /// <inheritdoc />
    /// <exception cref="TimeoutException">The command outran <see cref="ProcessCheckOptions.Timeout"/>.</exception>
    public async Task<Finding> RunAsync(string criterion, CancellationToken ct = default)
    {
        if (!CanRule(criterion))
        {
            throw new InvalidOperationException(
                $"'{Oracle}' was asked to rule on a criterion it does not cover: '{criterion}'.");
        }

        var psi = new ProcessStartInfo(_options.FileName, _options.Arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (_options.WorkingDirectory is not null)
            psi.WorkingDirectory = _options.WorkingDirectory;

        // A missing executable throws out of here, and is left to: a check that cannot run is a
        // broken gate, and a broken gate must not read as a failing one.
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"'{Oracle}' could not be started.");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.Timeout);

        try
        {
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            // Captured, not merely drained: a failing gate has something to say now, and a pipe left
            // undrained past the cap would block the process we are waiting on.
            var capturing = Task.WhenAll(
                CaptureAsync(process.StandardOutput, stdout, timeoutCts.Token),
                CaptureAsync(process.StandardError, stderr, timeoutCts.Token));

            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            await capturing.ConfigureAwait(false);

            return _options.SuccessExitCodes.Contains(process.ExitCode)
                ? Finding.Held
                : Finding.Not(Compose(stdout, stderr));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"'{Oracle}' did not finish within {_options.Timeout.TotalSeconds:F0}s, so it "
                + $"decided nothing about '{criterion}'.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    /// <summary>
    /// Reads a stream to its end, keeping at most <see cref="DetailCap"/> characters of it.
    /// </summary>
    /// <remarks>
    /// Reads past the cap without appending, so a chatty process cannot block on a full pipe just
    /// because its output stopped being kept.
    /// </remarks>
    private static async Task CaptureAsync(StreamReader reader, StringBuilder into, CancellationToken ct)
    {
        var buffer = new char[4096];
        int read;

        while ((read = await reader.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            var room = DetailCap - into.Length;
            if (room > 0)
                into.Append(buffer, 0, Math.Min(read, room));
        }
    }

    /// <summary>Both streams, labelled, for a reader who was not there when it ran.</summary>
    private static string Compose(StringBuilder stdout, StringBuilder stderr)
    {
        var parts = new List<string>(2);

        if (stdout.Length > 0)
            parts.Add($"stdout:\n{stdout}");

        if (stderr.Length > 0)
            parts.Add($"stderr:\n{stderr}");

        return parts.Count > 0 ? string.Join("\n\n", parts) : "(the command produced no output)";
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch { /* best effort — it may have exited between the timeout and here */ }
    }
}
