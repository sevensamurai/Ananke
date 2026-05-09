using System.Diagnostics.CodeAnalysis;
using Ananke.Organics.Kernel.Snapshots;

namespace Ananke.Tool.Shared;

/// <summary>
/// Loads a <see cref="HostSnapshot"/> from a file, handling the most common
/// error cases with a consistent return pattern.
/// </summary>
public static class SnapshotLoader
{
    /// <summary>
    /// Reads and parses a <see cref="HostSnapshot"/> from a YAML file.
    /// </summary>
    /// <param name="file">The snapshot file to read.</param>
    /// <param name="snapshot">The parsed snapshot, or <see langword="null"/> on failure.</param>
    /// <param name="errorMessage">Human-readable error message when parsing fails.</param>
    /// <returns><see langword="true"/> on success; <see langword="false"/> when the file
    /// does not exist or cannot be parsed.</returns>
    public static bool TryLoad(
        FileInfo file,
        [NotNullWhen(true)] out HostSnapshot? snapshot,
        [NotNullWhen(false)] out string? errorMessage)
    {
        snapshot = null;
        errorMessage = null;

        if (!file.Exists)
        {
            errorMessage = $"File not found: {file.FullName}";
            return false;
        }

        try
        {
            snapshot = HostSnapshotExporter.FromYaml(File.ReadAllText(file.FullName));
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to parse snapshot: {ex.Message}";
            return false;
        }
    }
}
