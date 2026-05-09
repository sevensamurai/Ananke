using System.Text.Json;
using System.Text.Json.Serialization;
using Ananke.Federation.Deployment;
using Shouldly;

namespace Ananke.Federation.Tests.Deployment;

[TestFixture]
public sealed class JsonFileDeploymentRegistryTests
{
    private string _filePath = null!;
    private JsonFileDeploymentRegistry _registry = null!;

    [SetUp]
    public void SetUp()
    {
        _filePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        _registry = new JsonFileDeploymentRegistry(_filePath);
    }

    [TearDown]
    public void TearDown()
    {
        _registry.Dispose();
        if (File.Exists(_filePath)) File.Delete(_filePath);
        var tmp = _filePath + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
    }

    private static DeploymentRecord MakeRecord(
        string id = "dep-1",
        string workflow = "test-workflow",
        string platform = "vertex-ai",
        DeploymentStatus status = DeploymentStatus.Pending) => new()
    {
        DeploymentId = id,
        WorkflowName = workflow,
        Platform = platform,
        Version = "1.0.0",
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    // ── basic CRUD ────────────────────────────────────────────────────────────

    [Test]
    public async Task Register_and_Get_roundtrips()
    {
        var record = MakeRecord();
        await _registry.RegisterAsync(record);

        var result = await _registry.GetAsync("dep-1");

        result.ShouldNotBeNull();
        result.WorkflowName.ShouldBe("test-workflow");
        result.Platform.ShouldBe("vertex-ai");
    }

    [Test]
    public async Task Get_returns_null_for_unknown_id()
    {
        var result = await _registry.GetAsync("nonexistent");
        result.ShouldBeNull();
    }

    [Test]
    public async Task Register_duplicate_throws_InvalidOperationException()
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
    public async Task UpdateStatus_changes_status_and_updates_timestamp()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        await _registry.RegisterAsync(MakeRecord(status: DeploymentStatus.Pending));
        await _registry.UpdateStatusAsync("dep-1", DeploymentStatus.Active);

        var result = await _registry.GetAsync("dep-1");

        result!.Status.ShouldBe(DeploymentStatus.Active);
        result.UpdatedAt.ShouldBeGreaterThan(before);
    }

    [Test]
    public async Task UpdateStatus_throws_KeyNotFoundException_for_unknown_id()
    {
        await Should.ThrowAsync<KeyNotFoundException>(
            () => _registry.UpdateStatusAsync("nonexistent", DeploymentStatus.Failed));
    }

    // ── persistence ───────────────────────────────────────────────────────────

    [Test]
    public async Task Data_persists_across_registry_instances()
    {
        await _registry.RegisterAsync(MakeRecord("dep-persist", "persist-workflow"));
        _registry.Dispose();

        using var second = new JsonFileDeploymentRegistry(_filePath);
        var result = await second.GetAsync("dep-persist");

        result.ShouldNotBeNull();
        result.WorkflowName.ShouldBe("persist-workflow");
    }

    [Test]
    public async Task File_is_created_when_it_does_not_exist()
    {
        File.Exists(_filePath).ShouldBeFalse();

        await _registry.RegisterAsync(MakeRecord());

        File.Exists(_filePath).ShouldBeTrue();
    }

    // ── atomic write ─────────────────────────────────────────────────────────

    [Test]
    public async Task Write_is_atomic_no_tmp_file_left_behind()
    {
        await _registry.RegisterAsync(MakeRecord());

        File.Exists(_filePath + ".tmp").ShouldBeFalse();
        File.Exists(_filePath).ShouldBeTrue();
    }

    // ── schema version ────────────────────────────────────────────────────────

    [Test]
    public async Task Schema_version_header_is_written()
    {
        await _registry.RegisterAsync(MakeRecord());

        var json = File.ReadAllText(_filePath);
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("schemaVersion").GetInt32()
            .ShouldBe(JsonFileDeploymentRegistry.CurrentSchemaVersion);
    }

    [Test]
    public async Task Schema_version_is_current()
    {
        JsonFileDeploymentRegistry.CurrentSchemaVersion.ShouldBe(1);
        await Task.CompletedTask;
    }

    // ── concurrency ───────────────────────────────────────────────────────────

    [Test]
    public async Task Concurrent_registrations_from_multiple_instances_do_not_corrupt_file()
    {
        const int instanceCount = 4;
        var tasks = Enumerable.Range(0, instanceCount).Select(i => Task.Run(async () =>
        {
            using var reg = new JsonFileDeploymentRegistry(_filePath);
            await reg.RegisterAsync(MakeRecord($"dep-{i}", $"workflow-{i}"));
        }));

        await Task.WhenAll(tasks);

        using var reader = new JsonFileDeploymentRegistry(_filePath);
        var all = await reader.ListAsync();
        all.Count.ShouldBe(instanceCount);
        all.Select(r => r.DeploymentId).ShouldBeUnique();
    }
}
