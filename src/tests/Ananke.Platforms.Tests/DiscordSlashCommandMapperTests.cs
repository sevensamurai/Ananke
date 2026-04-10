using Ananke.Orchestration.Tools;
using Ananke.Platforms.Discord;
using Discord;
using Shouldly;

namespace Ananke.Platforms.Tests;

[TestFixture]
public sealed class DiscordSlashCommandMapperTests
{
    [Test]
    public void ToSlashCommand_ZeroParams_MapsNameAndDescription()
    {
        var tool = new ToolDefinition
        {
            Name = "current_time",
            Description = "Returns the current UTC date and time.",
            Parameters = [],
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("now"))
        };

        var builder = DiscordSlashCommandMapper.ToSlashCommand(tool);
        var command = builder.Build();

        command.Name.Value.ShouldBe("current_time");
        command.Description.Value.ShouldBe("Returns the current UTC date and time.");
        command.Options.IsSpecified.ShouldBeFalse();
    }

    [Test]
    public void ToSlashCommand_WithStringParam_MapsParameterType()
    {
        var tool = new ToolDefinition
        {
            Name = "echo",
            Description = "Echoes input back.",
            Parameters = [new ToolParameter("text", "The text to echo", "string", IsRequired: true)],
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
        };

        var builder = DiscordSlashCommandMapper.ToSlashCommand(tool);
        var command = builder.Build();

        command.Options.Value.ShouldNotBeNull();
        command.Options.Value.Count.ShouldBe(1);
        var option = command.Options.Value[0];
        option.Name.ShouldBe("text");
        option.Description.ShouldBe("The text to echo");
        option.Type.ShouldBe(ApplicationCommandOptionType.String);
        option.IsRequired.ShouldBe(true);
    }

    [Test]
    public void ToSlashCommand_IntegerParam_MapsToDiscordInteger()
    {
        var tool = new ToolDefinition
        {
            Name = "roll_dice",
            Description = "Rolls dice.",
            Parameters = [new ToolParameter("sides", "Number of sides", "integer", IsRequired: true)],
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("6"))
        };

        var command = DiscordSlashCommandMapper.ToSlashCommand(tool).Build();
        command.Options.Value[0].Type.ShouldBe(ApplicationCommandOptionType.Integer);
    }

    [Test]
    public void ToSlashCommand_NumberParam_MapsToDiscordNumber()
    {
        var tool = new ToolDefinition
        {
            Name = "convert",
            Description = "Converts temperature.",
            Parameters = [new ToolParameter("celsius", "Temperature in C", "number", IsRequired: true)],
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("72"))
        };

        var command = DiscordSlashCommandMapper.ToSlashCommand(tool).Build();
        command.Options.Value[0].Type.ShouldBe(ApplicationCommandOptionType.Number);
    }

    [Test]
    public void ToSlashCommand_BooleanParam_MapsToDiscordBoolean()
    {
        var tool = new ToolDefinition
        {
            Name = "toggle",
            Description = "Toggles a feature.",
            Parameters = [new ToolParameter("enabled", "On or off", "boolean", IsRequired: false)],
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("done"))
        };

        var command = DiscordSlashCommandMapper.ToSlashCommand(tool).Build();
        var option = command.Options.Value[0];
        option.Type.ShouldBe(ApplicationCommandOptionType.Boolean);
        option.IsRequired.ShouldBe(false);
    }

    [Test]
    public void ToSlashCommand_NameNormalizedToLowercase()
    {
        var tool = new ToolDefinition
        {
            Name = "My_Tool",
            Description = "Test tool.",
            Parameters = [],
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
        };

        var command = DiscordSlashCommandMapper.ToSlashCommand(tool).Build();
        command.Name.Value.ShouldBe("my_tool");
    }

    [Test]
    public void ToSlashCommand_LongDescription_TruncatedTo100Chars()
    {
        var longDesc = new string('a', 200);
        var tool = new ToolDefinition
        {
            Name = "test",
            Description = longDesc,
            Parameters = [],
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
        };

        var command = DiscordSlashCommandMapper.ToSlashCommand(tool).Build();
        command.Description.Value.Length.ShouldBeLessThanOrEqualTo(100);
    }

    [Test]
    public void ToSlashCommand_MultipleParams_AllMapped()
    {
        var tool = new ToolDefinition
        {
            Name = "search",
            Description = "Searches for items.",
            Parameters =
            [
                new ToolParameter("query", "Search query", "string", IsRequired: true),
                new ToolParameter("limit", "Max results", "integer", IsRequired: false)
            ],
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("[]"))
        };

        var command = DiscordSlashCommandMapper.ToSlashCommand(tool).Build();
        command.Options.Value.Count.ShouldBe(2);
        command.Options.Value[0].Name.ShouldBe("query");
        command.Options.Value[1].Name.ShouldBe("limit");
    }

    [Test]
    public void ExtractArgs_ReturnsEmptyForNoOptions()
    {
        var args = DiscordSlashCommandMapper.ExtractArgs([]);
        args.Count.ShouldBe(0);
    }
}
