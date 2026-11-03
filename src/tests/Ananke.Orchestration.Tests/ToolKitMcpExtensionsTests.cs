using System.Text.Json;
using Ananke.MCP;
using Ananke.Orchestration.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class ToolKitMcpExtensionsTests
{
    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement;

    [Test]
    public void ParseParameters_AParameterTypedAsAString_KeepsThatType()
    {
        var parameters = ToolKitMcpExtensions.ParseParameters(Schema(
            """{ "type": "object", "properties": { "line": { "type": "integer" } }, "required": ["line"] }"""));

        var line = parameters.ShouldHaveSingleItem();
        line.JsonType.ShouldBe("integer");
        line.IsRequired.ShouldBeTrue();
    }

    [Test]
    public void ParseParameters_ANullableParameter_TakesTheTypeThatIsNotNull()
    {
        // A server describing an optional string writes its type as a list, as JSON Schema allows.
        var parameters = ToolKitMcpExtensions.ParseParameters(Schema(
            """{ "type": "object", "properties": { "name": { "type": ["string", "null"], "description": "Optional." } } }"""));

        var name = parameters.ShouldHaveSingleItem();
        name.JsonType.ShouldBe("string");
        name.Description.ShouldBe("Optional.");
        name.IsRequired.ShouldBeFalse();
    }

    // ── An MCP tool type, in-process ──

    [McpServerToolType]
    public sealed class Words
    {
        [McpServerTool(Name = "words_repeat")]
        [System.ComponentModel.Description("Repeats a word.")]
        public string Repeat(
            [System.ComponentModel.Description("The word.")] string word,
            [System.ComponentModel.Description("How many times.")] int times = 2) => string.Join(" ", Enumerable.Repeat(word, times));

        [McpServerTool(Name = "words_fail")]
        [System.ComponentModel.Description("Always fails.")]
        public string Fail() => throw new InvalidOperationException("no word here");

        [McpServerTool(Name = "words_refuse")]
        [System.ComponentModel.Description("Answers an error result, as an MCP tool reports a failure.")]
        public CallToolResult Refuse() => new()
        {
            IsError = true,
            Content = [new TextContentBlock { Text = "no such word" }]
        };

        [McpServerTool(Name = "words_split")]
        [System.ComponentModel.Description("Answers each word as its own block, later.")]
        public async Task<CallToolResult> SplitAsync(string text)
        {
            await Task.Yield();
            return new CallToolResult { Content = [.. text.Split(' ').Select(w => new TextContentBlock { Text = w })] };
        }

        [McpServerTool(Name = "words_block")]
        [System.ComponentModel.Description("Answers one content block.")]
        public ContentBlock Block() => new TextContentBlock { Text = "one block" };

        public string NotATool() => "unseen";
    }

    private static IReadOnlyDictionary<string, object?> Args(string json) =>
        JsonDocument.Parse(json).RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());

    [Test]
    public void AddMcpServerTools_ATypesToolMethods_ArriveAsTheSdkDeclaresThem()
    {
        var kit = new ToolKit("words").AddMcpServerTools(new Words());

        kit.Tools.Keys.Order().ShouldBe(["words_block", "words_fail", "words_refuse", "words_repeat", "words_split"]);

        var repeat = kit.Tools["words_repeat"];
        repeat.Description.ShouldBe("Repeats a word.");
        repeat.Parameters.Select(p => (p.Name, p.JsonType, p.IsRequired, p.Description))
            .ShouldBe([("word", "string", true, "The word."), ("times", "integer", false, "How many times.")]);
    }

    [Test]
    public async Task AddMcpServerTools_ACall_ReturnsTheMethodsAnswer_TheDefaultTakenForAnArgumentLeftOut()
    {
        var repeat = new ToolKit("words").AddMcpServerTools(new Words()).Tools["words_repeat"];

        (await repeat.ExecuteAsync(Args("""{ "word": "hi", "times": 3 }"""))).ShouldBe(ToolResult.Ok("hi hi hi"));
        (await repeat.ExecuteAsync(Args("""{ "word": "hi" }"""))).ShouldBe(ToolResult.Ok("hi hi"));
    }

    [Test]
    public async Task AddMcpServerTools_AMethodThatThrows_GivesAnErrorResult_WithItsMessage()
    {
        var result = await new ToolKit("words").AddMcpServerTools(new Words()).Tools["words_fail"].ExecuteAsync(Args("{}"));

        result.IsError.ShouldBeTrue();
        result.Value.ShouldContain("no word here");
    }

    [Test]
    public async Task AddMcpServerTools_AMethodThatAnswersAnErrorResult_GivesAnErrorResult_WithItsText()
    {
        // As a client of that server would read it: IsError is the failure, not a JSON field.
        var result = await new ToolKit("words").AddMcpServerTools(new Words()).Tools["words_refuse"].ExecuteAsync(Args("{}"));

        result.ShouldBe(ToolResult.Error("no such word"));
    }

    [Test]
    public async Task AddMcpServerTools_AnAsyncMethodAnsweringBlocks_GivesTheirText_OneLineEach()
    {
        var result = await new ToolKit("words").AddMcpServerTools(new Words()).Tools["words_split"]
            .ExecuteAsync(Args("""{ "text": "a b" }"""));

        result.ShouldBe(ToolResult.Ok("a\nb"));
    }

    [Test]
    public async Task AddMcpServerTools_AMethodAnsweringOneContentBlock_GivesItsText()
    {
        var result = await new ToolKit("words").AddMcpServerTools(new Words()).Tools["words_block"].ExecuteAsync(Args("{}"));

        result.ShouldBe(ToolResult.Ok("one block"));
    }
}
