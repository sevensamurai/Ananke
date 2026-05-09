using Ananke.Orchestration.Translators;
using Shouldly;

namespace Ananke.Orchestration.Conformance.Tests;

/// <summary>
/// Abstract conformance suite for <see cref="IJsonSchemaTranslator"/> implementations.
/// </summary>
/// <remarks>
/// Subclass in a provider's test project and override <see cref="CreateTranslator"/> to
/// test the real provider dialect.  <see cref="FakeJsonSchemaTranslatorConformanceTests"/>
/// validates the suite itself using a pass-through reference implementation.
/// </remarks>
[TestFixture]
public abstract class JsonSchemaTranslatorConformanceTests
{
    protected abstract IJsonSchemaTranslator CreateTranslator();

    // ── Helpers ──────────────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, object> SimpleObjectSchema() =>
        new Dictionary<string, object>
        {
            ["type"]       = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["name"] = new Dictionary<string, object> { ["type"] = "string" },
                ["age"]  = new Dictionary<string, object> { ["type"] = "integer" }
            },
            ["required"] = new[] { "name" }
        };

    private static IReadOnlyDictionary<string, object> StringSchema() =>
        new Dictionary<string, object> { ["type"] = "string" };

    private static IReadOnlyDictionary<string, object> ArraySchema() =>
        new Dictionary<string, object>
        {
            ["type"]  = "array",
            ["items"] = new Dictionary<string, object> { ["type"] = "number" }
        };

    // ── 1. Basic translation ─────────────────────────────────────────────

    [Test]
    public void Translate_SimpleObjectSchema_ReturnsNonNull()
    {
        var translator = CreateTranslator();
        var result = translator.Translate(SimpleObjectSchema());
        result.ShouldNotBeNull();
    }

    [Test]
    public void Translate_StringSchema_ReturnsNonNull()
    {
        var translator = CreateTranslator();
        var result = translator.Translate(StringSchema());
        result.ShouldNotBeNull();
    }

    [Test]
    public void Translate_ArraySchema_ReturnsNonNull()
    {
        var translator = CreateTranslator();
        var result = translator.Translate(ArraySchema());
        result.ShouldNotBeNull();
    }

    [Test]
    public void Translate_EmptySchema_ReturnsNonNull()
    {
        var translator = CreateTranslator();
        var result = translator.Translate(new Dictionary<string, object>());
        result.ShouldNotBeNull();
    }

    // ── 2. Idempotency ───────────────────────────────────────────────────

    [Test]
    public void Translate_CalledTwiceWithSameSchema_ProducesSameResult()
    {
        var translator = CreateTranslator();
        var schema = SimpleObjectSchema();

        var r1 = System.Text.Json.JsonSerializer.Serialize(translator.Translate(schema));
        var r2 = System.Text.Json.JsonSerializer.Serialize(translator.Translate(schema));

        r1.ShouldBe(r2, "JsonSchemaTranslator must be idempotent for identical input");
    }

    // ── 3. Standard pass-through contract ────────────────────────────────

    [Test]
    public void Translate_StandardSchema_PreservesTypeField()
    {
        // Providers that translate schemas must preserve the "type" semantics.
        // We accept any output as long as it is not null and the type information
        // has not been silently lost (tested by ensuring the serialised result
        // contains the word "object" somewhere).
        var translator = CreateTranslator();
        var result = translator.Translate(SimpleObjectSchema());

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.ShouldContain("object", Case.Insensitive,
            "Translated schema must preserve the 'object' type information");
    }
}

/// <summary>Pass-through reference implementation that returns the input unchanged.</summary>
internal sealed class PassThroughJsonSchemaTranslatorReference : IJsonSchemaTranslator
{
    public object Translate(IReadOnlyDictionary<string, object> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return schema;
    }
}

/// <summary>Self-validating run using the pass-through reference translator.</summary>
[TestFixture]
public sealed class FakeJsonSchemaTranslatorConformanceTests : JsonSchemaTranslatorConformanceTests
{
    protected override IJsonSchemaTranslator CreateTranslator() =>
        new PassThroughJsonSchemaTranslatorReference();
}
