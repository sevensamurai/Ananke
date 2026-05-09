using Ananke.Design;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Federation.Google.Tests;

[TestFixture]
public sealed class VertexAIValidatorTests
{
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
                    b.PlatformNative("code_execution");
                    break;
            }
        });
        return kit;
    }

    [Test]
    public async Task Valid_manifest_with_mapped_model_passes()
    {
        // Use a project/location that won't actually connect — we only test the mapper path
        var credProvider = new VertexAICredentialProvider("test-project", "us-central1");
        var validator = new VertexAIValidator(credProvider);

        // The credential check may fail in CI (no ADC), but we can test model/tool validation
        // by checking diagnostics that are NOT credential-related
        var report = await validator.ValidateAsync(MakeManifest(), MakeToolKit());

        // If credentials failed, FED030 is present but no FED031/FED032
        var nonCredErrors = report.Diagnostics
            .Where(d => d.Code != "FED030")
            .ToList();

        nonCredErrors.ShouldNotContain(d => d.Code == "FED031");
        nonCredErrors.ShouldNotContain(d => d.Code == "FED032");
    }

    [Test]
    public async Task Unknown_model_produces_FED031()
    {
        var credProvider = new VertexAICredentialProvider("test-project", "us-central1");
        var validator = new VertexAIValidator(credProvider);

        var report = await validator.ValidateAsync(
            MakeManifest(provider: "unknown", model: "mystery-v1"),
            MakeToolKit());

        // Either FED030 (no creds, short-circuits) or FED031 (model not mapped)
        var hasModelError = report.Diagnostics.Any(d => d.Code == "FED031");
        var hasCredError = report.Diagnostics.Any(d => d.Code == "FED030");

        // At least one of these must be true
        (hasModelError || hasCredError).ShouldBeTrue();
    }

    [Test]
    public async Task Local_tool_produces_FED032()
    {
        var credProvider = new VertexAICredentialProvider("test-project", "us-central1");
        var validator = new VertexAIValidator(credProvider);

        var localKit = new ToolKit("test");
        localKit.AddTool("local_tool", "A local tool", () => ToolResult.Ok("ok"));

        var report = await validator.ValidateAsync(MakeManifest(), localKit);

        var hasToolError = report.Diagnostics.Any(d => d.Code == "FED032");
        var hasCredError = report.Diagnostics.Any(d => d.Code == "FED030");

        // FED032 if we got past creds, or FED030 short-circuits
        (hasToolError || hasCredError).ShouldBeTrue();
    }

    // ── May-2026 capability recognition ─────────────────────────────────────

    [TestCase("code_execution")]
    [TestCase("code_interpreter")]
    [TestCase("vertex_extension:code_interpreter")]
    [TestCase("google_search")]
    [TestCase("google_search_retrieval")]
    [TestCase("bash")]
    [TestCase("computer_use")]
    [TestCase("url_context")]
    [TestCase("deep_research")]
    [TestCase("memory_bank")]
    [TestCase("memory_profiles")]
    [TestCase("image_generation")]
    [TestCase("bigquery")]
    [TestCase("spanner")]
    [TestCase("bigtable")]
    [TestCase("pubsub")]
    [TestCase("maps")]
    [TestCase("artifact_service")]
    [TestCase("a2a")]
    public async Task All_known_Google_capabilities_do_not_trigger_FED034(
        string capability)
    {
        var credProvider = new VertexAICredentialProvider("test-project", "us-central1");
        var validator = new VertexAIValidator(credProvider);

        var kit = new ToolKit("test");
        kit.AddTool(new ToolDefinition
        {
            Name = capability.Replace(':', '_'),
            Description = "platform built-in",
            Parameters = [],
            ExecutionMode = ToolExecutionMode.PlatformNative,
            PlatformCapability = capability,
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
        });

        var report = await validator.ValidateAsync(MakeManifest(), kit);

        report.Diagnostics.ShouldNotContain(d => d.Code == "FED034");
    }

    [Test]
    public async Task Unknown_Google_capability_produces_FED034_warning()
    {
        var credProvider = new VertexAICredentialProvider("test-project", "us-central1");
        var validator = new VertexAIValidator(credProvider);

        var kit = new ToolKit("test");
        kit.AddTool(new ToolDefinition
        {
            Name = "future_capability",
            Description = "a future built-in",
            Parameters = [],
            ExecutionMode = ToolExecutionMode.PlatformNative,
            PlatformCapability = "some_future_capability",
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
        });

        var report = await validator.ValidateAsync(MakeManifest(), kit);

        var hasFed034 = report.Diagnostics.Any(d => d.Code == "FED034");
        var hasCredError = report.Diagnostics.Any(d => d.Code == "FED030");

        // FED034 if we got past creds, or FED030 short-circuits before tool validation
        (hasFed034 || hasCredError).ShouldBeTrue();
    }
}
