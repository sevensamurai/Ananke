using Ananke.Design;
using Ananke.Orchestration.Tools;
using Ananke.Roles.Roles;
using Shouldly;

namespace Ananke.Roles.Tests;

[TestFixture]
public sealed class RoleManifestFactoryTests
{
    [Test]
    public void CreateManifest_PopulatesTagsModelAliasAndTools()
    {
        var promptPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.prompt.txt");
        File.WriteAllText(promptPath, "You are a reviewer.");

        try
        {
            var role = new AgentRole
            {
                Name = "reviewer",
                DomainTags = ["review", "quality"],
                ModelAlias = "local",
                SystemPromptPath = promptPath,
                ToolNames = ["search_docs"],
                MaxToolRounds = 4
            };
            var toolkit = new ToolKit("studio")
                .AddTool("search_docs", "Searches the docs", () => ToolResult.Ok("ok"));
            var factory = new RoleManifestFactory(new Dictionary<string, ModelDefinition>
            {
                ["local"] = new() { Provider = "openai", Model = "gpt-4.1-mini" }
            });

            var manifest = factory.CreateManifest(role, toolkit);

            manifest.Intents.ShouldBe(["review", "quality"]);
            manifest.Models["local"].Model.ShouldBe("gpt-4.1-mini");
            manifest.Jobs["main"].ModelAlias.ShouldBe("local");
            manifest.Jobs["main"].Tools.ShouldBe(["search_docs"]);
            manifest.Tools["search_docs"].Description.ShouldBe("Searches the docs");
        }
        finally
        {
            File.Delete(promptPath);
        }
    }
}
