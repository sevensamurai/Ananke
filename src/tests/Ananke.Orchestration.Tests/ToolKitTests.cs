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
            .AddTool("add", "Adds context", b => b
                .Param("a", "First value")
                .Param("b", "Second value")
                .OnExecute(args => ToolResult.Ok($"{args.Get("a")}+{args.Get("b")}")));

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
            .AddTool("combine", "Combines values", b => b
                .Param("a", "First")
                .Param("b", "Second")
                .OnExecute(async args =>
                {
                    await Task.Delay(1);
                    return ToolResult.Ok($"{args.Get("a")}:{args.Get("b")}");
                }));

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
            .AddTool("repeat", "Repeats text N times", b => b
                .Param("text", "The text to repeat")
                .Param<int>("count", "Number of repetitions")
                .OnExecute(args => ToolResult.Ok(
                    string.Concat(Enumerable.Repeat(args.Get("text"), args.Get<int>("count"))))));

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
            .AddTool("format", "Formats a number", b => b
                .Param<double>("number", "The number")
                .Param<bool>("round", "Whether to round")
                .OnExecute(async args =>
                {
                    await Task.Delay(1);
                    var n = args.Get<double>("number");
                    var round = args.Get<bool>("round");
                    return ToolResult.Ok(round ? Math.Round(n).ToString() : n.ToString());
                }));

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
            .AddTool("format", "Formats text", b => b
                .Param("text", "Input")
                .Param<bool>("upper", "Uppercase flag")
                .OnExecute(args =>
                {
                    var text = args.Get("text");
                    var upper = args.Get<bool>("upper");
                    return ToolResult.Ok(upper ? text.ToUpperInvariant() : text);
                }));

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

    // --- Prerequisite checks ---

    [Test]
    public async Task CheckPrerequisitesAsync_NoRequires_ReturnsSuccess()
    {
        var kit = new ToolKit("test")
            .AddTool("ping", "pong", () => "pong");

        var result = await kit.CheckPrerequisitesAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Passed.ShouldBeEmpty();
        result.Failures.ShouldBeEmpty();
        result.Summary.ShouldContain("All prerequisites satisfied");
    }

    [Test]
    public async Task CheckPrerequisitesAsync_SatisfiedPrerequisite_ReturnsPassedName()
    {
        var alwaysOk = new ToolPrerequisite("fake-bin", _ => Task.FromResult(true), "n/a");
        var kit = new ToolKit("test")
            .AddTool(new ToolDefinition
            {
                Name = "tool-a", Description = "A",
                Parameters = [],
                Requires = [alwaysOk],
                Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
            });

        var result = await kit.CheckPrerequisitesAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Passed.ShouldContain("fake-bin");
    }

    [Test]
    public async Task CheckPrerequisitesAsync_MissingPrerequisite_ReturnsFailure()
    {
        var missing = new ToolPrerequisite("missing-bin", _ => Task.FromResult(false), "Run: install missing-bin");
        var kit = new ToolKit("test")
            .AddTool(new ToolDefinition
            {
                Name = "tool-b", Description = "B",
                Parameters = [],
                Requires = [missing],
                Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
            });

        var result = await kit.CheckPrerequisitesAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Failures.Count.ShouldBe(1);
        result.Failures[0].Prerequisite.ShouldBe("missing-bin");
        result.Failures[0].ToolName.ShouldBe("tool-b");
        result.Failures[0].InstallHint.ShouldBe("Run: install missing-bin");
        result.Summary.ShouldContain("missing-bin");
        result.Summary.ShouldContain("tool-b");
    }

    [Test]
    public async Task CheckPrerequisitesAsync_SharedPrerequisite_CheckedOnlyOnce()
    {
        var checkCount = 0;
        var shared = new ToolPrerequisite("shared-bin", _ =>
        {
            Interlocked.Increment(ref checkCount);
            return Task.FromResult(true);
        }, "n/a");

        var kit = new ToolKit("test")
            .AddTool(new ToolDefinition
            {
                Name = "tool-x", Description = "X",
                Parameters = [], Requires = [shared],
                Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
            })
            .AddTool(new ToolDefinition
            {
                Name = "tool-y", Description = "Y",
                Parameters = [], Requires = [shared],
                Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
            });

        await kit.CheckPrerequisitesAsync();

        checkCount.ShouldBe(1);
    }

    [Test]
    public async Task CheckPrerequisitesAsync_MixedResults_ReportsCorrectly()
    {
        var ok = new ToolPrerequisite("present", _ => Task.FromResult(true), "n/a");
        var bad = new ToolPrerequisite("absent", _ => Task.FromResult(false), "pip install absent");

        var kit = new ToolKit("test")
            .AddTool(new ToolDefinition
            {
                Name = "good-tool", Description = "G",
                Parameters = [], Requires = [ok],
                Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
            })
            .AddTool(new ToolDefinition
            {
                Name = "bad-tool", Description = "B",
                Parameters = [], Requires = [bad],
                Execute = (_, _) => Task.FromResult(ToolResult.Ok("ok"))
            });

        var result = await kit.CheckPrerequisitesAsync();

        result.IsSuccess.ShouldBeFalse();
        result.Passed.ShouldContain("present");
        result.Failures.Count.ShouldBe(1);
        result.Failures[0].Prerequisite.ShouldBe("absent");
    }

    [Test]
    public async Task BinaryPrerequisite_DetectsDotnet()
    {
        // 'dotnet' should always be available in test environment
        var prereq = ToolPrerequisite.Binary("dotnet",
            "Install .NET: https://dot.net");

        var ok = await prereq.Check(CancellationToken.None);

        ok.ShouldBeTrue();
    }

    [Test]
    public async Task BinaryPrerequisite_DetectsMissingBinary()
    {
        var prereq = ToolPrerequisite.Binary("this-binary-does-not-exist-xyz",
            "You won't find this");

        var ok = await prereq.Check(CancellationToken.None);

        ok.ShouldBeFalse();
    }

    // --- ToolBuilder ---

    [Test]
    public async Task Builder_ThreeParams_ExecutesWithAllArgs()
    {
        var kit = new ToolKit("test")
            .AddTool("send", "Sends a message", b => b
                .Param("to", "Recipient")
                .Param("subject", "Subject line")
                .Param("body", "Message body")
                .OnExecute(args => ToolResult.Ok(
                    $"{args.Get("to")}:{args.Get("subject")}:{args.Get("body")}")));

        var tool = kit.Tools["send"];
        tool.Parameters.Count.ShouldBe(3);

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["to"] = "alice",
            ["subject"] = "hi",
            ["body"] = "hello"
        });
        result.Value.ShouldBe("alice:hi:hello");
    }

    [Test]
    public async Task Builder_TypedParams_InfersJsonType()
    {
        var kit = new ToolKit("test")
            .AddTool("calc", "Calculates", b => b
                .Param<double>("a", "First operand")
                .Param<int>("b", "Second operand")
                .Param<bool>("round", "Whether to round")
                .OnExecute(args =>
                {
                    var a = args.Get<double>("a");
                    var bVal = args.Get<int>("b");
                    var round = args.Get<bool>("round");
                    var val = a + bVal;
                    return ToolResult.Ok(round ? Math.Round(val).ToString() : val.ToString());
                }));

        var tool = kit.Tools["calc"];
        tool.Parameters[0].JsonType.ShouldBe("number");
        tool.Parameters[1].JsonType.ShouldBe("integer");
        tool.Parameters[2].JsonType.ShouldBe("boolean");

        var result = await tool.ExecuteAsync(new Dictionary<string, object?>
        {
            ["a"] = 1.5, ["b"] = 2, ["round"] = true
        });
        result.Value.ShouldBe("4");
    }

    [Test]
    public void Builder_Tags_AppearsOnDefinition()
    {
        var kit = new ToolKit("test")
            .AddTool("tagged", "A tagged tool", b => b
                .Tags("finance", "trading")
                .OnExecute(_ => ToolResult.Ok("ok")));

        kit.Tools["tagged"].Tags.ShouldBe(["finance", "trading"]);
    }

    [Test]
    public void Builder_Examples_AppearsOnDefinition()
    {
        var kit = new ToolKit("test")
            .AddTool("example", "An example tool", b => b
                .Examples("do X", "do Y")
                .OnExecute(_ => ToolResult.Ok("ok")));

        kit.Tools["example"].Examples.ShouldBe(["do X", "do Y"]);
    }

    [Test]
    public void Builder_ParamExamples_AppearsInSchema()
    {
        var kit = new ToolKit("test")
            .AddTool("lookup", "Looks up a symbol", b => b
                .Param("symbol", "Ticker", examples: ["AAPL", "MSFT"])
                .OnExecute(args => ToolResult.Ok(args.Get("symbol"))));

        var tool = kit.Tools["lookup"];
        tool.Parameters[0].Examples.ShouldBe(["AAPL", "MSFT"]);
        tool.ParametersJsonSchema.ShouldContain("\"examples\"");
    }

    [Test]
    public void Builder_OptionalParam_NotInRequired()
    {
        var kit = new ToolKit("test")
            .AddTool("search", "Searches", b => b
                .Param("query", "Search query")
                .Param("category", "Optional filter", required: false)
                .OnExecute(args => ToolResult.Ok("ok")));

        var tool = kit.Tools["search"];
        tool.Parameters[0].IsRequired.ShouldBeTrue();
        tool.Parameters[1].IsRequired.ShouldBeFalse();
        tool.ParametersJsonSchema.ShouldContain("\"query\"");
        tool.ParametersJsonSchema.ShouldNotContain("\"category\":[^}]*required");
    }

    [Test]
    public async Task Builder_AsyncWithCancellation_ReceivesToken()
    {
        using var cts = new CancellationTokenSource();
        var tokenReceived = false;

        var kit = new ToolKit("test")
            .AddTool("cancellable", "Supports cancellation", b => b
                .OnExecute((args, ct) =>
                {
                    tokenReceived = !ct.IsCancellationRequested;
                    return Task.FromResult(ToolResult.Ok("done"));
                }));

        await kit.Tools["cancellable"].ExecuteAsync(
            new Dictionary<string, object?>(), cts.Token);

        tokenReceived.ShouldBeTrue();
    }

    [Test]
    public void Builder_NoOnExecute_ThrowsOnBuild()
    {
        var kit = new ToolKit("test");
        Should.Throw<InvalidOperationException>(() =>
            kit.AddTool("broken", "No handler", _ => { }));
    }

    [Test]
    public void Builder_Requires_AppearsOnDefinition()
    {
        var prereq = new ToolPrerequisite("test-bin", _ => Task.FromResult(true), "install it");
        var kit = new ToolKit("test")
            .AddTool("need_bin", "Needs a binary", b => b
                .Requires(prereq)
                .OnExecute(_ => ToolResult.Ok("ok")));

        kit.Tools["need_bin"].Requires.Count.ShouldBe(1);
        kit.Tools["need_bin"].Requires[0].Name.ShouldBe("test-bin");
    }
}
