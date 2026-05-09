namespace Ananke.Tool.Templates;

/// <summary>
/// Generates <c>.ananke.yml</c> manifest content for different workflow topology patterns.
/// Templates are loaded from embedded <c>.template</c> resources.
/// </summary>
internal static class ManifestTemplate
{
    public static string Render(string name, string provider, string pattern)
    {
        var resourceName = pattern switch
        {
            "etl" => "manifest-etl.ananke.yml.template",
            "fan-out" => "manifest-fan-out.ananke.yml.template",
            _ => "manifest-sequential.ananke.yml.template",
        };

        return TemplateEngine.Render(resourceName, TemplateEngine.StandardVariables(name, provider));
    }
}
