using System.Net;
using System.Text.Json.Nodes;
using Ananke.Design;
using Ananke.Federation.Anthropic;
using Ananke.Federation.Deployment;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Federation.Anthropic.Tests;

[TestFixture]
public sealed class ClaudeDeployerTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static WorkflowManifest MakeManifest(string workflowName = "wf") =>
        WorkflowManifest.Parse([
            $"name: {workflowName}",
            "models:",
            "  default:",
            "    provider: anthropic",
            "    model: claude-sonnet-4",
            "jobs:",
            "  agent1:",
            "    type: agent",
            "    model: default",
            "connections:",
            "  - agent1",
        ]);

    private static ClaudeDeployer MakeDeployer(
        IDeploymentRegistry? registry = null,
        Func<string, ClaudeManagedAgentsClient>? clientFactory = null)
    {
        var cred = new ClaudeCredentialProvider("sk-ant-test");
        return new ClaudeDeployer(
            cred,
            registry ?? new InMemoryDeploymentRegistry(),
            clientFactory: clientFactory);
    }

    private static Func<string, ClaudeManagedAgentsClient> FakeClientFactory(
        string envId = "env-1", string agentId = "agent-1")
    {
        return _ =>
        {
            var handler = new FakeHttpMessageHandler(envId, agentId);
            return new ClaudeManagedAgentsClient("sk-ant-test", new HttpClient(handler)
            {
                BaseAddress = new Uri(ClaudeManagedAgentsClient.BaseUrl)
            });
        };
    }

    // ── Platform ──────────────────────────────────────────────────────────────

    [Test]
    public void Platform_is_claude()
    {
        MakeDeployer().Platform.ShouldBe("claude");
    }

    // ── SerializeResourceIds / DeserializeResourceIds ─────────────────────────

    [Test]
    public void SerializeDeserialize_roundtrips_correctly()
    {
        var json = ClaudeDeployer.SerializeResourceIds("env-abc", ["agent-1", "agent-2"]);
        var (envId, agentIds) = ClaudeDeployer.DeserializeResourceIds(json);
        envId.ShouldBe("env-abc");
        agentIds.ShouldBe(["agent-1", "agent-2"]);
    }

    [Test]
    public void DeserializeResourceIds_returns_empty_on_invalid_json()
    {
        var (envId, agentIds) = ClaudeDeployer.DeserializeResourceIds("not-json");
        envId.ShouldBeNull();
        agentIds.ShouldBeEmpty();
    }

    [Test]
    public void DeserializeResourceIds_empty_agents_array()
    {
        var json = ClaudeDeployer.SerializeResourceIds("env-x", []);
        var (envId, agentIds) = ClaudeDeployer.DeserializeResourceIds(json);
        envId.ShouldBe("env-x");
        agentIds.ShouldBeEmpty();
    }

    // ── BuildAgentRequestBody ─────────────────────────────────────────────────

    [Test]
    public void BuildAgentRequestBody_produces_expected_shape()
    {
        var tools = new JsonArray(new JsonObject { ["name"] = "my_tool" });
        var body = ClaudeDeployer.BuildAgentRequestBody("claude-sonnet-4-5", "do stuff", tools);
        var obj = JsonNode.Parse(body)!.AsObject();
        obj["model"]!.GetValue<string>().ShouldBe("claude-sonnet-4-5");
        obj["system"]!.GetValue<string>().ShouldBe("do stuff");
        obj["tools"]!.AsArray().Count.ShouldBe(1);
    }

    // ── ValidateAsync ─────────────────────────────────────────────────────────

    [Test]
    public async Task ValidateAsync_returns_deployable_for_valid_manifest()
    {
        var deployer = MakeDeployer();
        var report = await deployer.ValidateAsync(MakeManifest(), new ToolKit("wf"));
        report.IsDeployable.ShouldBeTrue();
    }

    [Test]
    public async Task ValidateAsync_error_FED050_when_no_api_key()
    {
        // Override env var just in case — use a provider with no key
        var cred = new ClaudeCredentialProvider(null);
        var deployer = new ClaudeDeployer(cred, new InMemoryDeploymentRegistry());
        var savedKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
            var report = await deployer.ValidateAsync(MakeManifest(), new ToolKit("wf"));
            report.IsDeployable.ShouldBeFalse();
            report.Errors.ShouldContain(e => e.Code == "FED050");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", savedKey);
        }
    }

    // ── DeployAsync ───────────────────────────────────────────────────────────

    [Test]
    public async Task DeployAsync_returns_active_record_with_platform_resource_id()
    {
        var registry = new InMemoryDeploymentRegistry();
        var deployer = MakeDeployer(registry, FakeClientFactory("env-abc", "agent-xyz"));

        var record = await deployer.DeployAsync(MakeManifest(), new ToolKit("wf"), new DeployOptions { Platform = "claude" });

        record.Status.ShouldBe(DeploymentStatus.Active);
        record.Platform.ShouldBe("claude");
        record.PlatformResourceId.ShouldNotBeNullOrEmpty();

        var (envId, agentIds) = ClaudeDeployer.DeserializeResourceIds(record.PlatformResourceId!);
        envId.ShouldBe("env-abc");
        agentIds.ShouldContain("agent-xyz");
    }

    [Test]
    public async Task DeployAsync_persists_active_record_in_registry()
    {
        var registry = new InMemoryDeploymentRegistry();
        var deployer = MakeDeployer(registry, FakeClientFactory("env-1", "agent-1"));

        var returned = await deployer.DeployAsync(MakeManifest(), new ToolKit("wf"), new DeployOptions { Platform = "claude" });

        var stored = await registry.GetAsync(returned.DeploymentId);
        stored.ShouldNotBeNull();
        stored!.Status.ShouldBe(DeploymentStatus.Active);
        stored.PlatformResourceId.ShouldBe(returned.PlatformResourceId);
    }

    [Test]
    public async Task DeployAsync_marks_failed_when_api_throws()
    {
        var registry = new InMemoryDeploymentRegistry();
        var cred = new ClaudeCredentialProvider("sk-ant-test");
        var deployer = new ClaudeDeployer(cred, registry,
            clientFactory: _ =>
            {
                var handler = new FakeHttpMessageHandler(throwOn: HttpMethod.Post);
                return new ClaudeManagedAgentsClient("sk-ant-test", new HttpClient(handler)
                {
                    BaseAddress = new Uri(ClaudeManagedAgentsClient.BaseUrl)
                });
            });

        await Should.ThrowAsync<HttpRequestException>(
            () => deployer.DeployAsync(MakeManifest(), new ToolKit("wf"), new DeployOptions { Platform = "claude" }));

        var records = await registry.ListAsync();
        records.ShouldContain(r => r.Status == DeploymentStatus.Failed);
    }

    // ── TeardownAsync ─────────────────────────────────────────────────────────

    [Test]
    public async Task TeardownAsync_marks_deployment_stopped()
    {
        var registry = new InMemoryDeploymentRegistry();
        var deployer = MakeDeployer(registry, FakeClientFactory("env-1", "agent-1"));

        var deployed = await deployer.DeployAsync(MakeManifest(), new ToolKit("wf"), new DeployOptions { Platform = "claude" });
        await deployer.TeardownAsync(deployed.DeploymentId);

        var stored = await registry.GetAsync(deployed.DeploymentId);
        stored!.Status.ShouldBe(DeploymentStatus.Stopped);
    }

    [Test]
    public async Task TeardownAsync_throws_when_deployment_not_found()
    {
        var deployer = MakeDeployer(clientFactory: FakeClientFactory());
        await Should.ThrowAsync<KeyNotFoundException>(() => deployer.TeardownAsync("nonexistent-id"));
    }

    [Test]
    public async Task TeardownAsync_succeeds_without_platform_resource_id()
    {
        // Deploy a record directly into the registry with no PlatformResourceId
        var registry = new InMemoryDeploymentRegistry();
        var record = new DeploymentRecord
        {
            DeploymentId = "dep-bare",
            WorkflowName = "wf",
            Platform = "claude",
            Version = "1.0.0",
            Status = DeploymentStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await registry.RegisterAsync(record);

        var deployer = MakeDeployer(registry, FakeClientFactory());
        await deployer.TeardownAsync("dep-bare");

        var stored = await registry.GetAsync("dep-bare");
        stored!.Status.ShouldBe(DeploymentStatus.Stopped);
    }
}

/// <summary>
/// Minimal fake HTTP handler for Anthropic API interactions in tests.
/// Returns canned IDs for create calls and 200 OK for deletes.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly string _envId;
    private readonly string _agentId;
    private readonly HttpMethod? _throwOnMethod;

    public FakeHttpMessageHandler(string envId = "env-1", string agentId = "agent-1", HttpMethod? throwOn = null)
    {
        _envId = envId;
        _agentId = agentId;
        _throwOnMethod = throwOn;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (_throwOnMethod is not null && request.Method == _throwOnMethod)
            return Task.FromException<HttpResponseMessage>(
                new HttpRequestException("Anthropic API error 500: simulated failure",
                    inner: null, statusCode: HttpStatusCode.InternalServerError));

        if (request.Method == HttpMethod.Delete)
            return Ok("{}");

        if (request.Method == HttpMethod.Get)
            return Ok("""{"data":[]}""");

        // POST — derive which resource type from the path
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/environments", StringComparison.Ordinal))
            return Ok($$"""{"id":"{{_envId}}","type":"environment"}""");

        if (path.EndsWith("/agents", StringComparison.Ordinal))
            return Ok($$"""{"id":"{{_agentId}}","type":"agent"}""");

        return Ok("{}");
    }

    private static Task<HttpResponseMessage> Ok(string json)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
