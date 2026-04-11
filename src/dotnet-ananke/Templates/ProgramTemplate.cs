namespace Ananke.Tool.Templates;

/// <summary>
/// Generates a <c>Program.cs</c> entry point for a scaffolded workflow project.
/// Selects the appropriate template based on the workflow pattern.
/// </summary>
internal static class ProgramTemplate
{
    /// <summary>
    /// Renders a <c>Program.cs</c> for the given pattern.
    /// Manifest-driven patterns use the generic scaffold template;
    /// code-driven patterns use pattern-specific templates.
    /// </summary>
    public static string Render(string name, string pattern = "etl") =>
        TemplateEngine.Render(ResourceName(pattern), TemplateEngine.StandardVariables(name, "openai"));

    private static string ResourceName(string pattern) => pattern switch
    {
        "review-critique" => "program-review-critique.cs-template",
        "iterative-refinement" => "program-iterative-refinement.cs-template",
        "router" => "program-router.cs-template",
        "human-in-the-loop" => "program-human-in-the-loop.cs-template",
        "handoff" => "program-handoff.cs-template",
        _ => "Program.cs-template",
    };
}
