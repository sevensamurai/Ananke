using Ananke.Orchestration.Agents;
using Shouldly;

namespace Ananke.Design.Tests;

[TestFixture]
public class ModelResolverTests
{
    // ── Registration ─────────────────────────────────────────────────

    [Test]
    public void Register_TwoParamFactory_Succeeds()
    {
        var resolver = new ModelResolver()
            .Register("openai", "OpenAI", (apiKey, model) => new FakeAgentModel(apiKey, model));

        resolver.ShouldNotBeNull();
    }

    [Test]
    public void Register_ThreeParamFactory_Succeeds()
    {
        var resolver = new ModelResolver()
            .Register("openai", "OpenAI", (apiKey, model, endpoint) => new FakeAgentModel(apiKey, model));

        resolver.ShouldNotBeNull();
    }

    [Test]
    public void Register_NullProvider_Throws()
    {
        Should.Throw<ArgumentException>(() =>
            new ModelResolver().Register(null!, "Section", (k, m) => new FakeAgentModel(k, m)));
    }

    [Test]
    public void Register_NullFactory_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            new ModelResolver().Register("openai", "OpenAI", (Func<string, string, IAgentModel>)null!));
    }

    // ── Resolve ──────────────────────────────────────────────────────

    [Test]
    public void Resolve_SingleModel_ReturnsInstance()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  fast:",
            "    provider: openai",
            "    model: gpt-4.1-mini",
            "jobs:",
            "connections:",
        ]);

        var resolver = new ModelResolver()
            .Register("openai", "OpenAI", (apiKey, model) => new FakeAgentModel(apiKey, model));

        var models = resolver.Resolve(manifest, key => key switch
        {
            "OpenAI:ApiKey" => "sk-test-key",
            _ => null
        });

        models.Count.ShouldBe(1);
        models.ShouldContainKey("fast");

        var fake = models["fast"].ShouldBeOfType<FakeAgentModel>();
        fake.ApiKey.ShouldBe("sk-test-key");
        fake.Model.ShouldBe("gpt-4.1-mini");
    }

    [Test]
    public void Resolve_ConfigModelOverridesYaml()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  fast:",
            "    provider: openai",
            "    model: gpt-4.1-mini",
            "jobs:",
            "connections:",
        ]);

        var resolver = new ModelResolver()
            .Register("openai", "OpenAI", (apiKey, model) => new FakeAgentModel(apiKey, model));

        var models = resolver.Resolve(manifest, key => key switch
        {
            "OpenAI:ApiKey" => "sk-test-key",
            "OpenAI:Model" => "gpt-4.1",
            _ => null
        });

        var fake = models["fast"].ShouldBeOfType<FakeAgentModel>();
        fake.Model.ShouldBe("gpt-4.1");
    }

    [Test]
    public void Resolve_WithEndpoint_PassedToFactory()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  local:",
            "    provider: openai",
            "    model: llama3",
            "    endpoint: http://localhost:11434/v1",
            "jobs:",
            "connections:",
        ]);

        Uri? capturedEndpoint = null;
        var resolver = new ModelResolver()
            .Register("openai", "OpenAI", (string apiKey, string model, Uri? endpoint) =>
            {
                capturedEndpoint = endpoint;
                return new FakeAgentModel(apiKey, model);
            });

        resolver.Resolve(manifest, key => key switch
        {
            "OpenAI:ApiKey" => "sk-test",
            _ => null
        });

        capturedEndpoint.ShouldNotBeNull();
        capturedEndpoint!.ToString().ShouldBe("http://localhost:11434/v1");
    }

    [Test]
    public void Resolve_EndpointFromConfig_WhenYamlEmpty()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  local:",
            "    provider: openai",
            "    model: llama3",
            "jobs:",
            "connections:",
        ]);

        Uri? capturedEndpoint = null;
        var resolver = new ModelResolver()
            .Register("openai", "OpenAI", (string apiKey, string model, Uri? endpoint) =>
            {
                capturedEndpoint = endpoint;
                return new FakeAgentModel(apiKey, model);
            });

        resolver.Resolve(manifest, key => key switch
        {
            "OpenAI:ApiKey" => "sk-test",
            "OpenAI:Endpoint" => "http://config-endpoint:8080/v1",
            _ => null
        });

        capturedEndpoint.ShouldNotBeNull();
        capturedEndpoint!.ToString().ShouldBe("http://config-endpoint:8080/v1");
    }

    // ── Error cases ──────────────────────────────────────────────────

    [Test]
    public void Resolve_UnregisteredProvider_Throws()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  x:",
            "    provider: unknown_provider",
            "jobs:",
            "connections:",
        ]);

        var resolver = new ModelResolver();

        var ex = Should.Throw<InvalidOperationException>(() =>
            resolver.Resolve(manifest, _ => null));
        ex.Message.ShouldContain("unknown_provider");
    }

    [Test]
    public void Resolve_MissingApiKey_Throws()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  fast:",
            "    provider: openai",
            "jobs:",
            "connections:",
        ]);

        var resolver = new ModelResolver()
            .Register("openai", "OpenAI", (apiKey, model) => new FakeAgentModel(apiKey, model));

        var ex = Should.Throw<InvalidOperationException>(() =>
            resolver.Resolve(manifest, _ => null));
        ex.Message.ShouldContain("ApiKey");
    }

    [Test]
    public void Resolve_MultipleModels_AllResolved()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  fast:",
            "    provider: openai",
            "    model: gpt-4.1-mini",
            "  smart:",
            "    provider: anthropic",
            "    model: claude-sonnet-4-20250514",
            "jobs:",
            "connections:",
        ]);

        var resolver = new ModelResolver()
            .Register("openai", "OpenAI", (apiKey, model) => new FakeAgentModel(apiKey, model))
            .Register("anthropic", "Anthropic", (apiKey, model) => new FakeAgentModel(apiKey, model));

        var models = resolver.Resolve(manifest, key => key switch
        {
            "OpenAI:ApiKey" => "sk-openai",
            "Anthropic:ApiKey" => "sk-anthropic",
            _ => null
        });

        models.Count.ShouldBe(2);
        models.ShouldContainKey("fast");
        models.ShouldContainKey("smart");
    }

    // ── Test helpers ─────────────────────────────────────────────────

    private sealed class FakeAgentModel(string apiKey, string model) : IAgentModel
    {
        public string ApiKey { get; } = apiKey;
        public string Model { get; } = model;

        public Task<AgentResponse> GenerateAsync(
            AgentRequest request,
            CancellationToken ct = default) =>
            throw new NotImplementedException();
    }
}
