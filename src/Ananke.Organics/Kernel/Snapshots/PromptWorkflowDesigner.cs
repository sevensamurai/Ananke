using Ananke.Abstractions.Agents;

namespace Ananke.Organics.Kernel.Snapshots;

/// <summary>
/// Generates a <see cref="WorkflowSnapshot"/> from a natural language prompt by asking
/// an LLM to produce the kernel snapshot YAML, then parsing it. This is the entry
/// point for the "prompt → workflow" pipeline:
/// <code>
/// User prompt → PromptWorkflowDesigner → WorkflowSnapshot YAML → WorkflowActivator → live Workflow&lt;T&gt;
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// The designer passes the available tool names to the LLM as context so it can
/// select which tools belong in the cell. The LLM generates the full cell YAML
/// (domain, tools, jobs, connections, system prompts) which is then parsed into
/// a <see cref="WorkflowSnapshot"/>.
/// </para>
/// <para>
/// The system prompt instructs the model to output valid kernel snapshot YAML
/// containing exactly one cell. The designer extracts the first cell from the
/// parsed snapshot.
/// </para>
/// </remarks>
public sealed class PromptWorkflowDesigner(IAgentModel model)
{
    private const string DefaultSystemPrompt = """
        You are a workflow architect for the Ananke organic kernel system.
        Given a user's description of what a workflow cell should do, generate
        a kernel snapshot YAML that defines exactly ONE cell.

        The YAML format is:

        ```
        kernel: <kernel-name>
        version: 1
        taken_at: <ISO 8601 timestamp>

        cells:
          <cell-name>:
            domain: <primary-domain>
            tools:
              - <tool_name_1>
              - <tool_name_2>
            models:
              default:
                provider: openai
                model: gpt-4o-mini
            jobs:
              <job-name>:
                type: agent
                model: default
                system_prompt: |
                  <system prompt for this agent job>
              <another-job>:
                type: code
            connections:
              - <job-name> -> <another-job>
        ```

        Rules:
        - Only use tools from the AVAILABLE TOOLS list provided below.
        - Choose a clear, descriptive cell name using kebab-case.
        - Choose a domain that reflects the cell's primary responsibility.
        - Write focused system prompts for agent jobs.
        - Keep the topology simple (usually 1-3 jobs in a chain).
        - Output ONLY the YAML — no markdown fences, no explanation.
        """;

    /// <summary>
    /// Generates a <see cref="WorkflowSnapshot"/> from a natural language description.
    /// </summary>
    /// <param name="prompt">
    /// Natural language description of what the cell should do.
    /// Example: <c>"Create a bookstore catalog assistant that can search books and check inventory"</c>
    /// </param>
    /// <param name="availableTools">
    /// Tool names the LLM can choose from. These must match tools registered in the
    /// <see cref="WorkflowActivator{TState}"/>'s tool registry.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A parsed <see cref="WorkflowSnapshot"/> ready for hydration.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the LLM response cannot be parsed into a valid cell snapshot.
    /// </exception>
    public async Task<WorkflowSnapshot> DesignAsync(
        string prompt,
        IReadOnlyList<string> availableTools,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(availableTools);

        var toolList = string.Join("\n", availableTools.Select(t => $"  - {t}"));

        return await DesignCoreAsync(prompt, toolList, ct);
    }

    /// <summary>
    /// Generates a <see cref="WorkflowSnapshot"/> from a natural language description,
    /// using a <see cref="Ananke.Orchestration.Tools.ToolKit"/> to provide the LLM with tool names <em>and
    /// descriptions</em> for better tool selection.
    /// </summary>
    /// <param name="prompt">Natural language description of what the cell should do.</param>
    /// <param name="toolKit">
    /// Tool registry with full definitions. Names and descriptions are sent to the LLM.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A parsed <see cref="WorkflowSnapshot"/> ready for hydration.</returns>
    public async Task<WorkflowSnapshot> DesignAsync(
        string prompt,
        Ananke.Orchestration.Tools.ToolKit toolKit,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(toolKit);

        var toolList = string.Join("\n", toolKit.Tools.Values.Select(t =>
            $"  - {t.Name}: {t.Description}"));

        return await DesignCoreAsync(prompt, toolList, ct);
    }

    private async Task<WorkflowSnapshot> DesignCoreAsync(
        string prompt, string toolList, CancellationToken ct)
    {
        var userMessage = $"""
            AVAILABLE TOOLS:
            {toolList}

            USER REQUEST:
            {prompt}
            """;

        var request = new AgentRequest
        {
            SystemPrompt = DefaultSystemPrompt,
            Messages = [AgentMessage.User(userMessage)]
        };

        var response = await model.GenerateAsync(request, ct);

        var yaml = response.Text
            ?? throw new InvalidOperationException(
                "The model returned an empty response when designing the cell.");

        // Strip markdown fences if the model wrapped the output
        yaml = StripMarkdownFences(yaml);

        try
        {
            var snapshot = HostSnapshotExporter.FromYaml(yaml);

            if (snapshot.Cells.Count == 0)
                throw new InvalidOperationException(
                    "The model generated a snapshot with no cells.");

            return snapshot.Cells[0];
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Failed to parse the model's YAML response into a WorkflowSnapshot. " +
                $"Model output:\n{yaml}", ex);
        }
    }

    private static string StripMarkdownFences(string text)
    {
        var trimmed = text.Trim();

        // Strip ```yaml ... ``` or ``` ... ```
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
                trimmed = trimmed[(firstNewline + 1)..];
        }

        if (trimmed.EndsWith("```"))
            trimmed = trimmed[..^3].TrimEnd();

        return trimmed;
    }
}
