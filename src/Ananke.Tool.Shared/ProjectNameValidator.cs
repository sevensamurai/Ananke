namespace Ananke.Tool.Shared;

/// <summary>
/// Validates project names for the <c>nnke new *</c> scaffold commands.
/// </summary>
public static class ProjectNameValidator
{
    /// <summary>
    /// Whether <paramref name="name"/> is usable as a project directory name and a
    /// <c>.csproj</c> file name.
    /// </summary>
    /// <remarks>
    /// Deliberately does not use <see cref="Path.GetInvalidFileNameChars"/>: that is a
    /// platform capability query, not a portability check — it returns 41 characters on
    /// Windows but only <c>'\0'</c> and <c>'/'</c> on Unix, so it silently stopped rejecting
    /// anything on Linux. An explicit allowlist behaves the same on every platform and matches
    /// what the ANANKE_IO_002 diagnostic already tells the user ("letters, numbers, hyphens,
    /// underscores, and periods").
    /// </remarks>
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        // "." and ".." are made entirely of allowlisted characters, so the loop below would
        // accept them on its own — and Path.Combine(cwd, "..") then writes into the parent
        // directory instead of a new one. Reject both explicitly.
        if (name is "." or "..") return false;

        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-'))
                return false;
        }

        return true;
    }
}
