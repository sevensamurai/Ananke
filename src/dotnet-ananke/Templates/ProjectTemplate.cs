namespace Ananke.Tool.Templates;

/// <summary>
/// Generates a <c>.csproj</c> file for a scaffolded workflow project.
/// Selects between manifest-driven (includes <c>Ananke.Design</c>) and
/// code-driven (no design package needed) templates.
/// </summary>
internal static class ProjectTemplate
{
    /// <summary>Code-driven patterns that don't need a manifest or Ananke.Design.</summary>
    private static readonly HashSet<string> CodePatterns =
        ["review-critique", "iterative-refinement", "router", "human-in-the-loop", "handoff"];

    public static string Render(string name, string provider, string pattern = "etl")
    {
        var resourceName = CodePatterns.Contains(pattern)
            ? "project-code.csproj.template"
            : "project.csproj.template";
        return TemplateEngine.Render(resourceName, TemplateEngine.StandardVariables(name, provider));
    }
}
