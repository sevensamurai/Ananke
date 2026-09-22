using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
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
            "OpenAI:Model" => Models.OpenAI.Gpt55,
            _ => null
        });

        var fake = models["fast"].ShouldBeOfType<FakeAgentModel>();
        fake.Model.ShouldBe(Models.OpenAI.Gpt55);
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

    // ── An endpoint makes the key optional ───────────────────────────
    //
    // A self-hosted OpenAI-compatible server has no key to give. Requiring one made the documented
    // local-first path fail before it ran, on a secret the user had to invent.

    [Test]
    public void Resolve_EndpointWithoutApiKey_UsesPlaceholderInsteadOfThrowing()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  local:",
            "    provider: openai",
            "    model: llama3.2:1b",
            "    endpoint: http://model-host:11434/v1",
            "jobs:",
            "connections:",
        ]);

        var resolver = new ModelResolver()
            .Register("openai", "OpenAI", (string apiKey, string model, Uri? endpoint) =>
                new FakeAgentModel(apiKey, model));

        var resolved = resolver.Resolve(manifest, _ => null);

        ((FakeAgentModel)resolved["local"]).ApiKey.ShouldBe(ModelResolver.PlaceholderApiKey);
    }

    [Test]
    public void Resolve_EndpointFromConfigWithoutApiKey_UsesPlaceholder()
    {
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  local:",
            "    provider: openai",
            "    model: llama3.2:1b",
            "jobs:",
            "connections:",
        ]);

        var resolver = new ModelResolver()
            .Register("openai", "OpenAI", (string apiKey, string model, Uri? endpoint) =>
                new FakeAgentModel(apiKey, model));

        var resolved = resolver.Resolve(manifest, key => key switch
        {
            "OpenAI:Endpoint" => "http://model-host:11434/v1",
            _ => null
        });

        ((FakeAgentModel)resolved["local"]).ApiKey.ShouldBe(ModelResolver.PlaceholderApiKey);
    }

    [Test]
    public void Resolve_EndpointWithApiKey_PrefersTheConfiguredKey()
    {
        // Hosted OpenAI-compatible providers — Groq, Together, Foundry — have both an endpoint and
        // a real key. The placeholder must never displace one that was configured.
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  hosted:",
            "    provider: openai",
            "    model: llama-3.3-70b",
            "    endpoint: https://api.example.com/v1",
            "jobs:",
            "connections:",
        ]);

        var resolver = new ModelResolver()
            .Register("openai", "OpenAI", (string apiKey, string model, Uri? endpoint) =>
                new FakeAgentModel(apiKey, model));

        var resolved = resolver.Resolve(manifest, key => key switch
        {
            "OpenAI:ApiKey" => "sk-real-key",
            _ => null
        });

        ((FakeAgentModel)resolved["hosted"]).ApiKey.ShouldBe("sk-real-key");
    }

    [Test]
    public void Resolve_NoEndpointAndNoApiKey_StillThrows()
    {
        // The absence of both can only mean a hosted provider, which certainly needs a key.
        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  hosted:",
            "    provider: openai",
            "jobs:",
            "connections:",
        ]);

        var resolver = new ModelResolver()
            .Register("openai", "OpenAI", (string apiKey, string model, Uri? endpoint) =>
                new FakeAgentModel(apiKey, model));

        var ex = Should.Throw<InvalidOperationException>(() => resolver.Resolve(manifest, _ => null));
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
            "    model: claude-sonnet-5",
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
