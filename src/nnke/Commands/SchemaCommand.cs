using Ananke.Tool.Shared;
using System.CommandLine;

namespace Ananke.Tool.Commands;

/// <summary>
/// Handles <c>nnke schema</c> — emits a JSON description of all available commands,
/// their arguments, and options. Designed for LLM agents to discover <c>nnke</c>
/// capabilities programmatically.
/// </summary>
internal static class SchemaCommand
{
    public static Command Create(RootCommand root)
    {
        var command = new Command("schema", "Emit a JSON schema of all nnke commands, arguments, and options.");

        command.SetAction(_ => Execute(root));

        return command;
    }

    private static void Execute(RootCommand root)
    {
        var commands = new List<object>();
        CollectCommands(root, prefix: "", commands);

        var schema = new Dictionary<string, object>
        {
            ["tool"] = "nnke",
            ["version"] = typeof(SchemaCommand).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            ["description"] = root.Description ?? "",
            ["commands"] = commands,
        };

        JsonOutput.Write(schema);
    }

    private static void CollectCommands(Command parent, string prefix, List<object> results)
    {
        foreach (var child in parent.Subcommands)
        {
            var fullName = string.IsNullOrEmpty(prefix) ? child.Name : $"{prefix} {child.Name}";

            // If the child has its own subcommands, recurse into them.
            if (child.Subcommands.Any())
            {
                CollectCommands(child, fullName, results);
                continue;
            }

            var arguments = new List<object>();
            foreach (var arg in child.Arguments)
            {
                arguments.Add(new Dictionary<string, object?>
                {
                    ["name"] = arg.Name,
                    ["description"] = arg.Description,
                    ["type"] = arg.ValueType.Name.ToLowerInvariant(),
                    ["required"] = !arg.HasDefaultValue,
                });
            }

            var options = new List<object>();
            foreach (var opt in child.Options)
            {
                if (opt.Name == "--json") continue; // global option, documented at top level

                var entry = new Dictionary<string, object?>
                {
                    ["name"] = opt.Name,
                    ["description"] = opt.Description,
                    ["type"] = opt.ValueType.Name.ToLowerInvariant(),
                };

                if (opt.HasDefaultValue)
                    entry["default"] = opt.GetDefaultValue();

                options.Add(entry);
            }

            results.Add(new Dictionary<string, object?>
            {
                ["name"] = fullName,
                ["description"] = child.Description,
                ["arguments"] = arguments.Count > 0 ? arguments : null,
                ["options"] = options.Count > 0 ? options : null,
            });
        }
    }
}
