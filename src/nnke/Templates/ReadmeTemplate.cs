namespace Ananke.Tool.Templates;

/// <summary>
/// Generates a <c>README.md</c> for scaffolded manifest-driven workflow projects,
/// explaining the role of the <c>.ananke.yml</c> file, its schema, and how to run
/// the workflow.
/// </summary>
internal static class ReadmeTemplate
{
    /// <summary>
    /// Renders a <c>README.md</c> for the given workflow name and provider.
    /// </summary>
    public static string Render(string name, string provider) =>
        TemplateEngine.Render("README.md.template", TemplateEngine.StandardVariables(name, provider));
}
