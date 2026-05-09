using System.Text;
using Ananke.Design;
using Ananke.Federation.Prompts;

namespace Ananke.Federation.Azure;

/// <summary>
/// Azure-optimized system prompt compiler. Formats prompts following
/// OpenAI's recommended patterns for the Assistants API instructions field.
/// </summary>
public sealed class AzureSystemPromptCompiler : ISystemPromptCompiler
{
    /// <inheritdoc />
    public string Compile(WorkflowManifest manifest, string jobName, IReadOnlyList<string>? skills = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);

        if (!manifest.Jobs.TryGetValue(jobName, out var job))
            throw new ArgumentException($"Job '{jobName}' not found in manifest '{manifest.Name}'.", nameof(jobName));

        var sb = new StringBuilder();

        sb.AppendLine($"You are '{jobName}', an agent in the '{manifest.Name}' workflow.");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(job.SystemPrompt))
        {
            sb.AppendLine("## Instructions");
            sb.AppendLine();
            sb.AppendLine(job.SystemPrompt);
            sb.AppendLine();
        }

        if (skills is { Count: > 0 })
        {
            sb.AppendLine("## Learned Behaviors");
            sb.AppendLine();
            sb.AppendLine("Apply these learned patterns when relevant:");
            sb.AppendLine();
            foreach (var skill in skills)
            {
                sb.AppendLine($"- {skill}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Guidelines");
        sb.AppendLine();
        sb.AppendLine("- Use available tools to accomplish tasks. Call tools with precise, well-formed arguments.");
        sb.AppendLine("- If a tool call fails, analyze the error and retry with corrected arguments before giving up.");
        sb.AppendLine("- Provide concise, actionable responses.");

        return sb.ToString().TrimEnd();
    }
}
