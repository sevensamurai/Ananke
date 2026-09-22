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

    [TestCase("foundry", "azure")]
    [TestCase("azure-ai", "azure")]
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
    [TestCase("azure-ai")]
    [TestCase("gemini-enterprise")]
    public void Alias_does_not_produce_FED023_unknown_platform_error(string alias)
    {
        var manifest = MakeManifest();
        var report = _validator.Validate(manifest, MakeToolKit(ToolExecutionMode.Callback), alias);

        report.Diagnostics.ShouldNotContain(d => d.Code == "FED023");
    }

    [TestCase("azure")]
    [TestCase("vertex-ai")]
    [TestCase("claude")]
    public void Canonical_platform_identifiers_still_resolve_without_FED060(string platform)
    {
        var manifest = MakeManifest();
        var report = _validator.Validate(manifest, MakeToolKit(ToolExecutionMode.Callback), platform);

        report.Diagnostics.ShouldNotContain(d => d.Code == "FED060");
        report.Diagnostics.ShouldNotContain(d => d.Code == "FED023");
    }

    // ── local as a deployment target (L2) ─────────────────────────────────────

    /// <summary>
    /// Before <c>local</c> became a known platform, <c>validate --platform local</c> returned
    /// <c>FED023 "not recognized — Supported platforms: azure, vertex-ai, claude"</c> for a substrate
    /// the CLI documentation calls <i>Stable</i>.
    /// </summary>
    [Test]
    public void Local_does_not_produce_FED023_unknown_platform()
    {
        var report = _validator.Validate(MakeManifest(), new ToolKit("empty"), "local");

        report.Diagnostics.ShouldNotContain(d => d.Code == "FED023");
    }

    // ── FED016: unresolved cross-manifest model reference (L3) ────────────────

    /// <summary>
    /// The sharpest case L3 uncovered. <c>ModelDefinition.Provider</c> and <c>Model</c> carry
    /// defaults, so an alias declaring only <c>ref: devstral2</c> parsed into a valid-looking
    /// <c>openai</c> / <c>gpt-5.4-mini</c> — and <c>studio-router.ananke.yml</c>, which points at a
    /// local Ollama model, validated <b>DEPLOYABLE</b> as a paid frontier call. It must refuse.
    /// </summary>
    [Test]
    public void FED016_unresolved_model_ref_produces_error()
    {
        var manifest = MakeManifest(
            models: new() { ["router"] = new() { Ref = "devstral2" } },
            jobs: new() { ["classify"] = new() { Type = "agent", ModelAlias = "router" } });

        var report = _validator.Validate(manifest, new ToolKit("empty"), "local");

        var diag = report.Diagnostics.Single(d => d.Code == "FED016");
        diag.Severity.ShouldBe(DeployDiagnosticSeverity.Error);
        diag.Message.ShouldContain("devstral2");
        report.IsDeployable.ShouldBeFalse();
    }

    /// <summary>
    /// A ref is not the same complaint as a missing alias. Before L3 the flow-mapped form was
    /// dropped and reported as <c>FED011</c> "not defined in the manifest", which sent the reader
    /// looking for an alias that was plainly there.
    /// </summary>
    [Test]
    public void FED016_not_FED011_when_the_alias_exists_but_is_a_reference()
    {
        var manifest = MakeManifest(
            models: new() { ["router"] = new() { Ref = "devstral2" } },
            jobs: new() { ["classify"] = new() { Type = "agent", ModelAlias = "router" } });

        var report = _validator.Validate(manifest, new ToolKit("empty"), "local");

        report.Diagnostics.ShouldNotContain(d => d.Code == "FED011");
    }

    [Test]
    public void FED016_not_raised_for_an_inline_model()
    {
        var report = _validator.Validate(MakeManifest(), new ToolKit("empty"), "local");

        report.Diagnostics.ShouldNotContain(d => d.Code == "FED016");
    }

    // ── FED016: catalogue resolution (L12) ────────────────────────────────────

    private static WorkflowManifest RefManifest() => MakeManifest(
        models: new() { ["router"] = new() { Ref = "devstral2" } },
        jobs: new() { ["classify"] = new() { Type = "agent", ModelAlias = "router" } });

    /// <summary>
    /// The case the repository's own manifests are in: every workflow references
    /// <c>roles.ananke.yml</c>'s catalogue, and with one supplied the reference resolves.
    /// </summary>
    [Test]
    public void A_ref_resolves_against_a_supplied_catalogue()
    {
        var catalogue = new Dictionary<string, ModelDefinition>
        {
            ["devstral2"] = new() { Provider = "openai", Model = "devstral2:latest" }
        };

        var report = _validator.Validate(RefManifest(), new ToolKit("empty"), "local", catalogue);

        report.Diagnostics.ShouldNotContain(d => d.Code == "FED016");
    }

    /// <summary>
    /// "No catalogue supplied" and "catalogue lacks the alias" are different problems with
    /// different fixes, so they must not share one message.
    /// </summary>
    [Test]
    public void FED016_distinguishes_a_missing_catalogue_from_a_missing_entry()
    {
        var withoutCatalogue = _validator.Validate(RefManifest(), new ToolKit("empty"), "local");
        var withWrongCatalogue = _validator.Validate(
            RefManifest(), new ToolKit("empty"), "local",
            new Dictionary<string, ModelDefinition> { ["something-else"] = new() });

        withoutCatalogue.Diagnostics.Single(d => d.Code == "FED016")
            .Message.ShouldContain("no catalogue was supplied");
        withWrongCatalogue.Diagnostics.Single(d => d.Code == "FED016")
            .Message.ShouldContain("not present in the supplied catalogue");
    }

    /// <summary>
    /// Resolution is for this validation only — the manifest keeps meaning exactly what its own
    /// file says. A validator that rewrote it would make a parsed manifest stop
    /// reflecting its source.
    /// </summary>
    [Test]
    public void Resolving_a_ref_does_not_rewrite_the_manifest()
    {
        var manifest = RefManifest();
        var catalogue = new Dictionary<string, ModelDefinition>
        {
            ["devstral2"] = new() { Provider = "openai", Model = "devstral2:latest" }
        };

        _validator.Validate(manifest, new ToolKit("empty"), "local", catalogue);

        manifest.Models["router"].Ref.ShouldBe("devstral2");
        manifest.Models["router"].Model.ShouldNotBe("devstral2:latest");
    }

    /// <summary>
    /// A catalogue must not silently rescue a genuinely undefined alias — that is still FED011.
    /// </summary>
    [Test]
    public void A_catalogue_does_not_mask_an_undefined_alias()
    {
        var manifest = MakeManifest(
            models: new(),
            jobs: new() { ["classify"] = new() { Type = "agent", ModelAlias = "nope" } });

        var report = _validator.Validate(
            manifest, new ToolKit("empty"), "local",
            new Dictionary<string, ModelDefinition> { ["nope"] = new() });

        report.Diagnostics.ShouldContain(d => d.Code == "FED011");
    }
}
