using Ananke.Organics.Division;
using Ananke.Organics.Division.Approval;
using Ananke.Organics.Kernel;
using Ananke.Roles.Roles;
using Ananke.Roles.Studio;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ananke.Roles.Tests;

[TestFixture]
public sealed class StudioHostBuilderTests
{
    [Test]
    public void DisableDivision_ProducesNoDivisionPolicyInBuiltServiceCollection()
    {
        var services = new ServiceCollection();
        var builder = new StudioHostBuilder()
            .AddRole(new AgentRole
            {
                Name = "writer",
                DomainTags = ["draft"],
                ModelAlias = "local",
                SystemPromptPath = "prompt.txt"
            })
            .UseApprovalGate<AutoApprovalGate>()
            .DisableDivision();

        builder.Build(services);
        var provider = services.BuildServiceProvider();

        provider.GetService<IDivisionPolicy>().ShouldBeNull();
        provider.GetService<OrganicHost>().ShouldNotBeNull();
    }
}
