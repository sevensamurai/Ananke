using Ananke.Design;

namespace Ananke.Federation.Prompts;

/// <summary>
/// Compiles a workflow manifest, job name, and optional skills into a system prompt
/// suitable for a remote platform agent.
/// </summary>
public interface ISystemPromptCompiler
{
    /// <summary>
    /// Compiles a system prompt for a specific job within a manifest.
    /// </summary>
    /// <param name="manifest">The workflow manifest providing context.</param>
    /// <param name="jobName">The job within the manifest to compile a prompt for.</param>
    /// <param name="skills">Optional skill descriptions to embed in the prompt.</param>
    /// <returns>The compiled system prompt string.</returns>
    string Compile(WorkflowManifest manifest, string jobName, IReadOnlyList<string>? skills = null);
}
