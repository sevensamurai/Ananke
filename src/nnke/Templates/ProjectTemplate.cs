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
        var resourceName = pattern switch
        {
            "organic-host" => "project-organic-host.csproj.template",
            "streaming-chat" => "project-streaming-chat.csproj.template",
            _ when CodePatterns.Contains(pattern) => "project-code.csproj.template",
            _ => "project.csproj.template",
        };
        return TemplateEngine.Render(resourceName, TemplateEngine.StandardVariables(name, provider));
    }
}
