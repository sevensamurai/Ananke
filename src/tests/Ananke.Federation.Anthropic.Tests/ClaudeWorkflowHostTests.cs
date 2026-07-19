using Ananke.Design;
using Ananke.Federation.Anthropic;
using Ananke.Federation.Deployment;
using Ananke.Orchestration.Tools;
using Ananke.TestHelpers;
using Shouldly;

namespace Ananke.Federation.Anthropic.Tests;

[TestFixture]
public sealed class ClaudeWorkflowHostTests
{
    private static WorkflowManifest MakeManifest() => WorkflowManifest.Parse([
        "name: test",
        "models:",
        "  default:",
        "    provider: anthropic",
        "    model: claude-sonnet-5",
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
        var credProvider = new ClaudeCredentialProvider("sk-ant-test");
        var deployer = new ClaudeDeployer(credProvider, registry);
        using var host = new TestableHost(deployer, MakeManifest(), new ToolKit("test"));

        await host.StartAsync("cell-1", WorkflowLoops.Park);

        host.ListActive().ShouldContain("cell-1");
    }

    [Test]
    public async Task Stop_removes_from_active_list()
    {
        var registry = new InMemoryDeploymentRegistry();
        var credProvider = new ClaudeCredentialProvider("sk-ant-test");
        var deployer = new ClaudeDeployer(credProvider, registry);
        using var host = new TestableHost(deployer, MakeManifest(), new ToolKit("test"));

        await host.StartAsync("cell-1", WorkflowLoops.Park);
        await host.StopAsync("cell-1");

        host.ListActive().ShouldBeEmpty();
    }

    [Test]
    public async Task Duplicate_name_throws()
    {
        var registry = new InMemoryDeploymentRegistry();
        var credProvider = new ClaudeCredentialProvider("sk-ant-test");
        var deployer = new ClaudeDeployer(credProvider, registry);
        using var host = new TestableHost(deployer, MakeManifest(), new ToolKit("test"));

        await host.StartAsync("cell-1", WorkflowLoops.Park);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            host.StartAsync("cell-1", WorkflowLoops.Park));
    }

    [Test]
    public async Task Dispose_clears_all()
    {
        var registry = new InMemoryDeploymentRegistry();
        var credProvider = new ClaudeCredentialProvider("sk-ant-test");
        var deployer = new ClaudeDeployer(credProvider, registry);
        var host = new TestableHost(deployer, MakeManifest(), new ToolKit("test"));

        await host.StartAsync("cell-1", WorkflowLoops.Park);
        await host.DisposeAsync();

        host.ListActive().ShouldBeEmpty();
    }

    private sealed class TestableHost : IDisposable
    {
        private readonly ClaudeWorkflowHost _inner;
        public TestableHost(ClaudeDeployer deployer, WorkflowManifest manifest, ToolKit toolKit)
            => _inner = new ClaudeWorkflowHost(deployer, manifest, toolKit);
        public Task StartAsync(string name, Func<CancellationToken, Task> loop, CancellationToken ct = default) => _inner.StartAsync(name, loop, ct);
        public Task StopAsync(string name) => _inner.StopAsync(name);
        public IReadOnlyList<string> ListActive() => _inner.ListActive();
        public ValueTask DisposeAsync() => _inner.DisposeAsync();
        public void Dispose() => _inner.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
