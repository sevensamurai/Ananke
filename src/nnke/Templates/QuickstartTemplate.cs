namespace Ananke.Tool.Templates;

/// <summary>
/// Generates scaffold files for <c>nnke new quickstart</c>.
/// Produces a <c>Program.cs</c>, state record, and <c>README.md</c> for Guide 01.
/// </summary>
internal static class QuickstartTemplate
{
    /// <summary>Renders the quickstart <c>Program.cs</c>.</summary>
    public static string RenderProgram(string name, string provider = "openai") =>
        TemplateEngine.Render("program-quickstart.cs.template", TemplateEngine.StandardVariables(name, provider));

    /// <summary>Renders the quickstart state record <c>QuickstartState.cs</c>.</summary>
    public static string RenderState(string name, string provider = "openai") =>
        TemplateEngine.Render("state-quickstart.cs.template", TemplateEngine.StandardVariables(name, provider));

    /// <summary>Renders the quickstart <c>README.md</c>.</summary>
    public static string RenderReadme(string name, string provider = "openai") =>
        TemplateEngine.Render("readme-quickstart.md.template", TemplateEngine.StandardVariables(name, provider));
}
