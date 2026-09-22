namespace Ananke.Orchestration.Tests;

/// <summary>
/// Where a live test finds its credentials: the environment, then the repo's own <c>.env</c>.
/// </summary>
/// <remarks>
/// Shared so that "live tests could not find the key" has one answer rather than one per fixture.
/// Nothing here caches: a test that runs after a key changes should see the new one.
/// </remarks>
internal static class Keys
{
    /// <summary>The value of <paramref name="name"/>, or a failure that says what is missing.</summary>
    public static string Require(string name) =>
        Environment.GetEnvironmentVariable(name)
            ?? FromDotEnv(name)
            ?? throw new InvalidOperationException($"{name} is not set; this test needs it.");

    /// <summary>The repo's own <c>.env</c>, which is where these keys live locally.</summary>
    private static string? FromDotEnv(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".env")))
            directory = directory.Parent;

        if (directory is null)
            return null;

        foreach (var line in File.ReadLines(Path.Combine(directory.FullName, ".env")))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#') || !trimmed.StartsWith(name + "=", StringComparison.Ordinal))
                continue;

            var value = trimmed[(name.Length + 1)..].Trim().Trim('"');
            if (value.Length > 0)
                return value;
        }

        return null;
    }
}
