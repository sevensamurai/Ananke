namespace Ananke.Tool.Templates;

/// <summary>
/// Generates a <c>secrets.json</c> skeleton with placeholder keys for the chosen provider.
/// Templates loaded from embedded <c>secrets-{provider}.json.template</c> resources.
/// </summary>
internal static class SecretsTemplate
{
    public static string Render(string provider)
    {
        var resourceName = provider switch
        {
            "anthropic" => "secrets-anthropic.json.template",
            "google" => "secrets-google.json.template",
            _ => "secrets-openai.json.template",
        };

        return TemplateEngine.Load(resourceName);
    }
}
