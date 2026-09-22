using System.Text.Json;
using Ananke.Abstractions.Providers;
using Shouldly;

namespace Ananke.Orchestration.Conformance;

/// <summary>
/// The conformance contract for <see cref="IJsonSchemaTranslator"/> implementations, as
/// framework-neutral scenarios.
/// </summary>
/// <remarks>
/// <see cref="PassThroughJsonSchemaTranslator"/> is the reference subject the suite is
/// self-validated against. On a runner that discovers tests by attribute, consume
/// <c>Ananke.Orchestration.Conformance.NUnit</c> instead of driving this list directly.
/// </remarks>
public static class JsonSchemaTranslatorConformance
{
    /// <summary>Every rule an <see cref="IJsonSchemaTranslator"/> must satisfy.</summary>
    public static IReadOnlyList<ConformanceScenario<IJsonSchemaTranslator>> Scenarios { get; } =
    [
        // ── 1. Basic translation ─────────────────────────────────────────────

        new("Translate_SimpleObjectSchema_ReturnsNonNull", translator =>
        {
            var result = translator.Translate(SimpleObjectSchema());
            result.ShouldNotBeNull();
            return ConformanceOutcome.Passed;
        }),

        new("Translate_StringSchema_ReturnsNonNull", translator =>
        {
            var result = translator.Translate(StringSchema());
            result.ShouldNotBeNull();
            return ConformanceOutcome.Passed;
        }),

        new("Translate_ArraySchema_ReturnsNonNull", translator =>
        {
            var result = translator.Translate(ArraySchema());
            result.ShouldNotBeNull();
            return ConformanceOutcome.Passed;
        }),

        new("Translate_EmptySchema_ReturnsNonNull", translator =>
        {
            var result = translator.Translate(new Dictionary<string, object>());
            result.ShouldNotBeNull();
            return ConformanceOutcome.Passed;
        }),

        // ── 2. Idempotency ───────────────────────────────────────────────────

        new("Translate_CalledTwiceWithSameSchema_ProducesSameResult", translator =>
        {
            var schema = SimpleObjectSchema();

            var r1 = JsonSerializer.Serialize(translator.Translate(schema));
            var r2 = JsonSerializer.Serialize(translator.Translate(schema));

            r1.ShouldBe(r2, "JsonSchemaTranslator must be idempotent for identical input");
            return ConformanceOutcome.Passed;
        }),

        // ── 3. Standard pass-through contract ────────────────────────────────

        new("Translate_StandardSchema_PreservesTypeField", translator =>
        {
            // Providers that translate schemas must preserve the "type" semantics.
            // We accept any output as long as it is not null and the type information
            // has not been silently lost (tested by ensuring the serialised result
            // contains the word "object" somewhere).
            var result = translator.Translate(SimpleObjectSchema());

            var json = JsonSerializer.Serialize(result);
            json.ShouldContain("object", Case.Insensitive,
                "Translated schema must preserve the 'object' type information");
            return ConformanceOutcome.Passed;
        })
    ];

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, object> SimpleObjectSchema() =>
        new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["name"] = new Dictionary<string, object> { ["type"] = "string" },
                ["age"] = new Dictionary<string, object> { ["type"] = "integer" }
            },
            ["required"] = new[] { "name" }
        };

    private static IReadOnlyDictionary<string, object> StringSchema() =>
        new Dictionary<string, object> { ["type"] = "string" };

    private static IReadOnlyDictionary<string, object> ArraySchema() =>
        new Dictionary<string, object>
        {
            ["type"] = "array",
            ["items"] = new Dictionary<string, object> { ["type"] = "number" }
        };
}
