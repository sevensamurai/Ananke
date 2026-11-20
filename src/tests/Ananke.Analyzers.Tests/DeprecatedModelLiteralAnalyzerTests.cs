using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Shouldly;

namespace Ananke.Analyzers.Tests;

[TestFixture]
public sealed class DeprecatedModelLiteralAnalyzerTests
{
    private const string LifecycleJson = """
        [
          { "id": "gpt-4.1", "status": "Deprecated", "replacedBy": "gpt-5.5" },
          { "id": "claude-opus-4", "status": "Retired", "replacedBy": "claude-opus-4-8" },
          { "id": "gpt-5.2", "status": "Legacy", "replacedBy": "gpt-5.5" }
        ]
        """;

    [Test]
    public async Task DeprecatedModelLiteral_ReportsWarning()
    {
        var source = """
            class MyMapper
            {
                public string Resolve() => {|#0:"gpt-4.1"|};
            }
            """;

        await VerifyDiagnosticAsync(source,
            new DiagnosticResult(DeprecatedModelLiteralAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("gpt-4.1", "gpt-5.5"));
    }

    [Test]
    public async Task RetiredModelLiteral_ReportsError()
    {
        var source = """
            class MyMapper
            {
                public string Resolve() => {|#0:"claude-opus-4"|};
            }
            """;

        await VerifyDiagnosticAsync(source,
            new DiagnosticResult(DeprecatedModelLiteralAnalyzer.RetiredDiagnosticId, DiagnosticSeverity.Error)
                .WithLocation(0)
                .WithArguments("claude-opus-4", "claude-opus-4-8"));
    }

    [Test]
    public async Task LegacyModelLiteral_NoDiagnostic()
    {
        // Legacy means "still fully supported" — only Deprecated/Retired are ever flagged.
        var source = """
            class MyMapper
            {
                public string Resolve() => "gpt-5.2";
            }
            """;

        await VerifyNoDiagnosticsAsync(source);
    }

    [Test]
    public async Task UnknownStringLiteral_NoDiagnostic()
    {
        var source = """
            class MyMapper
            {
                public string Resolve() => "not-a-model-id";
            }
            """;

        await VerifyNoDiagnosticsAsync(source);
    }

    [Test]
    public async Task Analyze_NoAdditionalFile_ReportsFromEmbeddedLifecycleFile()
    {
        // A product referencing the package passes no model-lifecycle.json: the analyzer reads
        // the copy embedded in its own assembly, the same file ModelLifecycleData reads.
        var deprecated = EmbeddedDeprecatedEntry();
        var source = $$"""
            class MyMapper
            {
                public string Resolve() => {|#0:"{{deprecated.Id}}"|};
            }
            """;

        var test = new CSharpAnalyzerTest<DeprecatedModelLiteralAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Net10Reference
        };
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DeprecatedModelLiteralAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments(deprecated.Id, deprecated.ReplacedBy!));
        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task Analyze_AdditionalFileGiven_ReplacesEmbeddedLifecycleFile()
    {
        // The id is Deprecated in the embedded file but absent from the one given: the given
        // file is read instead of the embedded one, not merged with it.
        var deprecated = EmbeddedDeprecatedEntry();
        LifecycleJson.ShouldNotContain($"\"{deprecated.Id}\"");
        var source = $$"""
            class MyMapper
            {
                public string Resolve() => "{{deprecated.Id}}";
            }
            """;

        await VerifyNoDiagnosticsAsync(source);
    }

    private static ModelLifecycleEntry EmbeddedDeprecatedEntry() =>
        ModelLifecycleData.Entries.Values
            .Where(e => e.Status == ModelStatus.Deprecated && e.ReplacedBy is not null)
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .First();

    private static async Task VerifyNoDiagnosticsAsync(string source)
    {
        var test = new CSharpAnalyzerTest<DeprecatedModelLiteralAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Net10Reference
        };
        test.TestState.AdditionalFiles.Add(("model-lifecycle.json", LifecycleJson));
        await test.RunAsync().ConfigureAwait(false);
    }

    private static async Task VerifyDiagnosticAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<DeprecatedModelLiteralAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Net10Reference
        };
        test.TestState.AdditionalFiles.Add(("model-lifecycle.json", LifecycleJson));
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync().ConfigureAwait(false);
    }

    private static readonly ReferenceAssemblies Net10Reference =
        new("net10.0",
            new PackageIdentity("Microsoft.NETCore.App.Ref", "10.0.0"),
            System.IO.Path.Combine("ref", "net10.0"));
}
