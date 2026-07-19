using Ananke.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Ananke.Analyzers.Tests;

[TestFixture]
public sealed class DeprecatedModelLiteralCodeFixProviderTests
{
    private const string LifecycleJson = """
        [
          { "id": "gpt-4.1", "status": "Deprecated", "replacedBy": "gpt-5.5" },
          { "id": "no-known-constant-for-this-one", "status": "Deprecated", "replacedBy": "also-unknown" }
        ]
        """;

    [Test]
    public async Task DeprecatedLiteral_WithKnownConstant_ReplacesWithConstantReference()
    {
        // "gpt-4.1"'s recorded replacement, "gpt-5.5", matches Models.OpenAI.Gpt55's real value —
        // the fix should resolve the symbolic constant, not just swap in a bare string.
        var source = """
            using Ananke.Abstractions.Agents;

            class MyMapper
            {
                public string Resolve() => {|#0:"gpt-4.1"|};
            }
            """;

        var fixedSource = """
            using Ananke.Abstractions.Agents;

            class MyMapper
            {
                public string Resolve() => global::Ananke.Abstractions.Agents.Models.OpenAI.Gpt55;
            }
            """;

        await VerifyFixAsync(source, fixedSource,
            new DiagnosticResult(DeprecatedModelLiteralAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("gpt-4.1", "gpt-5.5"),
            withAbstractionsReference: true);
    }

    [Test]
    public async Task DeprecatedLiteral_WithNoMatchingConstant_FallsBackToReplacementLiteral()
    {
        // No project reference to Ananke.Abstractions in this test's sandboxed compilation, so
        // the semantic search can never find a matching constant — proves the fallback path
        // still produces a correct (if less ideal) fix instead of leaving the diagnostic unfixed.
        var source = """
            class MyMapper
            {
                public string Resolve() => {|#0:"gpt-4.1"|};
            }
            """;

        var fixedSource = """
            class MyMapper
            {
                public string Resolve() => "gpt-5.5";
            }
            """;

        await VerifyFixAsync(source, fixedSource,
            new DiagnosticResult(DeprecatedModelLiteralAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("gpt-4.1", "gpt-5.5"),
            withAbstractionsReference: false);
    }

    private static async Task VerifyFixAsync(
        string source, string fixedSource, DiagnosticResult expected, bool withAbstractionsReference)
    {
        var test = new CSharpCodeFixTest<DeprecatedModelLiteralAnalyzer, DeprecatedModelLiteralCodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = Net10Reference
        };
        test.TestState.AdditionalFiles.Add(("model-lifecycle.json", LifecycleJson));
        test.ExpectedDiagnostics.Add(expected);

        if (withAbstractionsReference)
        {
            test.TestState.AdditionalReferences.Add(
                MetadataReference.CreateFromFile(typeof(Ananke.Abstractions.Agents.Models).Assembly.Location));
        }

        await test.RunAsync().ConfigureAwait(false);
    }

    private static readonly ReferenceAssemblies Net10Reference =
        new("net10.0",
            new PackageIdentity("Microsoft.NETCore.App.Ref", "10.0.0"),
            System.IO.Path.Combine("ref", "net10.0"));
}
