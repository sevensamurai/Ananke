using Ananke.Abstractions.Agents;
using Ananke.Design;
using ToolExecutionMode = Ananke.Abstractions.Providers.ToolExecutionMode;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Federation.Tests;

[TestFixture]
public sealed class DeployabilityValidatorTests
{
    private DeployabilityValidator _validator = null!;

    [SetUp]
    public void SetUp() => _validator = new DeployabilityValidator();

    private static WorkflowManifest MakeManifest(
        string name = "test",
        Dictionary<string, ModelDefinition>? models = null,
        Dictionary<string, JobDefinition>? jobs = null,
        List<string>? connections = null) => new()
        {
            Name = name,
            Models = models ?? new() { ["default"] = new() { Provider = "openai", Model = Models.OpenAI.Gpt54Mini } },
            Jobs = jobs ?? new() { ["agent1"] = new() { Type = "agent", ModelAlias = "default" } },
            Connections = connections ?? ["agent1"]
        };

    private static ToolKit MakeToolKit(params ToolExecutionMode[] modes)
    {
        var kit = new ToolKit("test");
        for (var i = 0; i < modes.Length; i++)
        {
            kit.AddTool($"tool{i}", $"Tool {i}", b =>
            {
                b.OnExecute(_ => ToolResult.Ok("ok"));
                switch (modes[i])
                {
                    case ToolExecutionMode.Callback:
                        b.Callback(new Uri("https://example.com/callback"));
                        break;
                    case ToolExecutionMode.Mcp:
                        b.Mcp(new Uri("https://example.com/mcp"));
                        break;
                    case ToolExecutionMode.OpenApi:
                        b.OpenApi(new Uri("https://example.com/openapi.json"));
                        break;
                    case ToolExecutionMode.PlatformNative:
                        b.PlatformNative("code_execution");
                        break;
                        // Local is the default
                }
            });
        }
        return kit;
    }

    [Test]
    public void FED001_local_tool_produces_error()
    {
        var report = _validator.Validate(MakeManifest(), MakeToolKit(ToolExecutionMode.Local), "vertex-ai");

        report.IsDeployable.ShouldBeFalse();
        report.Errors.ShouldContain(d => d.Code == "FED001");
    }

    [Test]
    public void FED002_remote_tool_without_endpoint_produces_error()
    {
        var kit = new ToolKit("test");
        // Manually create a tool with Callback mode but no endpoint
        kit.AddTool(new ToolDefinition
        {
            Name = "broken",
            Description = "No endpoint",
            Parameters = [],
            ExecutionMode = ToolExecutionMode.Callback,
            Endpoint = null,
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
        });

        var report = _validator.Validate(MakeManifest(), kit, "vertex-ai");
        report.Errors.ShouldContain(d => d.Code == "FED002");
    }

    [Test]
    public void FED003_unknown_platform_native_produces_warning()
    {
        var kit = new ToolKit("test");
        kit.AddTool("native", "Native tool", b =>
        {
            b.PlatformNative("unknown_capability");
            b.OnExecute(_ => ToolResult.Ok("ok"));
        });

        var report = _validator.Validate(MakeManifest(), kit, "vertex-ai");
        report.Warnings.ShouldContain(d => d.Code == "FED003");
    }

    [Test]
    public void FED010_agent_job_without_model_alias_produces_error()
    {
        var manifest = MakeManifest(jobs: new()
        {
            ["agent1"] = new() { Type = "agent", ModelAlias = null }
        });

        var report = _validator.Validate(manifest, MakeToolKit(ToolExecutionMode.Callback), "vertex-ai");
        report.Errors.ShouldContain(d => d.Code == "FED010");
    }

    [Test]
    public void FED011_undefined_model_alias_produces_error()
    {
        var manifest = MakeManifest(jobs: new()
        {
            ["agent1"] = new() { Type = "agent", ModelAlias = "nonexistent" }
        });

        var report = _validator.Validate(manifest, MakeToolKit(ToolExecutionMode.Callback), "vertex-ai");
        report.Errors.ShouldContain(d => d.Code == "FED011");
    }

    [Test]
    public void FED013_unmappable_model_produces_error_when_mapper_registered()
    {
        var mapper = new TestModelMapper("vertex-ai", returnNull: true);
        var validator = new DeployabilityValidator([mapper]);

        var report = validator.Validate(MakeManifest(), MakeToolKit(ToolExecutionMode.Callback), "vertex-ai");
        report.Errors.ShouldContain(d => d.Code == "FED013");
    }

    [Test]
    public void FED014_custom_endpoint_produces_warning()
    {
        var manifest = MakeManifest(models: new()
        {
            ["default"] = new() { Provider = "openai", Model = Models.OpenAI.Gpt54Mini, Endpoint = "http://localhost:11434/v1" }
        });

        var report = _validator.Validate(manifest, MakeToolKit(ToolExecutionMode.Callback), "vertex-ai");
        report.Warnings.ShouldContain(d => d.Code == "FED014");
    }

    [Test]
    public void FED015_no_mapper_produces_info()
    {
        var report = _validator.Validate(MakeManifest(), MakeToolKit(ToolExecutionMode.Callback), "vertex-ai");
        report.Diagnostics.ShouldContain(d => d.Code == "FED015");
    }

    [Test]
    public void FED020_no_jobs_produces_error()
    {
        var manifest = MakeManifest(jobs: []);
        var report = _validator.Validate(manifest, MakeToolKit(ToolExecutionMode.Callback), "vertex-ai");
        report.Errors.ShouldContain(d => d.Code == "FED020");
    }

    [Test]
    public void FED021_multiple_jobs_no_connections_produces_warning()
    {
        var manifest = MakeManifest(
            jobs: new()
            {
                ["a"] = new() { Type = "agent", ModelAlias = "default" },
                ["b"] = new() { Type = "agent", ModelAlias = "default" }
            },
            connections: []);

        var report = _validator.Validate(manifest, MakeToolKit(ToolExecutionMode.Callback), "vertex-ai");
        report.Warnings.ShouldContain(d => d.Code == "FED021");
    }

    [Test]
    public void FED022_empty_toolkit_produces_warning()
    {
        var report = _validator.Validate(MakeManifest(), new ToolKit("empty"), "vertex-ai");
        report.Warnings.ShouldContain(d => d.Code == "FED022");
    }

    [Test]
    public void FED023_unknown_platform_produces_error()
    {
        var report = _validator.Validate(MakeManifest(), MakeToolKit(ToolExecutionMode.Callback), "unknown-platform");
        report.Errors.ShouldContain(d => d.Code == "FED023");
    }

    [Test]
    public void Deployable_manifest_with_callback_tools_passes()
    {
        var mapper = new TestModelMapper("vertex-ai", returnNull: false);
        var validator = new DeployabilityValidator([mapper]);

        var report = validator.Validate(MakeManifest(), MakeToolKit(ToolExecutionMode.Callback), "vertex-ai");
        report.IsDeployable.ShouldBeTrue();
        report.Errors.ShouldBeEmpty();
    }

    [Test]
    public void Code_jobs_skip_model_validation()
    {
        var manifest = MakeManifest(
            models: [],
            jobs: new() { ["code1"] = new() { Type = "code" } });

        var report = _validator.Validate(manifest, MakeToolKit(ToolExecutionMode.Callback), "vertex-ai");
        report.Diagnostics.ShouldNotContain(d => d.Code == "FED010");
    }

    private sealed class TestModelMapper(string platform, bool returnNull) : IModelMapper
    {
        public string Platform => platform;
        public string? Map(ModelDefinition model) => returnNull ? null : "mapped-model";
    }

    // ── Platform identifier alias resolution (FED060) ─────────────────────

    [TestCase("foundry", "azure-ai")]
    [TestCase("gemini-enterprise", "vertex-ai")]
    public void Alias_resolves_to_canonical_and_emits_FED060(string alias, string canonical)
    {
        var manifest = MakeManifest();
        var report = _validator.Validate(manifest, MakeToolKit(ToolExecutionMode.Callback), alias);

        report.Diagnostics.ShouldContain(d =>
            d.Code == "FED060" &&
            d.Message.Contains(alias) &&
            d.Message.Contains(canonical));
    }

    [TestCase("foundry")]
    [TestCase("gemini-enterprise")]
    public void Alias_does_not_produce_FED023_unknown_platform_error(string alias)
    {
        var manifest = MakeManifest();
        var report = _validator.Validate(manifest, MakeToolKit(ToolExecutionMode.Callback), alias);

        report.Diagnostics.ShouldNotContain(d => d.Code == "FED023");
    }

    [TestCase("azure-ai")]
    [TestCase("vertex-ai")]
    [TestCase("claude")]
    public void Canonical_platform_identifiers_still_resolve_without_FED060(string platform)
    {
        var manifest = MakeManifest();
        var report = _validator.Validate(manifest, MakeToolKit(ToolExecutionMode.Callback), platform);

        report.Diagnostics.ShouldNotContain(d => d.Code == "FED060");
        report.Diagnostics.ShouldNotContain(d => d.Code == "FED023");
    }
}
