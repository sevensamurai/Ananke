using System.Reflection;
using System.Text.RegularExpressions;

namespace Ananke.Tool.Templates;

/// <summary>
/// Reads <c>.template</c> embedded resources and replaces <c>{{placeholder}}</c> tokens
/// with supplied values. All scaffold templates live under
/// <c>Templates/Resources/</c> and are embedded at build time.
/// </summary>
internal static partial class TemplateEngine
{
    private static readonly Assembly Assembly = typeof(TemplateEngine).Assembly;

    /// <summary>
    /// Loads an embedded template resource and applies placeholder substitutions.
    /// </summary>
    /// <param name="resourceName">
    /// The file name inside <c>Templates/Resources/</c> (e.g. <c>"Program.cs.template"</c>).
    /// </param>
    /// <param name="variables">
    /// Key-value pairs where the key matches a <c>{{key}}</c> token in the template.
    /// </param>
    /// <returns>The rendered template content.</returns>
    /// <exception cref="InvalidOperationException">The embedded resource was not found.</exception>
    public static string Render(string resourceName, Dictionary<string, string> variables)
    {
        var template = LoadResource(resourceName);
        return PlaceholderPattern().Replace(template, match =>
        {
            var key = match.Groups["key"].Value;
            return variables.TryGetValue(key, out var value) ? value : match.Value;
        });
    }

    /// <summary>
    /// Loads a raw embedded template resource without substitution.
    /// </summary>
    public static string Load(string resourceName) => LoadResource(resourceName);

    /// <summary>
    /// Returns the default model name for a given provider.
    /// </summary>
    public static string DefaultModel(string provider) => provider switch
    {
        "anthropic" => "claude-sonnet-4-20250514",
        "google" => "gemini-2.5-flash",
        _ => "gpt-4.1-mini",
    };

    /// <summary>
    /// Returns the NuGet package name for a given provider.
    /// </summary>
    public static string ProviderPackage(string provider) => provider switch
    {
        "anthropic" => "Ananke.Orchestration.Anthropic",
        "google" => "Ananke.Orchestration.Google",
        _ => "Ananke.Orchestration.OpenAI",
    };

    /// <summary>
    /// Builds the standard variable dictionary used by most scaffold templates.
    /// </summary>
    public static Dictionary<string, string> StandardVariables(string name, string provider) => new()
    {
        ["name"] = name,
        ["provider"] = provider,
        ["model"] = DefaultModel(provider),
        ["provider_package"] = ProviderPackage(provider),
    };

    private static string LoadResource(string resourceName)
    {
        // Embedded resource names use dots as separators and the root namespace as prefix.
        // Templates/Resources/Program.cs.template → Ananke.Tool.Templates.Resources.Program.cs.template
        var fullName = $"Ananke.Tool.Templates.Resources.{resourceName}";

        using var stream = Assembly.GetManifestResourceStream(fullName)
            ?? throw new InvalidOperationException(
                $"Embedded template resource '{fullName}' not found. " +
                $"Available: {string.Join(", ", Assembly.GetManifestResourceNames())}");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"\{\{(?<key>[a-z_]+)\}\}")]
    private static partial Regex PlaceholderPattern();
}
