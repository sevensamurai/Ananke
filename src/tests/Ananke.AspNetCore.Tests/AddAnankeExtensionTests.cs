using Ananke.AspNetCore.DependencyInjection;
using Ananke.Organics.Division;
using Ananke.Organics.Division.Approval;
using Ananke.Organics.Healing;
using Ananke.Organics.Kernel;
using Ananke.Organics.Kernel.Lineage;
using Ananke.Organics.Sensing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ananke.AspNetCore.Tests;

[TestFixture]
public sealed class AddAnankeExtensionTests
{
    [Test]
    public void AddAnanke_ReturnsBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddAnanke();
        builder.ShouldNotBeNull();
        builder.Services.ShouldBeSameAs(services);
    }

    [Test]
    public void AddOrganicHost_RegistersIWorkflowHost()
    {
        var provider = BuildProvider();
        provider.GetService<IWorkflowHost>().ShouldNotBeNull();
    }

    [Test]
    public void AddOrganicHost_RegistersIDivisionPolicy()
    {
        var provider = BuildProvider();
        provider.GetService<IDivisionPolicy>().ShouldNotBeNull();
    }

    [Test]
    public void AddOrganicHost_RegistersIDivisionApprovalGate()
    {
        var provider = BuildProvider();
        provider.GetService<IDivisionApprovalGate>().ShouldNotBeNull();
    }

    [Test]
    public void AddOrganicHost_RegistersIMeshAggregator()
    {
        var provider = BuildProvider();
        provider.GetService<IMeshAggregator>().ShouldNotBeNull();
    }

    [Test]
    public void AddOrganicHost_RegistersILineageStore()
    {
        var provider = BuildProvider();
        provider.GetService<ILineageStore>().ShouldNotBeNull();
    }

    [Test]
    public void AddOrganicHost_RegistersOrganicHost()
    {
        var provider = BuildProvider();
        provider.GetService<OrganicHost>().ShouldNotBeNull();
    }

    private static IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddAnanke().AddOrganicHost();
        return services.BuildServiceProvider();
    }
}
