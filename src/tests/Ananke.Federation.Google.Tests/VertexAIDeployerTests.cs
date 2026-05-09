using Ananke.Design;
using Ananke.Federation.Credentials;
using Ananke.Federation.Deployment;
using Ananke.Federation.Google;
using Ananke.Federation.Google.AgentRuntime;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Federation.Google.Tests;

[TestFixture]
public sealed class VertexAIDeployerTests
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static WorkflowManifest MakeManifest(string provider = "google", string model = "gemini-3.1-flash") =>
        WorkflowManifest.Parse([
            "name: test-workflow",
            "models:",
            "  default:",
           $"    provider: {provider}",
           $"    model: {model}",
            "jobs:",
            "  agent1:",
            "    type: agent",
            "    model: default",
            "connections:",
            "  - agent1",
        ]);

    private static WorkflowManifest MakeManifestWithUnknownModel() =>
        MakeManifest(provider: "unknown", model: "mystery-v1");

    private static VertexAICredentialProvider MakeCredProvider() =>
        new("test-project", "us-central1");

    private static VertexAIDeployer MakeDeployer(
        IDeploymentRegistry registry,
        IAgentRuntimeClient? runtimeClient = null,
        IFederationCredentialProvider? credProvider = null) =>
        new(credProvider ?? new FakeCredentialProvider(), registry,
            modelMapper: null, toolSchemaTranslator: null, systemPromptCompiler: null,
            agentRuntimeClient: runtimeClient);

    // ─────────────────────────────────────────────────────────────────────────
    //  Fake Agent Runtime client
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class FakeAgentRuntimeClient : IAgentRuntimeClient
    {
        private int _createCount;
        private int _deleteCount;
        private string _resourceName;

        public FakeAgentRuntimeClient(string resourceName = "projects/test/locations/us-central1/agents/fake-001")
            => _resourceName = resourceName;

        public int CreateCount => _createCount;
        public int DeleteCount => _deleteCount;
        public AgentDefinition? LastDefinition { get; private set; }

        public Task<string> CreateAgentAsync(AgentDefinition definition, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _createCount);
            LastDefinition = definition;
            return Task.FromResult(_resourceName);
        }

        public Task DeleteAgentAsync(string resourceName, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _deleteCount);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingAgentRuntimeClient : IAgentRuntimeClient
    {
        public Task<string> CreateAgentAsync(AgentDefinition definition, CancellationToken ct = default) =>
            throw new HttpRequestException("Simulated Agent Runtime API failure.");

        public Task DeleteAgentAsync(string resourceName, CancellationToken ct = default) =>
            throw new HttpRequestException("Simulated Agent Runtime API failure.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Validation failure → throws before touching the runtime
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Validation_failure_throws_without_calling_runtime()
    {
        var registry = new InMemoryDeploymentRegistry();
        var runtime = new FakeAgentRuntimeClient();
        var deployer = MakeDeployer(registry, runtime);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => deployer.DeployAsync(MakeManifestWithUnknownModel(), new ToolKit("test"),
                new DeployOptions { Platform = AgentPlatformConstants.Platform }));

        ex.Message.ShouldContain("not deployable");
        runtime.CreateCount.ShouldBe(0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ADC missing (credential provider returns null) → throws, record = Failed
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Missing_credentials_throws_before_deploying()
    {
        var registry = new InMemoryDeploymentRegistry();

        var noCredProvider = new NullCredentialProvider();
        var deployer = MakeDeployer(registry, new FakeAgentRuntimeClient(), noCredProvider);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => deployer.DeployAsync(MakeManifest(), new ToolKit("test"),
                new DeployOptions { Platform = AgentPlatformConstants.Platform }));

        ex.Message.ShouldContain("not deployable");

        // Validator short-circuits on FED030 before RegisterAsync is called
        var records = await registry.ListAsync("test-workflow");
        records.ShouldBeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Happy-path deploy → Active record with PlatformResourceId
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task DeployAsync_happy_path_returns_Active_record()
    {
        const string expectedResourceName = "projects/test-project/locations/us-central1/agents/abc123";
        var registry = new InMemoryDeploymentRegistry();
        var runtime = new FakeAgentRuntimeClient(expectedResourceName);
        var deployer = MakeDeployer(registry, runtime);

        var record = await deployer.DeployAsync(
            MakeManifest(), new ToolKit("test"),
            new DeployOptions { Platform = AgentPlatformConstants.Platform });

        record.Status.ShouldBe(DeploymentStatus.Active);
        record.PlatformResourceId.ShouldBe(expectedResourceName);
        record.Platform.ShouldBe(AgentPlatformConstants.Platform);
        record.WorkflowName.ShouldBe("test-workflow");

        runtime.CreateCount.ShouldBe(1);
        runtime.LastDefinition!.Model.ShouldBe("gemini-3.1-flash");
        runtime.LastDefinition.DisplayName.ShouldBe("test-workflow/agent1");
        runtime.LastDefinition.SystemInstructions.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task DeployAsync_happy_path_registers_Active_status_in_registry()
    {
        var registry = new InMemoryDeploymentRegistry();
        var deployer = MakeDeployer(registry, new FakeAgentRuntimeClient());

        var record = await deployer.DeployAsync(
            MakeManifest(), new ToolKit("test"),
            new DeployOptions { Platform = AgentPlatformConstants.Platform });

        var stored = await registry.GetAsync(record.DeploymentId);
        stored.ShouldNotBeNull();
        stored!.Status.ShouldBe(DeploymentStatus.Active);
    }

    [Test]
    public async Task DeployAsync_maps_OpenAI_model_to_Gemini_equivalent()
    {
        var registry = new InMemoryDeploymentRegistry();
        var runtime = new FakeAgentRuntimeClient();
        var deployer = MakeDeployer(registry, runtime);

        await deployer.DeployAsync(
            MakeManifest(provider: "openai", model: "gpt-4.1"),
            new ToolKit("test"),
            new DeployOptions { Platform = AgentPlatformConstants.Platform });

        runtime.LastDefinition!.Model.ShouldBe("gemini-3.1-pro");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Runtime API failure → record = Failed
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Runtime_failure_sets_record_to_Failed()
    {
        var registry = new InMemoryDeploymentRegistry();
        var deployer = MakeDeployer(registry, new ThrowingAgentRuntimeClient());

        await Should.ThrowAsync<Exception>(
            () => deployer.DeployAsync(MakeManifest(), new ToolKit("test"),
                new DeployOptions { Platform = AgentPlatformConstants.Platform }));

        var records = await registry.ListAsync("test-workflow");
        records.ShouldHaveSingleItem();
        records[0].Status.ShouldBe(DeploymentStatus.Failed);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Teardown happy-path → Stopped, DeleteAgent called
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task TeardownAsync_sets_record_to_Stopped_and_calls_delete()
    {
        const string resourceName = "projects/test/locations/us-central1/agents/abc";
        var registry = new InMemoryDeploymentRegistry();
        var runtime = new FakeAgentRuntimeClient(resourceName);
        var deployer = MakeDeployer(registry, runtime);

        // Register a pre-deployed record that already has a PlatformResourceId
        var record = new DeploymentRecord
        {
            DeploymentId = "vertex-ai-test-workflow-20260101120000",
            WorkflowName = "test-workflow",
            Platform = AgentPlatformConstants.Platform,
            Version = "1.0.0",
            Status = DeploymentStatus.Active,
            PlatformResourceId = resourceName,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await registry.RegisterAsync(record);

        await deployer.TeardownAsync(record.DeploymentId);

        runtime.DeleteCount.ShouldBe(1);

        var stored = await registry.GetAsync(record.DeploymentId);
        stored!.Status.ShouldBe(DeploymentStatus.Stopped);
    }

    [Test]
    public async Task TeardownAsync_without_resource_id_still_sets_Stopped()
    {
        var registry = new InMemoryDeploymentRegistry();
        // Register a record manually with no PlatformResourceId
        var record = new DeploymentRecord
        {
            DeploymentId = "test-deploy-001",
            WorkflowName = "test-workflow",
            Platform = AgentPlatformConstants.Platform,
            Version = "1.0.0",
            Status = DeploymentStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await registry.RegisterAsync(record);

        var runtime = new FakeAgentRuntimeClient();
        var deployer = MakeDeployer(registry, runtime);

        await deployer.TeardownAsync("test-deploy-001");

        runtime.DeleteCount.ShouldBe(0);
        var stored = await registry.GetAsync("test-deploy-001");
        stored!.Status.ShouldBe(DeploymentStatus.Stopped);
    }

    [Test]
    public void TeardownAsync_unknown_deployment_throws()
    {
        var registry = new InMemoryDeploymentRegistry();
        var deployer = MakeDeployer(registry, new FakeAgentRuntimeClient());

        Should.Throw<KeyNotFoundException>(
            async () => await deployer.TeardownAsync("does-not-exist"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Alias: gemini-agent-platform is accepted as platform string
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task DeployAsync_accepts_gemini_agent_platform_alias()
    {
        var registry = new InMemoryDeploymentRegistry();
        var deployer = MakeDeployer(registry, new FakeAgentRuntimeClient());

        // Should not throw — alias is accepted
        var record = await deployer.DeployAsync(
            MakeManifest(), new ToolKit("test"),
            new DeployOptions { Platform = AgentPlatformConstants.PlatformAlias });

        record.Status.ShouldBe(DeploymentStatus.Active);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Null credential provider helper
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>A credential provider that always fails to return credentials.</summary>
    private sealed class NullCredentialProvider : IFederationCredentialProvider
    {
        public string Platform => AgentPlatformConstants.Platform;

        public Task<object?> GetCredentialAsync(string platform, CancellationToken ct = default) =>
            Task.FromResult<object?>(null);

        public Task<bool> ValidateAsync(CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    /// <summary>A credential provider that always returns a non-null dummy credential, bypassing real ADC.</summary>
    private sealed class FakeCredentialProvider : IFederationCredentialProvider
    {
        public string Platform => AgentPlatformConstants.Platform;

        public Task<object?> GetCredentialAsync(string platform, CancellationToken ct = default) =>
            Task.FromResult<object?>(new object());

        public Task<bool> ValidateAsync(CancellationToken ct = default) =>
            Task.FromResult(true);
    }
}
