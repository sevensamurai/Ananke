using System.Collections.Concurrent;

namespace Ananke.Federation.Adapters;

/// <summary>
/// Outcome kind for an adapter load attempt.
/// </summary>
public enum AdapterLoadStatus
{
    /// <summary>Manifest read and assembly loaded successfully.</summary>
    Loaded,

    /// <summary>The manifest's <c>targetCliVersion</c> range excludes the running CLI version.</summary>
    VersionMismatch,

    /// <summary>The manifest JSON was present but could not be parsed.</summary>
    InvalidManifest,

    /// <summary>No manifest sidecar was found next to the DLL.</summary>
    MissingManifest,

    /// <summary>The entry assembly could not be loaded (corrupted DLL, ABI break, etc.).</summary>
    LoadFailed,
}

/// <summary>
/// Captures the result of a single adapter load attempt.
/// </summary>
public sealed record AdapterLoadResult
{
    /// <summary>Short adapter id, or the DLL file name when the manifest could not be parsed.</summary>
    public required string AdapterId { get; init; }

    /// <summary>Outcome of the load attempt.</summary>
    public required AdapterLoadStatus Status { get; init; }

    /// <summary>Full path to the DLL (or manifest) that was probed.</summary>
    public required string Path { get; init; }

    /// <summary>The parsed manifest, if available.</summary>
    public AdapterManifest? Manifest { get; init; }

    /// <summary>Error message when <see cref="Status"/> is not <see cref="AdapterLoadStatus.Loaded"/>.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Process-scoped store of adapter load outcomes populated by <c>PlatformHost</c> at startup.
/// Consumed by <c>nnke-platform adapters list</c> and <c>nnke-platform adapters doctor</c>.
/// </summary>
public static class AdapterDiagnostics
{
    private static readonly ConcurrentBag<AdapterLoadResult> _results = [];

    /// <summary>Records a load result. Called by <c>PlatformHost</c> during adapter probing.</summary>
    public static void Record(AdapterLoadResult result) => _results.Add(result);

    /// <summary>Snapshot of all recorded results for this process.</summary>
    public static IReadOnlyList<AdapterLoadResult> Results => _results.ToArray();

    /// <summary>Clears all recorded results. Intended for use in tests only.</summary>
    internal static void Reset() => _results.Clear();
}
