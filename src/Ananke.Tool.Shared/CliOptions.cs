using System.CommandLine;

namespace Ananke.Tool.Shared;

/// <summary>
/// Shared option factory and accessor helpers for common CLI switches
/// (<c>--json</c>, <c>--quiet</c>, <c>--no-color</c>).
/// </summary>
public static class CliOptions
{
    /// <summary>
    /// Creates the shared <c>--json</c> option (recursive, suitable for the root command).
    /// </summary>
    public static Option<bool> CreateJsonOption() =>
        new("--json")
        {
            Description = "Emit machine-readable JSON output instead of human-formatted text.",
            Recursive = true
        };

    /// <summary>
    /// Reads the <c>--json</c> flag from a parse result.
    /// </summary>
    public static bool IsJson(this ParseResult parseResult) =>
        parseResult.GetValue<bool>("--json");
}
