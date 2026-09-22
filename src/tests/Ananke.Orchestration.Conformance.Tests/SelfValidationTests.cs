using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Providers;
using Ananke.Orchestration.Conformance.NUnit;

namespace Ananke.Orchestration.Conformance.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Self-validation: the conformance suite run against its own reference subjects.
//
// This project is the suite's *consumer*, not its home — the fixtures live in
// Ananke.Orchestration.Conformance so that every adapter's test project can
// reference them too. What these three subclasses prove is
// only that the scenarios themselves are correct and pass without credentials;
// the adapters get their own subclasses in C4.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Runs the <see cref="IStreamingAgentModel"/> suite against <see cref="FakeConformanceModel"/>.
/// </summary>
[TestFixture]
public sealed class FakeConformanceTests : StreamingAgentModelConformanceTests
{
    protected override IStreamingAgentModel CreateModel() => new FakeConformanceModel();
}

/// <summary>
/// Runs the <see cref="IToolSchemaTranslator"/> suite against the pass-through reference translator.
/// </summary>
[TestFixture]
public sealed class FakeToolSchemaTranslatorConformanceTests : ToolSchemaTranslatorConformanceTests
{
    protected override IToolSchemaTranslator CreateTranslator() =>
        new PassThroughToolSchemaTranslator();
}

/// <summary>
/// Runs the <see cref="IJsonSchemaTranslator"/> suite against the pass-through reference translator.
/// </summary>
[TestFixture]
public sealed class FakeJsonSchemaTranslatorConformanceTests : JsonSchemaTranslatorConformanceTests
{
    protected override IJsonSchemaTranslator CreateTranslator() =>
        new PassThroughJsonSchemaTranslator();
}
