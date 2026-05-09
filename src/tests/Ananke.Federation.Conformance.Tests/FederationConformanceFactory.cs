using Ananke.Design;
using Ananke.Federation.Deployment;
using Ananke.Orchestration.Tools;

namespace Ananke.Federation.Conformance.Tests;

/// <summary>
/// Factory helpers shared by the federation conformance suites.
/// </summary>
internal static class FederationConformanceFactory
{
    /// <summary>Creates a minimal <see cref="WorkflowManifest"/> suitable for deploy/validate calls.</summary>
    internal static WorkflowManifest MakeManifest(string name = "conformance-wf") => new()
    {
        Name        = name,
        Models      = [],
        Jobs        = [],
        Connections = []
    };

    /// <summary>Creates an empty <see cref="ToolKit"/> with the given name.</summary>
    internal static ToolKit MakeToolKit(string name = "conformance-kit") => new(name);

    /// <summary>Creates a standard <see cref="DeployOptions"/> for the given platform.</summary>
    internal static DeployOptions MakeOptions(string platform = "fake", bool force = false) => new()
    {
        Platform = platform,
        Force    = force,
        Tags     = ["conformance"]
    };

    /// <summary>
    /// Creates a <see cref="FakeConformanceDeployer"/> and a <see cref="FakeConformanceAgentClient"/>
    /// that share a single in-memory backing store, so deployments written by the deployer
    /// are immediately visible through the client.
    /// </summary>
    internal static (FakeConformanceDeployer Deployer, FakeConformanceAgentClient Client)
        MakePair(string platform = "fake")
    {
        var sharedStore = new Dictionary<string, DeploymentRecord>();
        var deployer    = new FakeConformanceDeployer(sharedStore, platform);
        var client      = new FakeConformanceAgentClient(sharedStore, platform);
        return (deployer, client);
    }
}
