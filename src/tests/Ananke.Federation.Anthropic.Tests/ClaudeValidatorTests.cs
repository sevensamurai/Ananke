using Ananke.Abstractions.Providers;
using Ananke.Design;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Federation.Anthropic.Tests;

[TestFixture]
public sealed class ClaudeValidatorTests
{
    private static readonly string TestApiKey = "sk-ant-test-key";

    private static WorkflowManifest MakeManifest() => WorkflowManifest.Parse([
        "name: test-workflow",
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
    public async Task Valid_manifest_with_anthropic_model_passes()
    {
        var credProvider = new ClaudeCredentialProvider(TestApiKey);
        var validator = new ClaudeValidator(credProvider);

        var kit = new ToolKit("test");
        kit.AddTool("search", "Search the web", b =>
        {
            b.OnExecute(_ => ToolResult.Ok("ok"));
            b.PlatformNative("web_search");
        });

        var report = await validator.ValidateAsync(MakeManifest(), kit);

        report.IsDeployable.ShouldBeTrue();
    }

    [Test]
    public async Task Missing_credentials_produces_FED050()
    {
        var credProvider = new ClaudeCredentialProvider(null);
        // Remove env var for test
        var originalKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);

        try
        {
            var validator = new ClaudeValidator(credProvider);
            var report = await validator.ValidateAsync(MakeManifest(), new ToolKit("test"));

            report.Diagnostics.ShouldContain(d => d.Code == "FED050");
            report.IsDeployable.ShouldBeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", originalKey);
        }
    }

    [Test]
    public async Task Unknown_model_produces_FED051()
    {
        var credProvider = new ClaudeCredentialProvider(TestApiKey);
        var validator = new ClaudeValidator(credProvider);

        var manifest = WorkflowManifest.Parse([
            "name: test",
            "models:",
            "  custom:",
            "    provider: unknown-provider",
            "    model: mystery-model",
            "jobs:",
            "  agent1:",
            "    type: agent",
            "    model: custom",
            "connections:",
            "  - agent1",
        ]);

        var report = await validator.ValidateAsync(manifest, new ToolKit("test"));

        report.Diagnostics.ShouldContain(d => d.Code == "FED051");
        report.IsDeployable.ShouldBeFalse();
    }

    [Test]
    public async Task Local_tool_produces_FED052()
    {
        var credProvider = new ClaudeCredentialProvider(TestApiKey);
        var validator = new ClaudeValidator(credProvider);

        var kit = new ToolKit("test");
        kit.AddTool("local_tool", "A local tool", () => ToolResult.Ok("ok"));

        var report = await validator.ValidateAsync(MakeManifest(), kit);

        report.Diagnostics.ShouldContain(d => d.Code == "FED052");
        report.IsDeployable.ShouldBeFalse();
    }

    [Test]
    public async Task OpenApi_tool_produces_FED053_warning()
    {
        var credProvider = new ClaudeCredentialProvider(TestApiKey);
        var validator = new ClaudeValidator(credProvider);

        var kit = new ToolKit("test");
        kit.AddTool(new ToolDefinition
        {
            Name = "api_tool",
            Description = "An OpenAPI tool",
            Parameters = [],
            ExecutionMode = ToolExecutionMode.OpenApi,
            Endpoint = new ToolEndpoint { Uri = new Uri("https://example.com/spec.json") },
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
        });

        var report = await validator.ValidateAsync(MakeManifest(), kit);

        report.Diagnostics.ShouldContain(d => d.Code == "FED053" && d.Severity == DeployDiagnosticSeverity.Warning);
        report.IsDeployable.ShouldBeTrue(); // Warning, not error
    }

    [Test]
    public async Task Unknown_platform_capability_produces_FED054_warning()
    {
        var credProvider = new ClaudeCredentialProvider(TestApiKey);
        var validator = new ClaudeValidator(credProvider);

        var kit = new ToolKit("test");
        kit.AddTool(new ToolDefinition
        {
            Name = "future_tool",
            Description = "A future built-in",
            Parameters = [],
            ExecutionMode = ToolExecutionMode.PlatformNative,
            PlatformCapability = "some_future_capability",
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
        });

        var report = await validator.ValidateAsync(MakeManifest(), kit);

        report.Diagnostics.ShouldContain(d => d.Code == "FED054" && d.Severity == DeployDiagnosticSeverity.Warning);
        report.IsDeployable.ShouldBeTrue(); // Warning, not error — passthrough
    }

    // ── May-2026 capability recognition ─────────────────────────────────────

    [TestCase("web_search")]
    [TestCase("web_fetch")]
    [TestCase("code_execution")]
    [TestCase("computer_use")]
    [TestCase("text_editor")]
    [TestCase("bash")]
    [TestCase("memory")]
    public async Task All_known_Claude_capabilities_do_not_trigger_FED054(string capability)
    {
        var credProvider = new ClaudeCredentialProvider(TestApiKey);
        var validator = new ClaudeValidator(credProvider);

        var kit = new ToolKit("test");
        kit.AddTool(new ToolDefinition
        {
            Name = capability,
            Description = "platform built-in",
            Parameters = [],
            ExecutionMode = ToolExecutionMode.PlatformNative,
            PlatformCapability = capability,
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
        });

        var report = await validator.ValidateAsync(MakeManifest(), kit);

        report.Diagnostics.ShouldNotContain(d => d.Code == "FED054");
    }
}
