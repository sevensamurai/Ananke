namespace Ananke.Tool.Templates;

/// <summary>
/// Generates scaffold files for <c>nnke new chatbox</c>.
/// Produces a Minimal API <c>Program.cs</c> with SSE streaming and a <c>README.md</c>.
/// </summary>
internal static class ChatboxTemplate
{
    /// <summary>Renders the chatbox <c>Program.cs</c>.</summary>
    public static string RenderProgram(string name, string provider = "openai") =>
        TemplateEngine.Render("program-chatbox.cs.template", TemplateEngine.StandardVariables(name, provider));

    /// <summary>Renders the chatbox state record <c>ChatboxState.cs</c>.</summary>
    public static string RenderState(string name, string provider = "openai") =>
        TemplateEngine.Render("state-chatbox.cs.template", TemplateEngine.StandardVariables(name, provider));

    /// <summary>Renders the chatbox <c>README.md</c>.</summary>
    public static string RenderReadme(string name, string provider = "openai") =>
        TemplateEngine.Render("readme-chatbox.md.template", TemplateEngine.StandardVariables(name, provider));
}
