using Ananke.Roles.Roles;
using Shouldly;

namespace Ananke.Roles.Tests;

[TestFixture]
public sealed class AgentRoleCatalogTests
{
    [Test]
    public void TryGet_KnownRole_ReturnsRole()
    {
        var catalog = new AgentRoleCatalog();
        var role = CreateRole("writer");
        catalog.Add(role);

        var found = catalog.TryGet("writer", out var resolved);

        found.ShouldBeTrue();
        resolved.ShouldNotBeNull();
        resolved!.Name.ShouldBe("writer");
    }

    [Test]
    public void TryGet_UnknownRole_ReturnsFalse()
    {
        var catalog = new AgentRoleCatalog();

        var found = catalog.TryGet("missing", out var role);

        found.ShouldBeFalse();
        role.ShouldBeNull();
    }

    [Test]
    public void Add_DuplicateName_Throws()
    {
        var catalog = new AgentRoleCatalog();
        catalog.Add(CreateRole("writer"));

        Should.Throw<InvalidOperationException>(() => catalog.Add(CreateRole("writer")));
    }

    private static AgentRole CreateRole(string name) => new()
    {
        Name = name,
        DomainTags = ["draft"],
        ModelAlias = "local",
        SystemPromptPath = "prompt.txt"
    };
}
