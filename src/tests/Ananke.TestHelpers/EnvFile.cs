using System.Collections.Concurrent;

namespace Ananke.TestHelpers;

/// <summary>
/// Reads credentials for the live provider tests from a <c>.env</c> file at the repository root,
/// falling back to real environment variables.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately hand-rolled rather than taking a package dependency: the whole contract is
/// <c>KEY=VALUE</c>, and a <c>PackageReference</c> for <c>Split('=')</c> is a poor trade.
/// </para>
/// <para>
/// <b>A real environment variable always wins</b>, so CI can supply secrets without a file, and a
/// developer can override one value for a single run without editing the file.
/// </para>
/// </remarks>
public static class EnvFile
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Values = new(Load);
    private static readonly ConcurrentDictionary<string, string?> Cache = new();

    /// <summary>
    /// Returns the value for <paramref name="key"/>, or <see langword="null"/> when it is set
    /// nowhere or set to an empty value.
    /// </summary>
    /// <param name="key">Variable name.</param>
    public static string? Get(string key) => Cache.GetOrAdd(key, static k =>
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(k);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment;

        return Values.Value.TryGetValue(k, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    });

    /// <summary>
    /// Returns the subset of <paramref name="keys"/> that have no value, so a caller can skip with a
    /// reason that names them.
    /// </summary>
    /// <remarks>
    /// Returns the names rather than calling <c>Assert.Ignore</c> itself, so this assembly stays
    /// free of a test-framework dependency.
    /// </remarks>
    /// <param name="keys">Variables the caller cannot run without.</param>
    public static string[] MissingKeys(params string[] keys) =>
        [.. keys.Where(k => Get(k) is null)];

    private static IReadOnlyDictionary<string, string> Load()
    {
        var file = FindUpwards(".env");
        if (file is null)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadLines(file))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
                continue;

            var key = line[..separator].Trim();
            // "export FOO=bar" is a common shape for a file that is also sourced by a shell.
            if (key.StartsWith("export ", StringComparison.Ordinal))
                key = key["export ".Length..].Trim();

            values[key] = Unquote(line[(separator + 1)..].Trim());
        }

        return values;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0]
            ? value[1..^1]
            : value;

    private static string? FindUpwards(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }
}
