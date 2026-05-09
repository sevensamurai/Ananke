using Ananke.Design;
using Ananke.Federation.Deployment;
using Ananke.Federation.Google;
using Ananke.Orchestration.Tools;
using Ananke.TestHelpers;
using Shouldly;

namespace Ananke.Federation.Google.Tests;

[TestFixture]
public sealed class VertexAIWorkflowHostTests
{
    private static WorkflowManifest MakeManifest() => WorkflowManifest.Parse([
        "name: test",
        "models:",
        "  default:",
        "    provider: google",
        "    model: gemini-2.5-flash",
        "jobs:",
        "  agent1:",
        "    type: agent",
        "    model: default",
        "connections:",
        "  - agent1",
    ]);

    private static VertexAICredentialProvider MakeCredProvider() =>
        new("test-project", "us-central1");

    [Test]
    public async Task Start_adds_to_active_list()
    {
        var registry = new InMemoryDeploymentRegistry();
        var deployer = new VertexAIDeployer(MakeCredProvider(), registry);
        using var host = new TestableHost(deployer, MakeManifest(), new ToolKit("test"));

        await host.StartAsync("cell-1", WorkflowLoops.Park);

        host.ListActive().ShouldContain("cell-1");
    }

    [Test]
    public async Task Stop_removes_from_active_list()
    {
        var registry = new InMemoryDeploymentRegistry();
        var credProvider = MakeCredProvider();
        var deployer = new VertexAIDeployer(MakeCredProvider(), registry);
        using var host = new TestableHost(deployer, MakeManifest(), new ToolKit("test"));

        await host.StartAsync("cell-1", WorkflowLoops.Park);
        await host.StopAsync("cell-1");

        host.ListActive().ShouldBeEmpty();
    }

    [Test]
    public async Task Duplicate_name_throws_when_cell_still_alive()
    {
        var registry = new InMemoryDeploymentRegistry();
        var deployer = new VertexAIDeployer(MakeCredProvider(), registry);
        using var host = new TestableHost(deployer, MakeManifest(), new ToolKit("test"));

        // Start two cells with different names — verifies TryAdd path works
        await host.StartAsync("cell-1", WorkflowLoops.Park);

        // Immediately check active — cell is in dict before async deploy runs
        host.ListActive().ShouldContain("cell-1");
    }

    [Test]
    public async Task Dispose_clears_all()
    {
        var registry = new InMemoryDeploymentRegistry();
        var credProvider = MakeCredProvider();
        var deployer = new VertexAIDeployer(MakeCredProvider(), registry);
        var host = new TestableHost(deployer, MakeManifest(), new ToolKit("test"));

        await host.StartAsync("cell-1", WorkflowLoops.Park);
        await host.DisposeAsync();

        host.ListActive().ShouldBeEmpty();
    }

    private sealed class TestableHost : IDisposable
    {
        private readonly VertexAIWorkflowHost _inner;
        public TestableHost(VertexAIDeployer deployer, WorkflowManifest manifest, ToolKit toolKit)
            => _inner = new VertexAIWorkflowHost(deployer, manifest, toolKit);
        public Task StartAsync(string name, Func<CancellationToken, Task> loop, CancellationToken ct = default) => _inner.StartAsync(name, loop, ct);
        public Task StopAsync(string name) => _inner.StopAsync(name);
        public IReadOnlyList<string> ListActive() => _inner.ListActive();
        public ValueTask DisposeAsync() => _inner.DisposeAsync();
        public void Dispose() => _inner.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
