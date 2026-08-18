using System.Reflection;
using System.Text.RegularExpressions;
using Ananke.Abstractions.Agents;

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
    /// Returns the model a scaffold should use for a given provider — the provider's starred
    /// model, never an id named here.
    /// </summary>
    public static string DefaultModel(string provider) => provider switch
    {
        "anthropic" => Models.Anthropic.Starred,
        "google" => Models.Google.Starred,
        _ => Models.OpenAI.Starred,
    };

    /// <summary>
    /// Per-provider starred models, for templates whose "swap in a real provider" comment lists all
    /// three providers at once.
    /// </summary>
    /// <remarks>
    /// These exist because <c>{{model}}</c> resolves to the *selected* provider's model, so using it
    /// on every line of a three-provider list emitted e.g.
    /// <c>AnthropicAgentModel.Create(apiKey, "gpt-5.6-terra")</c> — an OpenAI id passed to the
    /// Anthropic factory. That compiles and fails only at the API call, so building the scaffolded
    /// project could never catch it.
    /// </remarks>
    private static readonly Dictionary<string, string> ProviderModels = new()
    {
        ["model_openai"] = Models.OpenAI.Starred,
        ["model_anthropic"] = Models.Anthropic.Starred,
        ["model_google"] = Models.Google.Starred,
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
    /// Returns the C# class name of the streaming agent model for a given provider.
    /// </summary>
    public static string ProviderClass(string provider) => provider switch
    {
        "anthropic" => "AnthropicAgentModel",
        "google" => "GeminiAgentModel",
        _ => "OpenAIChatAgentModel",
    };

    /// <summary>
    /// Returns the PascalCase configuration section name for a given provider,
    /// matching the key used in <c>secrets.json</c> (e.g. <c>"OpenAI"</c>, <c>"Anthropic"</c>).
    /// </summary>
    public static string ProviderSection(string provider) => provider switch
    {
        "anthropic" => "Anthropic",
        "google" => "Google",
        _ => "OpenAI",
    };

    /// <summary>
    /// Builds the standard variable dictionary used by most scaffold templates.
    /// </summary>
    public static Dictionary<string, string> StandardVariables(string name, string provider)
    {
        var variables = new Dictionary<string, string>
        {
            ["name"] = name,
            ["provider"] = provider,
            ["model"] = DefaultModel(provider),
            ["provider_package"] = ProviderPackage(provider),
            ["provider_class"] = ProviderClass(provider),
            ["provider_section"] = ProviderSection(provider),
            ["ananke_version"] = AnankeVersion,
            ["ms_extensions_version"] = MsExtensionsVersion,
        };

        foreach (var (key, value) in ProviderModels)
            variables[key] = value;

        return variables;
    }

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
