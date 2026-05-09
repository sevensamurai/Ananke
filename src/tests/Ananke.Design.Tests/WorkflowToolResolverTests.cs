using Ananke.Design.Tools;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Design.Tests;

[TestFixture]
public class WorkflowToolResolverTests
{
    [Test]
    public async Task ResolveJobToolKitsAsync_ResolvesReferencedTools()
    {
        var manifest = new WorkflowManifest
        {
            Name = "test",
            Models = [],
            Tools = new Dictionary<string, ToolManifestEntry>
            {
                ["web_search"] = new()
                {
                    Key = "web_search",
                    Name = "web_search",
                    Description = "Search the web",
                    Binding = new ToolManifestBinding { Reference = "code:web_search" }
                }
            },
            Jobs = new Dictionary<string, JobDefinition>
            {
                ["plan"] = new() { Tools = ["web_search"] }
            },
            Connections = [],
            Profiles = []
        };

        var tool = new ToolDefinition
        {
            Name = "web_search",
            Description = "Search the web",
            Parameters = [],
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
        };

        var resolver = new InMemoryToolBindingResolver()
            .Register("code:web_search", tool);

        var kits = await WorkflowToolResolver.ResolveJobToolKitsAsync(manifest, resolver);

        kits.ShouldContainKey("plan");
        kits["plan"].Tools.ShouldContainKey("web_search");
    }

    [Test]
    public async Task ResolveJobToolKitsAsync_UnknownToolReference_Throws()
    {
        var manifest = new WorkflowManifest
        {
            Name = "test",
            Models = [],
            Tools = [],
            Jobs = new Dictionary<string, JobDefinition>
            {
                ["plan"] = new() { Tools = ["missing"] }
            },
            Connections = [],
            Profiles = []
        };

        var resolver = new InMemoryToolBindingResolver();

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await WorkflowToolResolver.ResolveJobToolKitsAsync(manifest, resolver));

        ex.Message.ShouldContain("unknown tool");
    }
}
