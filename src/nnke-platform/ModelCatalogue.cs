using Ananke.Design;

namespace Ananke.Tool.Platform;

/// <summary>
/// Loads the model-alias catalogue supplied by <c>--catalog</c>.
/// </summary>
/// <remarks>
/// <para>
/// A catalogue is just a manifest: its <c>models:</c> section is exactly the
/// <see cref="ModelDefinition"/> map that a <c>ref:</c> resolves against, so this needs no schema of
/// its own. The convention in this repository is <c>roles.ananke.yml</c>, which declares the aliases
/// every workflow references.
/// </para>
/// <para>
/// <b>Supplied explicitly, never discovered.</b> A sibling file is not picked up automatically. A CLI
/// that absorbs a file it found by looking around is the same surprise as reading a <c>.env</c> from
/// a parent directory, which was rejected for the same reason — and here it would be
/// worse, because the file silently decides which models a workflow runs on.
/// </para>
/// </remarks>
internal static class ModelCatalogue
{
    /// <summary>
    /// Reads the <c>models:</c> section of <paramref name="file"/>, or returns
    /// <see langword="null"/> when no catalogue was supplied.
    /// </summary>
    /// <param name="file">Manifest passed to <c>--catalog</c>, or <see langword="null"/>.</param>
    /// <returns>
    /// The alias map, or <see langword="null"/>. <see langword="null"/> is meaningful: it makes the
    /// validator report <i>"no catalogue was supplied"</i> rather than <i>"the catalogue lacks this
    /// alias"</i>, which are different problems with different fixes.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The file does not exist, or cannot be parsed. Thrown rather than swallowed: a caller who
    /// passed <c>--catalog</c> and silently got no resolution would see every reference reported as
    /// unresolved, and go looking in the wrong place.
    /// </exception>
    public static IReadOnlyDictionary<string, ModelDefinition>? Load(FileInfo? file)
    {
        if (file is null)
            return null;

        if (!file.Exists)
            throw new InvalidOperationException($"Catalogue file not found: {file.FullName}");

        try
        {
            return WorkflowManifest.Load(file.FullName).Models;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse catalogue '{file.FullName}': {ex.Message}", ex);
        }
    }
}
