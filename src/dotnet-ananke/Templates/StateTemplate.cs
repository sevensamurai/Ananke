namespace Ananke.Tool.Templates;

/// <summary>
/// Generates the state record file for a scaffolded workflow project.
/// Selects the appropriate state type based on the workflow pattern.
/// </summary>
internal static class StateTemplate
{
    /// <summary>
    /// Renders the state record. Returns the file name and content.
    /// </summary>
    public static (string FileName, string Content) RenderForPattern(string name, string pattern)
    {
        var (resourceName, fileName) = pattern switch
        {
            "review-critique" => ("state-review-critique.cs-template", "ReviewState.cs"),
            "iterative-refinement" => ("state-iterative-refinement.cs-template", "RefinementState.cs"),
            "router" => ("state-router.cs-template", "RouterState.cs"),
            "human-in-the-loop" => ("state-human-in-the-loop.cs-template", "ApprovalState.cs"),
            "handoff" => ("state-handoff.cs-template", "HandoffState.cs"),
            _ => ("PipelineState.cs-template", "PipelineState.cs"),
        };

        var content = TemplateEngine.Render(resourceName, TemplateEngine.StandardVariables(name, "openai"));
        return (fileName, content);
    }

    /// <summary>
    /// Renders the default <c>PipelineState.cs</c> for manifest-driven patterns.
    /// </summary>
    public static string Render(string name) =>
        TemplateEngine.Render("PipelineState.cs-template", TemplateEngine.StandardVariables(name, "openai"));
}
