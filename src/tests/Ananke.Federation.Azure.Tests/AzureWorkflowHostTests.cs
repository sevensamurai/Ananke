using Ananke.Design;
using Ananke.Federation.Azure;
using Ananke.Federation.Deployment;
using Ananke.Orchestration.Tools;
using Ananke.TestHelpers;
using Shouldly;

namespace Ananke.Federation.Azure.Tests;

[TestFixture]
public sealed class AzureWorkflowHostTests
{
    private static WorkflowManifest MakeManifest() => WorkflowManifest.Parse([
        "name: test",
        "models:",
        "  default:",
        "    provider: openai",
        "    model: gpt-4.1-mini",
        "jobs:",
        "  agent1:",
        "    type: agent",
        "    model: default",
        "connections:",
        "  - agent1",
    ]);

    [Test]
    public async Task Start_adds_to_active_list()
    {
        var registry = new InMemoryDeploymentRegistry();
        var credProvider = new AzureAgentCredentialProvider(new Uri("https://test.services.ai.azure.com/api/projects/test"));
        var deployer = new AzureAgentDeployer(credProvider, registry);
        using var host = new TestableWorkflowHost(deployer, MakeManifest(), new ToolKit("test"));

        await host.StartAsync("cell-1", WorkflowLoops.Park);

        host.ListActive().ShouldContain("cell-1");
    }

    [Test]
    public async Task Stop_removes_from_active_list()
    {
        var registry = new InMemoryDeploymentRegistry();
        var credProvider = new AzureAgentCredentialProvider(new Uri("https://test.services.ai.azure.com/api/projects/test"));
        var deployer = new AzureAgentDeployer(credProvider, registry);
        using var host = new TestableWorkflowHost(deployer, MakeManifest(), new ToolKit("test"));

        await host.StartAsync("cell-1", WorkflowLoops.Park);
        await host.StopAsync("cell-1");

        host.ListActive().ShouldBeEmpty();
    }

    [Test]
    public async Task Duplicate_name_throws()
    {
        var registry = new InMemoryDeploymentRegistry();
        var credProvider = new AzureAgentCredentialProvider(new Uri("https://test.services.ai.azure.com/api/projects/test"));
        var deployer = new AzureAgentDeployer(credProvider, registry);
        using var host = new TestableWorkflowHost(deployer, MakeManifest(), new ToolKit("test"));

        await host.StartAsync("cell-1", WorkflowLoops.Park);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            host.StartAsync("cell-1", WorkflowLoops.Park));
    }

    [Test]
    public async Task Stop_nonexistent_is_noop()
    {
        var registry = new InMemoryDeploymentRegistry();
        var credProvider = new AzureAgentCredentialProvider(new Uri("https://test.services.ai.azure.com/api/projects/test"));
        var deployer = new AzureAgentDeployer(credProvider, registry);
        using var host = new TestableWorkflowHost(deployer, MakeManifest(), new ToolKit("test"));

        await host.StopAsync("nonexistent"); // Should not throw
    }

    [Test]
    public async Task Dispose_clears_all_cells()
    {
        var registry = new InMemoryDeploymentRegistry();
        var credProvider = new AzureAgentCredentialProvider(new Uri("https://test.services.ai.azure.com/api/projects/test"));
        var deployer = new AzureAgentDeployer(credProvider, registry);
        var host = new TestableWorkflowHost(deployer, MakeManifest(), new ToolKit("test"));

        await host.StartAsync("cell-1", WorkflowLoops.Park);
        await host.StartAsync("cell-2", WorkflowLoops.Park);

        await host.DisposeAsync();

        host.ListActive().ShouldBeEmpty();
    }

    /// <summary>
    /// Wraps AzureWorkflowHost for test disposal convenience.
    /// </summary>
    private sealed class TestableWorkflowHost : IDisposable
    {
        private readonly AzureWorkflowHost _inner;

        public TestableWorkflowHost(AzureAgentDeployer deployer, WorkflowManifest manifest, ToolKit toolKit)
            => _inner = new AzureWorkflowHost(deployer, manifest, toolKit);

        public Task StartAsync(string name, Func<CancellationToken, Task> loop, CancellationToken ct = default) => _inner.StartAsync(name, loop, ct);
        public Task StopAsync(string name) => _inner.StopAsync(name);
        public IReadOnlyList<string> ListActive() => _inner.ListActive();
        public ValueTask DisposeAsync() => _inner.DisposeAsync();
        public void Dispose() => _inner.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
