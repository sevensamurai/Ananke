using System.Text;
using Ananke.Design;
using Ananke.Federation.Prompts;

namespace Ananke.Federation.Anthropic;

/// <summary>
/// Claude-optimized system prompt compiler. Formats prompts following
/// Anthropic's recommended patterns with XML-style structure markers.
/// </summary>
public sealed class ClaudeSystemPromptCompiler : ISystemPromptCompiler
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
            sb.AppendLine("<instructions>");
            sb.AppendLine(job.SystemPrompt);
            sb.AppendLine("</instructions>");
            sb.AppendLine();
        }

        if (skills is { Count: > 0 })
        {
            sb.AppendLine("<learned_behaviors>");
            sb.AppendLine("Apply these learned patterns when relevant:");
            foreach (var skill in skills)
            {
                sb.AppendLine($"- {skill}");
            }
            sb.AppendLine("</learned_behaviors>");
            sb.AppendLine();
        }

        sb.AppendLine("<guidelines>");
        sb.AppendLine("- Use available tools to accomplish tasks. Provide precise, well-formed arguments.");
        sb.AppendLine("- If a tool call fails, analyze the error and retry with corrected arguments before giving up.");
        sb.AppendLine("- Be concise and actionable in responses.");
        sb.AppendLine("- Think step by step for complex problems.");
        sb.AppendLine("</guidelines>");

        return sb.ToString().TrimEnd();
    }
}
