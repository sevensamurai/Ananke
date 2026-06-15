using Ananke.Abstractions.Providers;
using Ananke.Design;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Federation.Azure.Tests;

[TestFixture]
public sealed class AzureAgentValidatorTests
{
    // Use a fake but syntactically valid endpoint — no real Azure calls are made
    private static readonly Uri TestEndpoint = new("https://test.services.ai.azure.com/api/projects/test");

    private static WorkflowManifest MakeManifest(
        string provider = "openai",
        string model = "gpt-4.1-mini") => new()
    {
        Name = "test",
        Models = new() { ["default"] = new() { Provider = provider, Model = model } },
        Jobs = new() { ["agent1"] = new() { Type = "agent", ModelAlias = "default" } },
        Connections = ["agent1"]
    };

    private static ToolKit MakeToolKit(ToolExecutionMode mode = ToolExecutionMode.Callback)
    {
        var kit = new ToolKit("test");
        kit.AddTool("tool1", "A test tool", b =>
        {
            b.OnExecute(_ => ToolResult.Ok("ok"));
            switch (mode)
            {
                case ToolExecutionMode.Callback:
                    b.Callback(new Uri("https://example.com/cb"));
                    break;
                case ToolExecutionMode.PlatformNative:
                    b.PlatformNative("code_interpreter");
                    break;
            }
        });
        return kit;
    }

    [Test]
    public async Task Valid_manifest_with_openai_model_passes()
    {
        var credProvider = new AzureAgentCredentialProvider(TestEndpoint);
        var validator = new AzureAgentValidator(credProvider);

        var report = await validator.ValidateAsync(MakeManifest(), MakeToolKit());

        report.IsDeployable.ShouldBeTrue();
        report.Diagnostics.ShouldNotContain(d => d.Code == "FED041");
        report.Diagnostics.ShouldNotContain(d => d.Code == "FED042");
    }

    [Test]
    public async Task Unknown_model_produces_FED041()
    {
        var credProvider = new AzureAgentCredentialProvider(TestEndpoint);
        var validator = new AzureAgentValidator(credProvider);

        var report = await validator.ValidateAsync(
            MakeManifest(provider: "unknown", model: "mystery-v1"),
            MakeToolKit());

        report.Diagnostics.ShouldContain(d => d.Code == "FED041");
        report.IsDeployable.ShouldBeFalse();
    }

    [Test]
    public async Task Local_tool_produces_FED042()
    {
        var credProvider = new AzureAgentCredentialProvider(TestEndpoint);
        var validator = new AzureAgentValidator(credProvider);

        var localKit = new ToolKit("test");
        localKit.AddTool("local_tool", "A local tool", () => ToolResult.Ok("ok"));

        var report = await validator.ValidateAsync(MakeManifest(), localKit);

        report.Diagnostics.ShouldContain(d => d.Code == "FED042");
        report.IsDeployable.ShouldBeFalse();
    }

    [Test]
    public async Task Mcp_tool_produces_FED043_warning()
    {
        var credProvider = new AzureAgentCredentialProvider(TestEndpoint);
        var validator = new AzureAgentValidator(credProvider);

        var mcpKit = new ToolKit("test");
        mcpKit.AddTool("mcp_tool", "An MCP tool", b =>
        {
            b.OnExecute(_ => ToolResult.Ok("ok"));
            b.Mcp(new Uri("https://example.com/mcp"));
        });

        var report = await validator.ValidateAsync(MakeManifest(), mcpKit);

        report.Diagnostics.ShouldContain(d => d.Code == "FED043" && d.Severity == DeployDiagnosticSeverity.Warning);
        report.IsDeployable.ShouldBeTrue(); // Warning, not error
    }

    [Test]
    public async Task OpenApi_tool_without_endpoint_produces_FED044()
    {
        var credProvider = new AzureAgentCredentialProvider(TestEndpoint);
        var validator = new AzureAgentValidator(credProvider);

        // Construct a ToolDefinition directly with OpenApi mode but no endpoint
        var openApiKit = new ToolKit("test");
        openApiKit.AddTool(new ToolDefinition
        {
            Name = "api_tool",
            Description = "An OpenAPI tool with missing spec URI",
            Parameters = [],
            ExecutionMode = ToolExecutionMode.OpenApi,
            Endpoint = null,
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
        });

        var report = await validator.ValidateAsync(MakeManifest(), openApiKit);

        report.Diagnostics.ShouldContain(d => d.Code == "FED044");
        report.IsDeployable.ShouldBeFalse();
    }

    [Test]
    public async Task Unknown_platform_capability_produces_FED045_warning()
    {
        var credProvider = new AzureAgentCredentialProvider(TestEndpoint);
        var validator = new AzureAgentValidator(credProvider);

        var kit = new ToolKit("test");
        kit.AddTool(new ToolDefinition
        {
            Name = "future_tool",
            Description = "A tool using a future capability",
            Parameters = [],
            ExecutionMode = ToolExecutionMode.PlatformNative,
            PlatformCapability = "some_future_capability",
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
        });

        var report = await validator.ValidateAsync(MakeManifest(), kit);

        report.Diagnostics.ShouldContain(d => d.Code == "FED045" && d.Severity == DeployDiagnosticSeverity.Warning);
        report.IsDeployable.ShouldBeTrue(); // Warning, not error — passthrough
    }

    // ── May-2026 capability recognition ─────────────────────────────────────

    [TestCase("code_interpreter")]
    [TestCase("file_search")]
    [TestCase("bing_search")]
    [TestCase("bing_grounding")]
    [TestCase("bing_custom_search")]
    [TestCase("azure_ai_search")]
    [TestCase("azure_function")]
    [TestCase("sharepoint")]
    [TestCase("sharepoint_grounding")]
    [TestCase("microsoft_fabric")]
    [TestCase("browser_automation")]
    [TestCase("memory_search")]
    [TestCase("a2a")]
    [TestCase("capture_structured_outputs")]
    [TestCase("deep_research")]
    [TestCase("image_generation")]
    public async Task All_known_Azure_capabilities_do_not_trigger_FED045(string capability)
    {
        var credProvider = new AzureAgentCredentialProvider(TestEndpoint);
        var validator = new AzureAgentValidator(credProvider);

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

        report.Diagnostics.ShouldNotContain(d => d.Code == "FED045");
    }
}
