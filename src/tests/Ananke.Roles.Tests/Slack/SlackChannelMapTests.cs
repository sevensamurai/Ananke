using Ananke.Roles.Roles;
using Ananke.Roles.Slack;
using Ananke.Roles.Studio;
using Shouldly;

namespace Ananke.Roles.Tests.Slack;

[TestFixture]
public sealed class SlackChannelMapTests
{
    private static AgentRole MakeRole(string name) => new()
    {
        Name = name,
        DomainTags = ["test"],
        ModelAlias = "local",
        SystemPromptPath = "prompt.txt"
    };

    private static (SlackChannelMap map, AgentRoleCatalog catalog) Build(
        Dictionary<string, string> channelRoleMap)
    {
        var options = new StudioOptions
        {
            ChannelRoleMap = channelRoleMap
        };
        var catalog = new AgentRoleCatalog();
        foreach (var roleName in channelRoleMap.Values.Distinct())
            catalog.Add(MakeRole(roleName));

        return (new SlackChannelMap(options, catalog), catalog);
    }

    [Test]
    public void TryResolveRole_MappedChannel_ReturnsRole()
    {
        var (map, _) = Build(new Dictionary<string, string> { ["C123"] = "writer" });

        var found = map.TryResolveRole("C123", out var role);

        found.ShouldBeTrue();
        role.ShouldNotBeNull();
        role!.Name.ShouldBe("writer");
    }

    [Test]
    public void TryResolveRole_UnmappedChannel_ReturnsFalse()
    {
        var (map, _) = Build(new Dictionary<string, string> { ["C123"] = "writer" });

        var found = map.TryResolveRole("C999", out var role);

        found.ShouldBeFalse();
        role.ShouldBeNull();
    }

    [Test]
    public void TryResolveRole_NullOrWhiteSpace_ReturnsFalse()
    {
        var (map, _) = Build(new Dictionary<string, string> { ["C123"] = "writer" });

        map.TryResolveRole("", out _).ShouldBeFalse();
        map.TryResolveRole("   ", out _).ShouldBeFalse();
    }

    [Test]
    public void TryResolveRole_MappedChannelButRoleMissingFromCatalog_ReturnsFalse()
    {
        var options = new StudioOptions
        {
            ChannelRoleMap = new Dictionary<string, string> { ["C123"] = "ghost" }
        };
        var catalog = new AgentRoleCatalog(); // "ghost" is NOT added
        var map = new SlackChannelMap(options, catalog);

        var found = map.TryResolveRole("C123", out var role);

        found.ShouldBeFalse();
        role.ShouldBeNull();
    }

    [Test]
    public void MappedChannelIds_ReturnsOnlyChannelsWithKnownRoles()
    {
        var options = new StudioOptions
        {
            ChannelRoleMap = new Dictionary<string, string>
            {
                ["C1"] = "writer",
                ["C2"] = "ghost"   // not in catalog
            }
        };
        var catalog = new AgentRoleCatalog();
        catalog.Add(MakeRole("writer"));
        var map = new SlackChannelMap(options, catalog);

        var ids = map.MappedChannelIds;

        ids.ShouldContain("C1");
        ids.ShouldNotContain("C2");
    }

    [Test]
    public void TryResolveRole_IsCaseInsensitive()
    {
        var (map, _) = Build(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["C123"] = "writer"
        });

        map.TryResolveRole("c123", out var role).ShouldBeTrue();
        role!.Name.ShouldBe("writer");
    }
}
