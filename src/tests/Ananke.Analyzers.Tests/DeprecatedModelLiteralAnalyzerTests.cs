using Ananke.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

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
    public async Task NoAdditionalFile_NoDiagnostic()
    {
        // Without model-lifecycle.json wired in, the analyzer must not fire on anything —
        // confirms it fails closed (silently does nothing) rather than throwing or false-flagging.
        var source = """
            class MyMapper
            {
                public string Resolve() => "gpt-4.1";
            }
            """;

        var test = new CSharpAnalyzerTest<DeprecatedModelLiteralAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Net10Reference
        };
        await test.RunAsync();
    }

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
