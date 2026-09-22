using System.Text;

namespace Ananke.Orchestration.Agents.Context;

/// <summary>
/// Spills oversized tool output to a private directory on the local filesystem.
/// </summary>
/// <remarks>
/// <para>
/// The security shape is deliberate and is the point of having a provider rather than a
/// <c>Path.Combine</c> at the call site. The directory is created owner-only and every file inside
/// it is randomly named and created exclusively, so nothing predictable is ever written to a
/// world-readable temp path — a shipped bug class, not a hypothetical one.
/// </para>
/// <para>
/// The tool name is treated as a <b>hint, never a path</b>: it is reduced to a short alphanumeric
/// fragment used only to make a stored file recognisable to a human reading the directory. A tool
/// called <c>../../etc/passwd</c> gets a filename as safe as any other.
/// </para>
/// </remarks>
public sealed class FileToolOutputSpillStore : IToolOutputSpillStore
{
    private const int MaxHintLength = 32;

    private readonly string _root;
    private int _initialized;

    /// <summary>Creates a store rooted at <paramref name="root"/>, or under the temp directory.</summary>
    public FileToolOutputSpillStore(string? root = null) =>
        _root = root ?? Path.Combine(Path.GetTempPath(), "ananke-spill");

    /// <summary>The directory spilled output is written to.</summary>
    public string Root => _root;

    /// <inheritdoc />
    public async Task<SpilledToolOutput> SaveAsync(
        string toolName, string content, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        EnsureRoot();

        var path = Path.Combine(_root, $"{Hint(toolName)}-{Path.GetRandomFileName()}.txt");

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };

        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        var bytes = Encoding.UTF8.GetByteCount(content);

        await using (var stream = new FileStream(path, options))
        await using (var writer = new StreamWriter(stream, Encoding.UTF8))
        {
            await writer.WriteAsync(content.AsMemory(), ct).ConfigureAwait(false);
        }

        return new SpilledToolOutput
        {
            Locator = path,
            RetrievalHint = "Read that file to see the full output.",
            Bytes = bytes
        };
    }

    private void EnsureRoot()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1 && Directory.Exists(_root))
            return;

        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(_root);
        else
            Directory.CreateDirectory(_root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>Reduces a tool name to one safe, short, single-segment fragment.</summary>
    private static string Hint(string toolName)
    {
        if (string.IsNullOrEmpty(toolName))
            return "tool";

        var builder = new StringBuilder(MaxHintLength);
        foreach (var c in toolName)
        {
            if (builder.Length == MaxHintLength)
                break;
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
                builder.Append(c);
        }

        return builder.Length == 0 ? "tool" : builder.ToString();
    }
}
