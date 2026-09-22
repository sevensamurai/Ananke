using System.Security.Cryptography;
using System.Text;

namespace Ananke.Orchestration;

/// <summary>
/// Turns an identifier into a file name that is safe to write, and unique to that identifier.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sanitising alone silently merges two different things.</b> Mapping every character a file name
/// may not carry to <c>-</c> is enough to stop an id being read as a path, and not enough to keep two
/// ids apart: <c>team/plan</c> and <c>team-plan</c> reduce to the same name, so whichever is written
/// second overwrites the first and neither is ever reported missing. Traversal was prevented;
/// collision was not.
/// </para>
/// <para>
/// <b>So the sanitised name carries a fingerprint of the id it came from.</b> It is a hash rather
/// than a counter because the name has to be derivable from the id alone, by a process that has
/// never seen the folder — and <see cref="string.GetHashCode()"/> cannot do it, being randomised per
/// process. The sanitised half is kept in front so a person can still tell what a file holds by
/// looking at it.
/// </para>
/// </remarks>
internal static class FileStoreNaming
{
    /// <summary>The file name for <paramref name="id"/>, ending in <paramref name="suffix"/>.</summary>
    public static string For(string id, string suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var safe = new string([.. id.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-')]);
        return $"{safe}-{Fingerprint(id)}{suffix}";
    }

    /// <summary>Four bytes of SHA-256 over the id, which is plenty to separate names a folder holds.</summary>
    private static string Fingerprint(string id) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id)).AsSpan(0, 4));
}
