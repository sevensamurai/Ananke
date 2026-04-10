using Discord;
using Discord.WebSocket;
using Ananke.Orchestration.Tools;

namespace Ananke.Platforms.Discord;

/// <summary>
/// Maps Ananke <see cref="ToolDefinition"/> instances to Discord slash commands
/// and extracts slash command arguments back into tool-compatible dictionaries.
/// </summary>
internal static class DiscordSlashCommandMapper
{
    /// <summary>
    /// Builds a Discord <see cref="SlashCommandBuilder"/> from an Ananke <see cref="ToolDefinition"/>.
    /// </summary>
    /// <remarks>
    /// <para>Mapping rules:</para>
    /// <list type="table">
    ///   <listheader><term>ToolParameter.JsonType</term><description>Discord type</description></listheader>
    ///   <item><term><c>"integer"</c></term><description><see cref="ApplicationCommandOptionType.Integer"/></description></item>
    ///   <item><term><c>"number"</c></term><description><see cref="ApplicationCommandOptionType.Number"/></description></item>
    ///   <item><term><c>"boolean"</c></term><description><see cref="ApplicationCommandOptionType.Boolean"/></description></item>
    ///   <item><term>everything else</term><description><see cref="ApplicationCommandOptionType.String"/></description></item>
    /// </list>
    /// <para>
    /// Names are normalized to lowercase (Discord requirement). Descriptions are
    /// truncated to 100 characters (Discord limit).
    /// </para>
    /// </remarks>
    internal static SlashCommandBuilder ToSlashCommand(ToolDefinition tool)
    {
        var builder = new SlashCommandBuilder()
            .WithName(NormalizeName(tool.Name))
            .WithDescription(Truncate(tool.Description, 100));

        foreach (var param in tool.Parameters)
        {
            builder.AddOption(
                NormalizeName(param.Name),
                MapOptionType(param.JsonType),
                Truncate(param.Description, 100),
                isRequired: param.IsRequired);
        }

        return builder;
    }

    /// <summary>
    /// Extracts slash command option values into a dictionary compatible with
    /// <see cref="ToolDefinition.ExecuteAsync"/>.
    /// </summary>
    /// <remarks>
    /// Discord.Net returns typed values: <see cref="string"/> for String options,
    /// <see cref="long"/> for Integer, <see cref="double"/> for Number,
    /// <see cref="bool"/> for Boolean. These are passed through to <see cref="ToolArgs"/>
    /// which handles conversion via <see cref="System.Convert.ChangeType(object, Type, IFormatProvider)"/>.
    /// </remarks>
    internal static IReadOnlyDictionary<string, object?> ExtractArgs(
        IReadOnlyCollection<SocketSlashCommandDataOption> options)
    {
        var args = new Dictionary<string, object?>(options.Count);
        foreach (var option in options)
            args[option.Name] = option.Value;
        return args;
    }

    private static ApplicationCommandOptionType MapOptionType(string jsonType) => jsonType switch
    {
        "integer" => ApplicationCommandOptionType.Integer,
        "number" => ApplicationCommandOptionType.Number,
        "boolean" => ApplicationCommandOptionType.Boolean,
        _ => ApplicationCommandOptionType.String
    };

    /// <summary>
    /// Normalizes a tool/parameter name for Discord: lowercase, spaces → hyphens, max 32 chars.
    /// </summary>
    private static string NormalizeName(string name)
    {
        var normalized = name.ToLowerInvariant().Replace(' ', '-');
        return normalized.Length <= 32 ? normalized : normalized[..32];
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "No description";
        return text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength - 1), "…");
    }
}
