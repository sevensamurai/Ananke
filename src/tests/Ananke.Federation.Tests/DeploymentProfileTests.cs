using Ananke.Abstractions.Providers;
using Ananke.Federation.Deployment;
using Ananke.Orchestration.Tools;
using Shouldly;

namespace Ananke.Federation.Tests;

[TestFixture]
public sealed class DeploymentProfileTests
{
    [Test]
    public void Bind_rebinds_tool_to_platform_native()
    {
        var source = new ToolKit("test");
        source.AddTool("search", "Web search", () => ToolResult.Ok("ok"));

        var profile = new DeploymentProfile
        {
            Name = "azure-ai",
            Tools = new Dictionary<string, ToolBinding>
            {
                ["search"] = new() { Execute = "platform", Platform = "bing_search" }
            }
        };

        var bound = profile.Bind(source);

        bound.Tools["search"].ExecutionMode.ShouldBe(ToolExecutionMode.PlatformNative);
        bound.Tools["search"].PlatformCapability.ShouldBe("bing_search");
    }

    [Test]
    public void Bind_rebinds_tool_to_callback()
    {
        var source = new ToolKit("test");
        source.AddTool("search", "Web search", () => ToolResult.Ok("ok"));

        var profile = new DeploymentProfile
        {
            Name = "staging",
            Tools = new Dictionary<string, ToolBinding>
            {
                ["search"] = new() { Execute = "callback", Endpoint = "https://api.example.com/search" }
            }
        };

        var bound = profile.Bind(source);

        bound.Tools["search"].ExecutionMode.ShouldBe(ToolExecutionMode.Callback);
        bound.Tools["search"].Endpoint!.Uri.AbsoluteUri.ShouldBe("https://api.example.com/search");
    }

    [Test]
    public void Bind_preserves_unlisted_tools()
    {
        var source = new ToolKit("test");
        source.AddTool("search", "Web search", () => ToolResult.Ok("ok"));
        source.AddTool("calc", "Calculator", () => ToolResult.Ok("42"));

        var profile = new DeploymentProfile
        {
            Name = "azure-ai",
            Tools = new Dictionary<string, ToolBinding>
            {
                ["search"] = new() { Execute = "platform", Platform = "bing_search" }
            }
        };

        var bound = profile.Bind(source);

        // search rebound
        bound.Tools["search"].ExecutionMode.ShouldBe(ToolExecutionMode.PlatformNative);
        // calc unchanged
        bound.Tools["calc"].ExecutionMode.ShouldBe(ToolExecutionMode.Local);
    }

    [Test]
    public void Bind_to_local_clears_endpoint_and_capability()
    {
        var source = new ToolKit("test");
        source.AddTool("code", "Code interpreter", b =>
        {
            b.OnExecute(_ => ToolResult.Ok("ok"));
            b.PlatformNative("code_interpreter");
        });

        var profile = new DeploymentProfile
        {
            Name = "local",
            Tools = new Dictionary<string, ToolBinding>
            {
                ["code"] = new() { Execute = "local" }
            }
        };

        var bound = profile.Bind(source);

        bound.Tools["code"].ExecutionMode.ShouldBe(ToolExecutionMode.Local);
        bound.Tools["code"].PlatformCapability.ShouldBeNull();
        bound.Tools["code"].Endpoint.ShouldBeNull();
    }

    [Test]
    public void ParseExecutionMode_rejects_unknown()
    {
        var profile = new DeploymentProfile
        {
            Name = "bad",
            Tools = new Dictionary<string, ToolBinding>
            {
                ["x"] = new() { Execute = "invalid_mode" }
            }
        };

        var source = new ToolKit("test");
        source.AddTool("x", "X", () => ToolResult.Ok("ok"));

        Should.Throw<InvalidOperationException>(() => profile.Bind(source));
    }
}
