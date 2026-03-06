using Ananke.Orchestration.Tools;
using Shouldly;
using System.Text.Json;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class ToolKitTests
{
    [Test]
    public void AddTool_NoParams_RegistersAndExecutes()
    {
        var kit = new ToolKit("test")
            .AddTool("ping", "Returns pong", () => "pong");

        kit.Tools.ShouldContainKey("ping");
        kit.Tools["ping"].Description.ShouldBe("Returns pong");
        kit.Tools["ping"].Parameters.ShouldBeEmpty();
    }

    [Test]
    public async Task AddTool_SingleParam_ExecutesWithArg()
    {
        var kit = new ToolKit("test")
            .AddTool("greet", "Greets a user", (string name) => $"Hello, {name}!",
                "name", "The user's name");

        var tool = kit.Tools["greet"];
        tool.Parameters.Count.ShouldBe(1);
        tool.Parameters[0].Name.ShouldBe("name");

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["name"] = "Alice" });
        result.Value.ShouldBe("Hello, Alice!");
    }

    [Test]
    public async Task AddTool_TwoParams_ExecutesWithBothArgs()
    {
        var kit = new ToolKit("test")
            .AddTool("add", "Adds context", (string a, string b) => $"{a}+{b}",
                ("a", "First value"), ("b", "Second value"));

        var tool = kit.Tools["add"];
        tool.Parameters.Count.ShouldBe(2);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["a"] = "foo",
            ["b"] = "bar"
        });
        result.Value.ShouldBe("foo+bar");
    }

    [Test]
    public void AddTool_MissingArg_Throws()
    {
        var kit = new ToolKit("test")
            .AddTool("greet", "Greets", (string name) => $"Hi {name}",
                "name", "Name");

        Should.ThrowAsync<ArgumentException>(
            () => kit.Tools["greet"].ExecuteAsync(new Dictionary<string, object?>()));
    }

    [Test]
    public void ToolKit_Name_IsSet()
    {
        var kit = new ToolKit("my-tools");
        kit.Name.ShouldBe("my-tools");
    }

    [Test]
    public async Task AddTool_AsyncNoParams_ExecutesAsync()
    {
        var kit = new ToolKit("test")
            .AddTool("ping", "Async ping", async () =>
            {
                await Task.Delay(1);
                return "pong";
            });

        var result = await kit.Tools["ping"].ExecuteAsync(new Dictionary<string, object?>());
        result.Value.ShouldBe("pong");
    }

    [Test]
    public async Task AddTool_AsyncSingleParam_ExecutesAsync()
    {
        var kit = new ToolKit("test")
            .AddTool("fetch", "Fetches data",
                async (string url) =>
                {
                    await Task.Delay(1);
                    return $"data from {url}";
                },
                "url", "The URL to fetch");

        var result = await kit.Tools["fetch"].ExecuteAsync(
            new Dictionary<string, object?> { ["url"] = "https://example.com" });
        result.Value.ShouldBe("data from https://example.com");
    }

    [Test]
    public async Task AddTool_AsyncTwoParams_ExecutesAsync()
    {
        var kit = new ToolKit("test")
            .AddTool("combine", "Combines values",
                async (string a, string b) =>
                {
                    await Task.Delay(1);
                    return $"{a}:{b}";
                },
                ("a", "First"), ("b", "Second"));

        var result = await kit.Tools["combine"].ExecuteAsync(
            new Dictionary<string, object?> { ["a"] = "x", ["b"] = "y" });
        result.Value.ShouldBe("x:y");
    }

    [Test]
    public void ParametersJsonSchema_ProducesValidSchema()
    {
        var kit = new ToolKit("test")
            .AddTool("search", "Searches", (string query) => query, "query", "Search query");

        var schema = kit.Tools["search"].ParametersJsonSchema;
        schema.ShouldContain("\"query\"");
        schema.ShouldContain("\"string\"");
        schema.ShouldContain("\"required\"");
    }

    [Test]
    public async Task AddTool_TypedInt_ExecutesWithConvertedArg()
    {
        var kit = new ToolKit("test")
            .AddTool<int>("multiply", "Doubles a number",
                (int n) => (n * 2).ToString(),
                "value", "The number to double");

        var tool = kit.Tools["multiply"];
        tool.Parameters[0].JsonType.ShouldBe("integer");

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["value"] = 5.0 });
        result.Value.ShouldBe("10");
    }

    [Test]
    public async Task AddTool_TypedDouble_ExecutesDirectly()
    {
        var kit = new ToolKit("test")
            .AddTool<double>("half", "Halves a number",
                (double n) => (n / 2).ToString(),
                "value", "The number to halve");

        var tool = kit.Tools["half"];
        tool.Parameters[0].JsonType.ShouldBe("number");

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["value"] = 10.0 });
        result.Value.ShouldBe("5");
    }

    [Test]
    public async Task AddTool_TypedBool_ExecutesWithConvertedArg()
    {
        var kit = new ToolKit("test")
            .AddTool<bool>("toggle", "Negates a boolean",
                (bool b) => (!b).ToString(),
                "flag", "The boolean flag");

        var tool = kit.Tools["toggle"];
        tool.Parameters[0].JsonType.ShouldBe("boolean");

        var result = await tool.ExecuteAsync(new Dictionary<string, object?> { ["flag"] = true });
        result.Value.ShouldBe("False");
    }

    [Test]
    public async Task AddTool_TypedTwoParams_MixedTypes()
    {
        var kit = new ToolKit("test")
            .AddTool<string, int>("repeat", "Repeats text N times",
                (string text, int count) => string.Concat(Enumerable.Repeat(text, count)),
                ("text", "The text to repeat"), ("count", "Number of repetitions"));

        var tool = kit.Tools["repeat"];
        tool.Parameters[0].JsonType.ShouldBe("string");
        tool.Parameters[1].JsonType.ShouldBe("integer");

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["text"] = "ab",
            ["count"] = 3.0
        });
        result.Value.ShouldBe("ababab");
    }

    [Test]
    public async Task AddTool_TypedAsync_ExecutesAsync()
    {
        var kit = new ToolKit("test")
            .AddTool<int>("square", "Squares a number",
                async (int n) =>
                {
                    await Task.Delay(1);
                    return (n * n).ToString();
                },
                "value", "The number to square");

        var result = await kit.Tools["square"].ExecuteAsync(
            new Dictionary<string, object?> { ["value"] = 4.0 });
        result.Value.ShouldBe("16");
    }

    [Test]
    public async Task AddTool_TypedTwoParamsAsync_ExecutesAsync()
    {
        var kit = new ToolKit("test")
            .AddTool<double, bool>("format", "Formats a number",
                async (double n, bool round) =>
                {
                    await Task.Delay(1);
                    return round ? Math.Round(n).ToString() : n.ToString();
                },
                ("number", "The number"), ("round", "Whether to round"));

        var result = await kit.Tools["format"].ExecuteAsync(
            new Dictionary<string, object?> { ["number"] = 3.7, ["round"] = true });
        result.Value.ShouldBe("4");
    }

    [Test]
    public void AddTool_TypedInvalidConversion_ThrowsArgumentException()
    {
        var kit = new ToolKit("test")
            .AddTool<int>("bad", "Fails on non-numeric",
                (int n) => n.ToString(),
                "value", "A number");

        Should.ThrowAsync<ArgumentException>(
            () => kit.Tools["bad"].ExecuteAsync(
                new Dictionary<string, object?> { ["value"] = "not-a-number" }));
    }

    [Test]
    public async Task AddTool_TypedInt_DeserializesFromJsonElement()
    {
        var kit = new ToolKit("test")
            .AddTool<int>("double", "Doubles a number",
                (int n) => (n * 2).ToString(),
                "value", "The number");

        using var doc = JsonDocument.Parse("""{"value": 7}""");
        var args = new Dictionary<string, object?>();
        foreach (var prop in doc.RootElement.EnumerateObject())
            args[prop.Name] = prop.Value.Clone();

        var result = await kit.Tools["double"].ExecuteAsync(args);
        result.Value.ShouldBe("14");
    }

    [Test]
    public async Task AddTool_StringParam_DeserializesFromJsonElement()
    {
        var kit = new ToolKit("test")
            .AddTool("echo", "Echoes input", (string s) => s,
                "text", "The text");

        using var doc = JsonDocument.Parse("""{"text": "hello"}""");
        var args = new Dictionary<string, object?>();
        foreach (var prop in doc.RootElement.EnumerateObject())
            args[prop.Name] = prop.Value.Clone();

        var result = await kit.Tools["echo"].ExecuteAsync(args);
        result.Value.ShouldBe("hello");
    }

    [Test]
    public async Task AddTool_TypedMixed_DeserializesFromJsonElement()
    {
        var kit = new ToolKit("test")
            .AddTool<string, bool>("format", "Formats text",
                (string text, bool upper) => upper ? text.ToUpperInvariant() : text,
                ("text", "Input"), ("upper", "Uppercase flag"));

        using var doc = JsonDocument.Parse("""{"text": "hello", "upper": true}""");
        var args = new Dictionary<string, object?>();
        foreach (var prop in doc.RootElement.EnumerateObject())
            args[prop.Name] = prop.Value.Clone();

        var result = await kit.Tools["format"].ExecuteAsync(args);
        result.Value.ShouldBe("HELLO");
    }

    [Test]
    public async Task AddTool_ToolDefinition_RegistersDirectly()
    {
        var definition = new ToolDefinition
        {
            Name = "echo",
            Description = "Echoes input",
            Parameters = [new ToolParameter("text", "The text to echo")],
            Execute = (args, _) => Task.FromResult(ToolResult.Ok(args["text"]?.ToString() ?? ""))
        };

        var kit = new ToolKit("test").AddTool(definition);

        kit.Tools.ShouldContainKey("echo");
        kit.Tools["echo"].Description.ShouldBe("Echoes input");
        kit.Tools["echo"].Parameters.Count.ShouldBe(1);

        var result = await kit.Tools["echo"].ExecuteAsync(
            new Dictionary<string, object?> { ["text"] = "hello" });
        result.Value.ShouldBe("hello");
    }

    [Test]
    public void AddTool_ToolDefinition_NullThrows()
    {
        var kit = new ToolKit("test");
        Should.Throw<ArgumentNullException>(() => kit.AddTool((ToolDefinition)null!));
    }

    [Test]
    public void ToolDefinition_Tags_DefaultsToEmpty()
    {
        var tool = new ToolDefinition
        {
            Name = "test", Description = "desc", Parameters = [],
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
        };

        tool.Tags.ShouldBeEmpty();
    }

    [Test]
    public void ToolDefinition_Examples_DefaultsToEmpty()
    {
        var tool = new ToolDefinition
        {
            Name = "test", Description = "desc", Parameters = [],
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
        };

        tool.Examples.ShouldBeEmpty();
    }

    [Test]
    public void ToolDefinition_Tags_CanBeSet()
    {
        var tool = new ToolDefinition
        {
            Name = "search", Description = "Searches", Parameters = [],
            Tags = ["retrieval", "web"],
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
        };

        tool.Tags.ShouldBe(["retrieval", "web"]);
    }

    [Test]
    public void ToolDefinition_Examples_CanBeSet()
    {
        var tool = new ToolDefinition
        {
            Name = "search", Description = "Searches", Parameters = [],
            Examples = ["search for cats", "find documents about AI"],
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
        };

        tool.Examples.ShouldBe(["search for cats", "find documents about AI"]);
    }

    [Test]
    public void ToolParameter_Examples_DefaultsToNull()
    {
        var param = new ToolParameter("query", "Search query");
        param.Examples.ShouldBeNull();
    }

    [Test]
    public void ToolParameter_Examples_CanBeSet()
    {
        var param = new ToolParameter("query", "Search query",
            Examples: ["distributed consensus", "Raft vs Paxos"]);

        param.Examples.ShouldBe(["distributed consensus", "Raft vs Paxos"]);
    }

    [Test]
    public void ParametersJsonSchema_WithExamples_EmitsExamplesAnnotation()
    {
        var tool = new ToolDefinition
        {
            Name = "search", Description = "Searches",
            Parameters = [new ToolParameter("query", "Search query",
                Examples: ["distributed consensus", "Raft vs Paxos"])],
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
        };

        var schema = tool.ParametersJsonSchema;
        schema.ShouldContain("\"examples\"");
        schema.ShouldContain("distributed consensus");
        schema.ShouldContain("Raft vs Paxos");
    }

    [Test]
    public void ParametersJsonSchema_WithoutExamples_OmitsExamplesKey()
    {
        var tool = new ToolDefinition
        {
            Name = "search", Description = "Searches",
            Parameters = [new ToolParameter("query", "Search query")],
            Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
        };

        var schema = tool.ParametersJsonSchema;
        schema.ShouldNotContain("\"examples\"");
    }
}
