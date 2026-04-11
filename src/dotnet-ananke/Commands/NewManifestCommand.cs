using Ananke.Tool.Output;
using Ananke.Tool.Templates;
using System.CommandLine;

namespace Ananke.Tool.Commands;

/// <summary>
/// Handles <c>nnke new manifest &lt;name&gt;</c> — generates a standalone
/// <c>.ananke.yml</c> starter file in the current directory.
/// Supports <c>--json</c> for structured output.
/// </summary>
internal static class NewManifestCommand
{
    public static Command Create()
    {
        var nameArg = new Argument<string>("name") { Description = "Workflow name for the manifest." };

        var providerOption = new Option<string>("--provider")
        {
            Description = "LLM provider to configure (openai, anthropic, google).",
            DefaultValueFactory = _ => "openai"
        };

        var patternOption = new Option<string>("--pattern")
        {
            Description = "Workflow topology pattern (etl, fan-out, sequential).",
            DefaultValueFactory = _ => "etl"
        };

        var outputOption = new Option<DirectoryInfo?>("--output")
        {
            Description = "Output directory. Defaults to current directory."
        };

        var command = new Command("manifest", "Generate a standalone .ananke.yml manifest file.")
        {
            nameArg,
            providerOption,
            patternOption,
            outputOption
        };

        command.SetAction(parseResult =>
        {
            var name = parseResult.GetValue(nameArg)!;
            var provider = parseResult.GetValue(providerOption)!;
            var pattern = parseResult.GetValue(patternOption)!;
            var output = parseResult.GetValue(outputOption);
            var json = parseResult.GetValue<bool>("--json");
            Execute(name, provider, pattern, output, json);
        });

        return command;
    }

    private static void Execute(string name, string provider, string pattern, DirectoryInfo? output, bool json)
    {
        var dir = output?.FullName ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(dir);

        var fileName = $"{name}.ananke.yml";
        var path = Path.Combine(dir, fileName);

        if (File.Exists(path))
        {
            if (json)
            {
                JsonOutput.Write(new { status = "skipped", file = path, reason = "File already exists." });
            }
            else
            {
                Console.WriteLine($"  Skipped (exists): {fileName}");
            }
            return;
        }

        File.WriteAllText(path, ManifestTemplate.Render(name, provider, pattern));

        if (json)
        {
            JsonOutput.Write(new { status = "created", file = path, pattern, provider });
        }
        else
        {
            Console.WriteLine($"  Created: {path}");
        }
    }
}
