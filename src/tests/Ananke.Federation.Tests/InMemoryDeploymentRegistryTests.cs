using Ananke.Federation.Deployment;
using Shouldly;

namespace Ananke.Federation.Tests;

[TestFixture]
public sealed class InMemoryDeploymentRegistryTests
{
    private InMemoryDeploymentRegistry _registry = null!;

    [SetUp]
    public void SetUp() => _registry = new InMemoryDeploymentRegistry();

    private static DeploymentRecord MakeRecord(
        string id = "dep-1",
        string workflow = "test-workflow",
        DeploymentStatus status = DeploymentStatus.Pending) => new()
    {
        DeploymentId = id,
        WorkflowName = workflow,
        Platform = "vertex-ai",
        Version = "1.0.0",
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    [Test]
    public async Task Register_and_Get_roundtrips()
    {
        var record = MakeRecord();
        await _registry.RegisterAsync(record);

        var result = await _registry.GetAsync("dep-1");
        result.ShouldNotBeNull();
        result.WorkflowName.ShouldBe("test-workflow");
    }

    [Test]
    public async Task Get_returns_null_for_unknown_id()
    {
        var result = await _registry.GetAsync("nonexistent");
        result.ShouldBeNull();
    }

    [Test]
    public async Task Register_duplicate_throws()
    {
        await _registry.RegisterAsync(MakeRecord());
        await Should.ThrowAsync<InvalidOperationException>(
            () => _registry.RegisterAsync(MakeRecord()));
    }

    [Test]
    public async Task List_returns_all_records()
    {
        await _registry.RegisterAsync(MakeRecord("dep-1"));
        await _registry.RegisterAsync(MakeRecord("dep-2", "other-workflow"));

        var all = await _registry.ListAsync();
        all.Count.ShouldBe(2);
    }

    [Test]
    public async Task List_filters_by_workflow_name()
    {
        await _registry.RegisterAsync(MakeRecord("dep-1", "alpha"));
        await _registry.RegisterAsync(MakeRecord("dep-2", "beta"));

        var filtered = await _registry.ListAsync("alpha");
        filtered.Count.ShouldBe(1);
        filtered[0].WorkflowName.ShouldBe("alpha");
    }

    [Test]
    public async Task UpdateStatus_changes_status()
    {
        await _registry.RegisterAsync(MakeRecord());
        await _registry.UpdateStatusAsync("dep-1", DeploymentStatus.Active);

        var result = await _registry.GetAsync("dep-1");
        result!.Status.ShouldBe(DeploymentStatus.Active);
    }

    [Test]
    public async Task UpdateStatus_throws_for_unknown_id()
    {
        await Should.ThrowAsync<KeyNotFoundException>(
            () => _registry.UpdateStatusAsync("nonexistent", DeploymentStatus.Failed));
    }
}
