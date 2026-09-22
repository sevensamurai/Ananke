using ItineraryDemo.Model;

namespace ItineraryDemo.Shared;

/// <summary>
/// Reads keys from a <c>.env</c> at the repository root, falling back to real environment variables.
/// </summary>
/// <remarks>
/// Demo-only, and deliberately not part of the framework: nothing shipped reads a <c>.env</c> it
/// found by looking around, because a library that absorbs credentials from the filesystem is a
/// surprise. A real environment variable always wins, so one value can be overridden for a run.
/// </remarks>
internal static class EnvKeys
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> FromFile = new(Load);

    public static string? Get(string key)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment;

        return FromFile.Value.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static IReadOnlyDictionary<string, string> Load()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".env")))
            directory = directory.Parent;

        if (directory is null)
            return values;

        foreach (var line in File.ReadLines(Path.Combine(directory.FullName, ".env")))
        {
            var text = line.Trim();
            if (text.Length == 0 || text.StartsWith('#') || !text.Contains('='))
                continue;

            var split = text.IndexOf('=');
            values[text[..split].Trim()] = text[(split + 1)..].Trim().Trim('"');
        }

        return values;
    }
}
