using Ananke.Abstractions.Providers;
using Ananke.Federation.Google;
using Ananke.Orchestration.Tools;
using Google.GenAI.Types;
using Shouldly;

namespace Ananke.Federation.Google.Tests;

[TestFixture]
public sealed class VertexAIToolSchemaTranslatorTests
{
    private VertexAIToolSchemaTranslator _translator = null!;

    [SetUp]
    public void SetUp() => _translator = new VertexAIToolSchemaTranslator();

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
    public void Callback_tool_becomes_function_declaration()
    {
        var tools = _translator.Translate([
            MakeTool("search", ToolExecutionMode.Callback, endpointUri: new Uri("https://example.com/cb"))
        ]);

        tools.Count.ShouldBe(1);
        tools[0].FunctionDeclarations.ShouldNotBeNull();
        tools[0].FunctionDeclarations!.Count.ShouldBe(1);
        tools[0].FunctionDeclarations![0].Name.ShouldBe("search");
    }

    [Test]
    public void Mcp_tool_becomes_function_declaration()
    {
        var tools = _translator.Translate([
            MakeTool("fetch", ToolExecutionMode.Mcp, endpointUri: new Uri("https://example.com/mcp"))
        ]);

        tools.Count.ShouldBe(1);
        tools[0].FunctionDeclarations.ShouldNotBeNull();
        tools[0].FunctionDeclarations![0].Name.ShouldBe("fetch");
    }

    [Test]
    public void OpenApi_tool_becomes_function_declaration()
    {
        var tools = _translator.Translate([
            MakeTool("api", ToolExecutionMode.OpenApi, endpointUri: new Uri("https://example.com/openapi.json"))
        ]);

        tools.Count.ShouldBe(1);
        tools[0].FunctionDeclarations.ShouldNotBeNull();
        tools[0].FunctionDeclarations![0].Name.ShouldBe("api");
    }

    [Test]
    public void PlatformNative_code_execution_becomes_code_execution_tool()
    {
        var tools = _translator.Translate([
            MakeTool("code", ToolExecutionMode.PlatformNative, platformCapability: "code_execution")
        ]);

        tools.Count.ShouldBe(1);
        tools[0].CodeExecution.ShouldNotBeNull();
    }

    [Test]
    public void PlatformNative_google_search_becomes_google_search_tool()
    {
        var tools = _translator.Translate([
            MakeTool("search", ToolExecutionMode.PlatformNative, platformCapability: "google_search")
        ]);

        tools.Count.ShouldBe(1);
        tools[0].GoogleSearch.ShouldNotBeNull();
    }

    [Test]
    public void Local_tool_throws()
    {
        Should.Throw<InvalidOperationException>(() =>
            _translator.Translate([MakeTool("local", ToolExecutionMode.Local)]));
    }

    [Test]
    public void Mixed_tools_produce_correct_structure()
    {
        var tools = _translator.Translate([
            MakeTool("cb", ToolExecutionMode.Callback, endpointUri: new Uri("https://example.com/cb")),
            MakeTool("mcp", ToolExecutionMode.Mcp, endpointUri: new Uri("https://example.com/mcp")),
            MakeTool("code", ToolExecutionMode.PlatformNative, platformCapability: "code_execution"),
            MakeTool("search", ToolExecutionMode.PlatformNative, platformCapability: "google_search"),
        ]);

        // Function declarations grouped into one Tool, plus two platform-native tools
        tools.Count.ShouldBe(3);
        tools[0].FunctionDeclarations.ShouldNotBeNull();
        tools[0].FunctionDeclarations!.Count.ShouldBe(2);
    }

    [Test]
    public void Function_declaration_includes_parameter_schema()
    {
        var tools = _translator.Translate([
            MakeTool("search", ToolExecutionMode.Callback, endpointUri: new Uri("https://example.com/cb"))
        ]);

        var decl = tools[0].FunctionDeclarations![0];
        decl.Parameters.ShouldNotBeNull();
        decl.Parameters!.Properties!.ShouldContainKey("input");
    }

    [Test]
    public void Unknown_platform_native_capability_falls_through()
    {
        // Unknown capabilities are passed through (best-effort mapping).
        // The validator warns; the translator does not throw.
        var tools = _translator.Translate([
            MakeTool("future", ToolExecutionMode.PlatformNative, platformCapability: "some_future_tool")
        ]);

        tools.Count.ShouldBe(1);
    }
}
