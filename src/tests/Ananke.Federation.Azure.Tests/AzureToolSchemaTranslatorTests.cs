using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Federation.Azure.Tests;

[TestFixture]
public sealed class AzureToolSchemaTranslatorTests
{
    private AzureToolSchemaTranslator _translator = null!;

    [SetUp]
    public void SetUp() => _translator = new AzureToolSchemaTranslator();

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
    public void Callback_tool_becomes_function_json()
    {
        var tools = _translator.Translate([
            MakeTool("search", ToolExecutionMode.Callback, endpointUri: new Uri("https://example.com/cb"))
        ]);

        tools.Count.ShouldBe(1);
        var tool = tools[0]!.AsObject();
        tool["type"]!.GetValue<string>().ShouldBe("function");
        tool["function"]!["name"]!.GetValue<string>().ShouldBe("search");
        tool["function"]!["description"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
        tool["function"]!["parameters"].ShouldNotBeNull();
    }

    [Test]
    public void Mcp_tool_becomes_function_json()
    {
        var tools = _translator.Translate([
            MakeTool("fetch", ToolExecutionMode.Mcp, endpointUri: new Uri("https://example.com/mcp"))
        ]);

        tools.Count.ShouldBe(1);
        tools[0]!["type"]!.GetValue<string>().ShouldBe("function");
        tools[0]!["function"]!["name"]!.GetValue<string>().ShouldBe("fetch");
    }

    [Test]
    public void OpenApi_tool_becomes_native_openapi_json()
    {
        var tools = _translator.Translate([
            MakeTool("api", ToolExecutionMode.OpenApi, endpointUri: new Uri("https://example.com/openapi.json"))
        ]);

        tools.Count.ShouldBe(1);
        var tool = tools[0]!.AsObject();
        tool["type"]!.GetValue<string>().ShouldBe("openapi");
        tool["openapi"]!["name"]!.GetValue<string>().ShouldBe("api");
        tool["openapi"]!["spec"]!["url"]!.GetValue<string>().ShouldBe("https://example.com/openapi.json");
        tool["openapi"]!["auth"]!["type"]!.GetValue<string>().ShouldBe("anonymous");
    }

    [Test]
    public void OpenApi_tool_without_endpoint_throws()
    {
        Should.Throw<InvalidOperationException>(() =>
            _translator.Translate([MakeTool("api", ToolExecutionMode.OpenApi)]));
    }

    [Test]
    public void PlatformNative_code_interpreter_produces_correct_type()
    {
        var tools = _translator.Translate([
            MakeTool("code", ToolExecutionMode.PlatformNative, platformCapability: "code_interpreter")
        ]);

        tools.Count.ShouldBe(1);
        tools[0]!["type"]!.GetValue<string>().ShouldBe("code_interpreter");
    }

    [Test]
    public void PlatformNative_bing_search_produces_bing_grounding()
    {
        var tools = _translator.Translate([
            MakeTool("bing", ToolExecutionMode.PlatformNative, platformCapability: "bing_search")
        ]);

        tools.Count.ShouldBe(1);
        tools[0]!["type"]!.GetValue<string>().ShouldBe("bing_grounding");
    }

    [Test]
    public void PlatformNative_azure_ai_search_produces_correct_type()
    {
        var tools = _translator.Translate([
            MakeTool("search", ToolExecutionMode.PlatformNative, platformCapability: "azure_ai_search")
        ]);

        tools.Count.ShouldBe(1);
        tools[0]!["type"]!.GetValue<string>().ShouldBe("azure_ai_search");
    }

    [Test]
    public void PlatformNative_file_search_produces_correct_type()
    {
        var tools = _translator.Translate([
            MakeTool("files", ToolExecutionMode.PlatformNative, platformCapability: "file_search")
        ]);

        tools.Count.ShouldBe(1);
        tools[0]!["type"]!.GetValue<string>().ShouldBe("file_search");
    }

    [Test]
    public void PlatformNative_azure_function_produces_correct_type()
    {
        var tools = _translator.Translate([
            MakeTool("func", ToolExecutionMode.PlatformNative, platformCapability: "azure_function")
        ]);

        tools.Count.ShouldBe(1);
        tools[0]!["type"]!.GetValue<string>().ShouldBe("azure_function");
    }

    [Test]
    public void PlatformNative_sharepoint_produces_correct_type()
    {
        var tools = _translator.Translate([
            MakeTool("sp", ToolExecutionMode.PlatformNative, platformCapability: "sharepoint")
        ]);

        tools.Count.ShouldBe(1);
        tools[0]!["type"]!.GetValue<string>().ShouldBe("sharepoint_grounding");
    }

    [Test]
    public void Local_tool_throws()
    {
        Should.Throw<InvalidOperationException>(() =>
            _translator.Translate([MakeTool("local", ToolExecutionMode.Local)]));
    }

    [Test]
    public void Unknown_platform_capability_passes_through()
    {
        var tools = _translator.Translate([
            MakeTool("future", ToolExecutionMode.PlatformNative, platformCapability: "browser_automation")
        ]);

        tools.Count.ShouldBe(1);
        tools[0]!["type"]!.GetValue<string>().ShouldBe("browser_automation");
    }

    [Test]
    public void Mixed_tools_all_present()
    {
        var tools = _translator.Translate([
            MakeTool("cb", ToolExecutionMode.Callback, endpointUri: new Uri("https://example.com/cb")),
            MakeTool("api", ToolExecutionMode.OpenApi, endpointUri: new Uri("https://example.com/spec.json")),
            MakeTool("code", ToolExecutionMode.PlatformNative, platformCapability: "code_interpreter"),
            MakeTool("bing", ToolExecutionMode.PlatformNative, platformCapability: "bing_search"),
        ]);

        tools.Count.ShouldBe(4);
        tools[0]!["type"]!.GetValue<string>().ShouldBe("function");
        tools[1]!["type"]!.GetValue<string>().ShouldBe("openapi");
        tools[2]!["type"]!.GetValue<string>().ShouldBe("code_interpreter");
        tools[3]!["type"]!.GetValue<string>().ShouldBe("bing_grounding");
    }
}
