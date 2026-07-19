using Ananke.Abstractions.Agents;
using Ananke.Design;
using Ananke.Organics.Kernel;
using Ananke.Organics.Kernel.Snapshots;
using Ananke.Organics.Division;
using Ananke.TestHelpers;
using Shouldly;

namespace Ananke.Organics.Tests.Kernel;

[TestFixture]
public class WorkflowReplicatorTests
{
    private InProcessWorkflowHost _host = null!;

    [SetUp]
    public void SetUp()
    {
        _host = new InProcessWorkflowHost();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _host.DisposeAsync();
    }

    // ── Core replication ─────────────────────────────────────────────────────────────

    [Test]
    public async Task ReplicateAsync_SpawnsClone()
    {
        await StartSourceAsync("source");
        var replicator = CreateReplicator();

        await replicator.ReplicateAsync("source", "clone");

        _host.ListActive().ShouldContain("clone");
    }

    [Test]
    public async Task ReplicateAsync_OriginalKeepsRunning()
    {
        await StartSourceAsync("source");
        var replicator = CreateReplicator();

        await replicator.ReplicateAsync("source", "clone");

        _host.ListActive().ShouldContain("source");
    }

    [Test]
    public async Task ReplicateAsync_ReturnsManifest()
    {
        await StartSourceAsync("source");
        var replicator = CreateReplicator();

        var result = await replicator.ReplicateAsync("source", "clone");

        result.Manifest.Name.ShouldBe("source");
        result.ClonedFrom.ShouldBe("source");
    }

    [Test]
    public async Task ReplicateAsync_ReturnsMemoryProfile()
    {
        await StartSourceAsync("source");
        var profile = new MemoryProfile
        {
            Domains = ["search", "general"],
            LineageTags = ["bookstore"]
        };
        var replicator = CreateReplicator(memoryProfileFactory: _ => profile);

        var result = await replicator.ReplicateAsync("source", "clone");

        result.MemoryProfile.Domains.ShouldContain("search");
        result.MemoryProfile.Domains.ShouldContain("general");
        result.MemoryProfile.LineageTags.ShouldContain("bookstore");
    }

    [Test]
    public async Task ReplicateAsync_DefaultMemoryProfile_HasGeneralDomain()
    {
        await StartSourceAsync("source");
        var replicator = CreateReplicator();

        var result = await replicator.ReplicateAsync("source", "clone");

        result.MemoryProfile.Domains.ShouldContain("general");
    }

    // ── Null guards ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task ReplicateAsync_NullSource_Throws()
    {
        var replicator = CreateReplicator();
        await Should.ThrowAsync<ArgumentException>(
            () => replicator.ReplicateAsync(null!, "clone"));
    }

    [Test]
    public async Task ReplicateAsync_NullCloneName_Throws()
    {
        var replicator = CreateReplicator();
        await Should.ThrowAsync<ArgumentException>(
            () => replicator.ReplicateAsync("source", null!));
    }

    [Test]
    public async Task ReplicateAsync_DuplicateCloneName_Throws()
    {
        await StartSourceAsync("source");
        var replicator = CreateReplicator();

        // First clone succeeds
        await replicator.ReplicateAsync("source", "clone");

        // Second clone with same name fails (host rejects duplicate)
        await Should.ThrowAsync<InvalidOperationException>(
            () => replicator.ReplicateAsync("source", "clone"));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────

    private WorkflowReplicator CreateReplicator(
        Func<string, MemoryProfile>? memoryProfileFactory = null) =>
        new(
            _host,
            new StubActivatorFactory(),
            BuildManifest,
            memoryProfileFactory);

    private Task StartSourceAsync(string name) =>
        _host.StartAsync(name, async ct =>
        {
            await WorkflowLoops.Spin(ct);
        });

    private static WorkflowManifest BuildManifest(string name) => new()
    {
        Name = name,
        Models = new Dictionary<string, ModelDefinition>
        {
            ["default"] = new() { Provider = "openai", Model = Models.OpenAI.Gpt54Mini }
        },
        Jobs = new Dictionary<string, JobDefinition>
        {
            ["handle"] = new() { Type = "agent", ModelAlias = "default" },
            ["process"] = new() { Type = "code" }
        },
        Connections = ["handle -> process", "process -> End"]
    };

    // ── Test doubles ────────────────────────────────────────────────────────────────

    private sealed class StubActivatorFactory : IWorkflowActivatorFactory
    {
        public Func<CancellationToken, Task> CreateLoop(
            WorkflowSnapshot snapshot, MemoryProfile? memoryProfile = null) =>
            WorkflowLoops.Spin;
    }
}
