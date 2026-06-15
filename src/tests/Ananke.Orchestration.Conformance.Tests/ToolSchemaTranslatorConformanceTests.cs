using System.Text.Json;
using Ananke.Abstractions.Providers;
using Shouldly;

namespace Ananke.Orchestration.Conformance.Tests;

/// <summary>
/// Abstract conformance suite for <see cref="IToolSchemaTranslator"/> implementations.
/// </summary>
/// <remarks>
/// Subclass this in a provider's test project and override <see cref="CreateTranslator"/>
/// to plug in the real translator. The <see cref="FakeToolSchemaTranslatorConformanceTests"/>
/// subclass below runs the suite against a pass-through reference implementation so the
/// suite is self-validating in CI.
/// </remarks>
[TestFixture]
public abstract class ToolSchemaTranslatorConformanceTests
{
    protected abstract IToolSchemaTranslator CreateTranslator();

    // ── Helpers ──────────────────────────────────────────────────────────

    private static ProviderTool MakeRemoteTool(string name = "test_tool") =>
        new(name, "A test tool", "{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false}")
        {
            ExecutionMode = ToolExecutionMode.Callback
        };

    // ── 1. Basic translation ─────────────────────────────────────────────

    [Test]
    public void Translate_EmptyList_ReturnsNonNull()
    {
        var translator = CreateTranslator();
        var result = translator.Translate([]);
        result.ShouldNotBeNull();
    }

    [Test]
    public void Translate_SingleTool_ReturnsNonNull()
    {
        var translator = CreateTranslator();
        var result = translator.Translate([MakeRemoteTool()]);
        result.ShouldNotBeNull();
    }

    [Test]
    public void Translate_MultipleTools_ReturnsNonNull()
    {
        var translator = CreateTranslator();
        var tools = new[]
        {
            MakeRemoteTool("tool_a"),
            MakeRemoteTool("tool_b"),
            MakeRemoteTool("tool_c"),
        };

        var result = translator.Translate(tools);
        result.ShouldNotBeNull();
    }

    [Test]
    public void Translate_NullInput_ThrowsArgumentNullException()
    {
        var translator = CreateTranslator();
        Should.Throw<ArgumentNullException>(() => translator.Translate(null!));
    }

    // ── 2. Idempotency ───────────────────────────────────────────────────

    [Test]
    public void Translate_CalledTwiceWithSameTools_ProducesSameResult()
    {
        var translator = CreateTranslator();
        var tools = new[] { MakeRemoteTool("idempotent_tool") };

        var r1 = translator.Translate(tools);
        var r2 = translator.Translate(tools);

        // Compare via JSON round-trip to avoid reference-equality traps.
        JsonSerializer.Serialize(r1).ShouldBe(
            JsonSerializer.Serialize(r2),
            "Translate must be idempotent for the same input");
    }

    // ── 3. Local-execution rejection ─────────────────────────────────────

    [Test]
    public void Translate_LocalTool_ThrowsOrReturnsNonNull()
    {
        // Providers that disallow Local-mode tools (e.g. OpenAI) must throw.
        // Providers that silently skip or accept them must at least return non-null.
        var translator = CreateTranslator();
        var localTool = new ProviderTool("local_tool", "runs in-process", "{}")
        {
            ExecutionMode = ToolExecutionMode.Local
        };

        object? result = null;
        var act = () => result = translator.Translate([localTool]);

        // Either an exception (correct for strict providers) or a non-null result is valid.
        try { act(); result.ShouldNotBeNull(); }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        { /* correct strict-mode behaviour */ }
    }
}

/// <summary>
/// Pass-through reference translator — accepts everything, returns a JSON array of
/// tool name/description pairs.  Used to self-validate the conformance suite in CI.
/// </summary>
internal sealed class PassThroughToolSchemaTranslator : IToolSchemaTranslator
{
    public object Translate(IEnumerable<ProviderTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        return tools.Select(t => new { t.Name, t.Description }).ToList();
    }
}

/// <summary>Self-validating run using the pass-through reference translator.</summary>
[TestFixture]
public sealed class FakeToolSchemaTranslatorConformanceTests : ToolSchemaTranslatorConformanceTests
{
    protected override IToolSchemaTranslator CreateTranslator() =>
        new PassThroughToolSchemaTranslator();
}
