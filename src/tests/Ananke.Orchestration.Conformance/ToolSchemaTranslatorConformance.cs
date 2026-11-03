using System.Text.Json;
using Ananke.Abstractions.Providers;
using Shouldly;

namespace Ananke.Orchestration.Conformance;

/// <summary>
/// The conformance contract for <see cref="IToolSchemaTranslator"/> implementations, as
/// framework-neutral scenarios.
/// </summary>
/// <remarks>
/// <see cref="PassThroughToolSchemaTranslator"/> is the reference subject the suite is
/// self-validated against. On a runner that discovers tests by attribute, consume
/// <c>Ananke.Orchestration.Conformance.NUnit</c> instead of driving this list directly.
/// </remarks>
public static class ToolSchemaTranslatorConformance
{
    /// <summary>Every rule an <see cref="IToolSchemaTranslator"/> must satisfy.</summary>
    public static IReadOnlyList<ConformanceScenario<IToolSchemaTranslator>> Scenarios { get; } =
    [
        // ── 1. Basic translation ─────────────────────────────────────────────

        new("Translate_EmptyList_ReturnsNonNull", translator =>
        {
            var result = translator.Translate([]);
            result.ShouldNotBeNull();
            return ConformanceOutcome.Passed;
        }),

        new("Translate_SingleTool_ReturnsNonNull", translator =>
        {
            var result = translator.Translate([MakeRemoteTool()]);
            result.ShouldNotBeNull();
            return ConformanceOutcome.Passed;
        }),

        new("Translate_MultipleTools_ReturnsNonNull", translator =>
        {
            var tools = new[]
            {
                MakeRemoteTool("tool_a"),
                MakeRemoteTool("tool_b"),
                MakeRemoteTool("tool_c"),
            };

            var result = translator.Translate(tools);
            result.ShouldNotBeNull();
            return ConformanceOutcome.Passed;
        }),

        new("Translate_NullInput_ThrowsArgumentNullException", translator =>
        {
            Should.Throw<ArgumentNullException>(() => translator.Translate(null!));
            return ConformanceOutcome.Passed;
        }),

        // ── 2. Idempotency ───────────────────────────────────────────────────

        new("Translate_CalledTwiceWithSameTools_ProducesSameResult", translator =>
        {
            var tools = new[] { MakeRemoteTool("idempotent_tool") };

            var r1 = translator.Translate(tools);
            var r2 = translator.Translate(tools);

            // Compare via JSON round-trip to avoid reference-equality traps.
            JsonSerializer.Serialize(r1).ShouldBe(
                JsonSerializer.Serialize(r2),
                "Translate must be idempotent for the same input");
            return ConformanceOutcome.Passed;
        }),

        // ── 3. Local-execution rejection ─────────────────────────────────────

        new("Translate_LocalTool_ThrowsOrReturnsNonNull", translator =>
        {
            // Providers that disallow Local-mode tools (e.g. OpenAI) must throw.
            // Providers that silently skip or accept them must at least return non-null.
            var localTool = new ProviderTool("local_tool", "runs in-process", "{}")
            {
                ExecutionMode = ToolExecutionMode.Local
            };

            try
            {
                translator.Translate([localTool]).ShouldNotBeNull();
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                // correct strict-mode behaviour
            }

            return ConformanceOutcome.Passed;
        })
    ];

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ProviderTool MakeRemoteTool(string name = "test_tool") =>
        new(name, "A test tool", "{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false}")
        {
            ExecutionMode = ToolExecutionMode.Callback
        };
}
