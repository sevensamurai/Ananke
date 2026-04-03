namespace Ananke.Skills;

/// <summary>
/// How the skill's CLI binary is installed and invoked.
/// </summary>
public enum SkillInstallMethod
{
    /// <summary>Python tool via <c>uvx</c> (no install required — runs from cache).</summary>
    Uvx,

    /// <summary>Node.js tool via <c>npx</c>.</summary>
    Npx,

    /// <summary>Docker container.</summary>
    Docker,

    /// <summary>Arbitrary shell command.</summary>
    Shell
}
