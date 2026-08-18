using System.Text.RegularExpressions;
using Ananke.Abstractions.Agents;
using Ananke.Tool.Templates;
using Shouldly;

namespace Ananke.Tool.Tests;

/// <summary>
/// Guards the model ids the scaffold templates hand to users. Nothing else catches drift here:
/// the <c>.template</c> files are embedded text, so the model-deprecation analyzer never sees
/// them, <c>check-docs.ps1</c> only scans Markdown, and building a scaffolded project does not
/// help either — a wrong-but-current-shaped id compiles fine and only fails at the provider's API.
/// Both failure modes below were live in the tree until 2026-08-10.
/// </summary>
[TestFixture]
public class ScaffoldTemplateModelTests
{
    /// <summary>Model ids inside a double-quoted string in rendered template output.</summary>
    private static readonly Regex QuotedModelId = new(
        @"""(?<id>(?:gpt|claude|gemini|o\d)[A-Za-z0-9.\-]*)""", RegexOptions.Compiled);

    private static readonly string[] AllProviders = ["openai", "anthropic", "google"];

    /// <summary>Every <c>.cs</c> template that carries a "swap in a real provider" comment.</summary>
    private static readonly string[] ProviderListingTemplates =
    [
        "Program.cs.template",
        "program-quickstart.cs.template",
        "program-streaming-chat.cs.template",
    ];

    private static string Render(string resource, string provider) =>
        TemplateEngine.Render(resource, TemplateEngine.StandardVariables("sample", provider));

    [Test]
    public void AllTemplates_RenderedForEveryProvider_ReferenceNoRetiredOrDeprecatedModel()
    {
        var offenders = new List<string>();

        foreach (var resource in ProviderListingTemplates)
            foreach (var provider in AllProviders)
            {
                var rendered = Render(resource, provider);

                foreach (Match match in QuotedModelId.Matches(rendered))
                {
                    var id = match.Groups["id"].Value;
                    if (ModelLifecycleData.Entries.TryGetValue(id, out var entry))
                        offenders.Add($"{resource} (--provider {provider}): '{id}' is {entry.Status} — use '{entry.ReplacedBy}'");
                }
            }

        offenders.ShouldBeEmpty(
            "scaffold templates must only suggest Current models:" + Environment.NewLine +
            string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The three-provider comment blocks list all providers at once, so a single <c>{{model}}</c>
    /// placeholder resolved to the *selected* provider's id on every line — emitting
    /// <c>AnthropicAgentModel.Create(apiKey, "gpt-5.6-terra")</c> for an OpenAI scaffold. That
    /// compiles and fails only when the provider rejects the id, so no build-based check finds it.
    /// </summary>
    [TestCase("Program.cs.template")]
    [TestCase("program-quickstart.cs.template")]
    [TestCase("program-streaming-chat.cs.template")]
    public void ProviderComment_PairsEachProviderWithItsOwnModel_RegardlessOfSelectedProvider(string resource)
    {
        // Against the stars, not named ids — so moving a star updates the scaffold
        // and this assertion together, and neither can drift from the other.
        (string Factory, string ExpectedModel)[] expectations =
        [
            ("OpenAIChatAgentModel.Create", Models.OpenAI.Starred),
            ("AnthropicAgentModel.Create", Models.Anthropic.Starred),
            ("GeminiAgentModel.Create", Models.Google.Starred),
        ];

        foreach (var provider in AllProviders)
        {
            // Comment lines only. The live code in the streaming templates also calls a factory —
            // as `{{provider_class}}.Create(..., "{{model}}")`, which is correct by construction
            // because both placeholders resolve from the same selected provider. It is the
            // hand-written three-provider *listing* that can mismatch.
            var commentLines = Render(resource, provider)
                .Split('\n')
                .Select(l => l.TrimStart())
                .Where(l => l.StartsWith("//", StringComparison.Ordinal))
                .ToArray();

            foreach (var (factory, expectedModel) in expectations)
            {
                var line = commentLines.SingleOrDefault(l => l.Contains(factory, StringComparison.Ordinal));

                line.ShouldNotBeNull($"{resource} should show a {factory} example");
                line!.ShouldContain(
                    $"\"{expectedModel}\"",
                    customMessage: $"{resource} (--provider {provider}): {factory} must be paired with {expectedModel}");
            }
        }
    }

    [Test]
    public void ProviderClass_ForGoogle_IsTheCurrentTypeName()
    {
        // GoogleAgentModel was renamed to GeminiAgentModel; the quickstart template kept naming the
        // old one in a comment, so the snippet did not compile at all (CS0103).
        TemplateEngine.ProviderClass("google").ShouldBe("GeminiAgentModel");

        foreach (var resource in ProviderListingTemplates)
            foreach (var provider in AllProviders)
                Render(resource, provider).ShouldNotContain("GoogleAgentModel");
    }
}
