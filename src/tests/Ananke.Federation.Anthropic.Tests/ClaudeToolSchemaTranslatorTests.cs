using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Federation.Anthropic.Tests;

[TestFixture]
public sealed class ClaudeToolSchemaTranslatorTests
{
    private ClaudeToolSchemaTranslator _translator = null!;

    [SetUp]
    public void SetUp() => _translator = new ClaudeToolSchemaTranslator();

    private static ToolDefinition MakeTool(
        string name,
        ToolExecutionMode mode,
        string? platformCapability = null,
        Uri? endpointUri = null)
    {
        return new ToolDefinition
        {
            Name = name,
            Description = $"Test tool {name}",
            Parameters = [new ToolParameter("input", "Test input", IsRequired: true)],
            ExecutionMode = mode,
            Endpoint = endpointUri is not null ? new ToolEndpoint { Uri = endpointUri } : null,
            PlatformCapability = platformCapability,
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
        };
    }

    [Test]
    public void Callback_tool_becomes_custom_tool()
    {
        var tools = _translator.Translate([
            MakeTool("search", ToolExecutionMode.Callback, endpointUri: new Uri("https://example.com/cb"))
        ]);

        tools.Count.ShouldBe(1);
        var tool = tools[0]!.AsObject();
        tool["name"]!.GetValue<string>().ShouldBe("search");
        tool["description"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
        tool["input_schema"].ShouldNotBeNull();
    }

    [Test]
    public void Mcp_tool_becomes_custom_tool()
    {
        var tools = _translator.Translate([
            MakeTool("mcp_tool", ToolExecutionMode.Mcp, endpointUri: new Uri("https://example.com/mcp"))
        ]);

        tools.Count.ShouldBe(1);
        tools[0]!["name"]!.GetValue<string>().ShouldBe("mcp_tool");
        tools[0]!["input_schema"].ShouldNotBeNull();
    }

    [Test]
    public void OpenApi_tool_becomes_custom_tool()
    {
        var tools = _translator.Translate([
            MakeTool("api", ToolExecutionMode.OpenApi, endpointUri: new Uri("https://example.com/spec.json"))
        ]);

        tools.Count.ShouldBe(1);
        tools[0]!["name"]!.GetValue<string>().ShouldBe("api");
    }

    [Test]
    public void PlatformNative_web_search_produces_correct_type()
    {
        var tools = _translator.Translate([
            MakeTool("search", ToolExecutionMode.PlatformNative, platformCapability: "web_search")
        ]);

        tools.Count.ShouldBe(1);
        tools[0]!["type"]!.GetValue<string>().ShouldBe("web_search");
    }

    [Test]
    public void PlatformNative_code_execution_produces_correct_type()
    {
        var tools = _translator.Translate([
            MakeTool("code", ToolExecutionMode.PlatformNative, platformCapability: "code_execution")
        ]);

        tools.Count.ShouldBe(1);
        tools[0]!["type"]!.GetValue<string>().ShouldBe("code_execution");
    }

    [Test]
    public void PlatformNative_computer_use_produces_correct_type()
    {
        var tools = _translator.Translate([
            MakeTool("computer", ToolExecutionMode.PlatformNative, platformCapability: "computer_use")
        ]);

        tools.Count.ShouldBe(1);
        tools[0]!["type"]!.GetValue<string>().ShouldBe("computer_use");
    }

    [Test]
    public void PlatformNative_text_editor_produces_correct_type()
    {
        var tools = _translator.Translate([
            MakeTool("editor", ToolExecutionMode.PlatformNative, platformCapability: "text_editor")
        ]);

        tools.Count.ShouldBe(1);
        tools[0]!["type"]!.GetValue<string>().ShouldBe("text_editor");
    }

    [Test]
    public void PlatformNative_bash_produces_correct_type()
    {
        var tools = _translator.Translate([
            MakeTool("shell", ToolExecutionMode.PlatformNative, platformCapability: "bash")
        ]);

        tools.Count.ShouldBe(1);
        tools[0]!["type"]!.GetValue<string>().ShouldBe("bash");
    }

    [Test]
    public void Unknown_platform_capability_passes_through()
    {
        var tools = _translator.Translate([
            MakeTool("future", ToolExecutionMode.PlatformNative, platformCapability: "some_future_tool")
        ]);

        tools.Count.ShouldBe(1);
        tools[0]!["type"]!.GetValue<string>().ShouldBe("some_future_tool");
    }

    [Test]
    public void Local_tool_throws()
    {
        Should.Throw<InvalidOperationException>(() =>
            _translator.Translate([MakeTool("local", ToolExecutionMode.Local)]));
    }

    [Test]
    public void Mixed_tools_all_present()
    {
        var tools = _translator.Translate([
            MakeTool("cb", ToolExecutionMode.Callback, endpointUri: new Uri("https://example.com/cb")),
            MakeTool("search", ToolExecutionMode.PlatformNative, platformCapability: "web_search"),
            MakeTool("code", ToolExecutionMode.PlatformNative, platformCapability: "code_execution"),
        ]);

        tools.Count.ShouldBe(3);
        tools[0]!["name"]!.GetValue<string>().ShouldBe("cb");
        tools[1]!["type"]!.GetValue<string>().ShouldBe("web_search");
        tools[2]!["type"]!.GetValue<string>().ShouldBe("code_execution");
    }
}
