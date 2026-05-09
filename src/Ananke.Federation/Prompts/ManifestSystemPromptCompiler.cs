using System.Text;
using Ananke.Design;

namespace Ananke.Federation.Prompts;

/// <summary>
/// Default system prompt compiler that combines the manifest's job description,
/// available tool names, and optional skill descriptions into a system prompt.
/// </summary>
public sealed class ManifestSystemPromptCompiler : ISystemPromptCompiler
{
    /// <inheritdoc />
    public string Compile(WorkflowManifest manifest, string jobName, IReadOnlyList<string>? skills = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);

        if (!manifest.Jobs.TryGetValue(jobName, out var job))
            throw new ArgumentException($"Job '{jobName}' not found in manifest '{manifest.Name}'.", nameof(jobName));

        var sb = new StringBuilder();

        sb.AppendLine($"You are the '{jobName}' agent in the '{manifest.Name}' workflow.");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(job.SystemPrompt))
        {
            sb.AppendLine(job.SystemPrompt);
            sb.AppendLine();
        }

        if (skills is { Count: > 0 })
        {
            sb.AppendLine("## Learned Skills");
            sb.AppendLine();
            foreach (var skill in skills)
            {
                sb.AppendLine($"- {skill}");
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
