using Ananke.Design;
using Ananke.Orchestration.Tools;
using Ananke.Roles.Roles;
using Shouldly;
// Alias rather than a namespace using: Ananke.Abstractions.Agents also declares AgentRole,
// which would collide with Ananke.Roles.Roles.AgentRole used throughout this fixture.
using Models = Ananke.Abstractions.Agents.Models;

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
                ["local"] = new() { Provider = "openai", Model = Models.OpenAI.Gpt54Mini }
            });

            var manifest = factory.CreateManifest(role, toolkit);

            manifest.Intents.ShouldBe(["review", "quality"]);
            manifest.Models["local"].Model.ShouldBe(Models.OpenAI.Gpt54Mini);
            manifest.Jobs["main"].ModelAlias.ShouldBe("local");
            manifest.Jobs["main"].Tools.ShouldBe(["search_docs"]);
            manifest.Tools["search_docs"].Description.ShouldBe("Searches the docs");
        }
        finally
        {
            File.Delete(promptPath);
        }
    }

    // ── unresolved aliases ───────────────────────────────

    /// <summary>
    /// An alias absent from the map used to become <c>Provider = "studio"</c> — a provider no
    /// factory registers, which <c>ModelResolver</c> rejects by telling the caller to register it.
    /// It records the reference instead, which is what it actually is.
    /// </summary>
    [Test]
    public void An_unresolved_alias_is_recorded_as_a_reference_not_a_provider()
    {
        var promptPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.prompt.txt");
        File.WriteAllText(promptPath, "You are a reviewer.");

        try
        {
            var manifest = new RoleManifestFactory().CreateManifest(new AgentRole
            {
                Name = "solo",
                DomainTags = [],
                ModelAlias = "not-in-the-map",
                SystemPromptPath = promptPath
            });

            var model = manifest.Models["not-in-the-map"];
            model.Ref.ShouldBe("not-in-the-map");
            model.Provider.ShouldNotBe("studio");
        }
        finally
        {
            File.Delete(promptPath);
        }
    }

    /// <summary>
    /// Building manifests with no alias map is a supported path —
    /// <c>StudioOptions.ModelAliasMap</c> defaults to empty — so this must not throw.
    /// </summary>
    [Test]
    public void Building_without_an_alias_map_does_not_throw()
    {
        var promptPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.prompt.txt");
        File.WriteAllText(promptPath, "You are a reviewer.");

        try
        {
            Should.NotThrow(() => new RoleManifestFactory().CreateManifest(new AgentRole
            {
                Name = "solo",
                DomainTags = [],
                ModelAlias = "anything",
                SystemPromptPath = promptPath
            }));
        }
        finally
        {
            File.Delete(promptPath);
        }
    }
}
