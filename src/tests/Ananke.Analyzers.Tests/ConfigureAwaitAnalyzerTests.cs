using Ananke.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Ananke.Analyzers.Tests;

[TestFixture]
public sealed class ConfigureAwaitAnalyzerTests
{
    [Test]
    public async Task InternalAsyncMethod_MissingConfigureAwait_ReportsDiagnostic()
    {
        var source = """
            using System.Threading.Tasks;

            class MyService
            {
                internal async Task DoWorkAsync()
                {
                    {|#0:await Task.Delay(1)|}; // missing ConfigureAwait(false)
                }
            }
            """;

        await VerifyDiagnosticAsync(source,
            new DiagnosticResult(ConfigureAwaitAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("DoWorkAsync"));
    }

    [Test]
    public async Task InternalAsyncMethod_WithConfigureAwaitFalse_NoDiagnostic()
    {
        var source = """
            using System.Threading.Tasks;

            class MyService
            {
                internal async Task DoWorkAsync()
                {
                    await Task.Delay(1).ConfigureAwait(false);
                }
            }
            """;

        await VerifyNoDiagnosticsAsync(source);
    }

    [Test]
    public async Task PublicAsyncMethod_WithAgentJobAttribute_NoDiagnostic()
    {
        var source = """
            using System;
            using System.Threading.Tasks;

            [AttributeUsage(AttributeTargets.Method)]
            public sealed class AgentJobAttribute : Attribute { }

            class MyWorkflow
            {
                [AgentJob]
                public async Task RunAsync()
                {
                    await Task.Delay(1); // exempt — entry-point attribute
                }
            }
            """;

        await VerifyNoDiagnosticsAsync(source);
    }

    [Test]
    public async Task PublicAsyncMethod_NoAttribute_NoDiagnostic()
    {
        // Public methods without the exempt attribute are NOT subject to the rule —
        // the rule targets internal/private helpers only.
        var source = """
            using System.Threading.Tasks;

            class MyController
            {
                public async Task<string> HandleAsync()
                {
                    await Task.Delay(1);
                    return "ok";
                }
            }
            """;

        await VerifyNoDiagnosticsAsync(source);
    }

    [Test]
    public async Task PrivateAsyncMethod_MissingConfigureAwait_ReportsDiagnostic()
    {
        var source = """
            using System.Threading.Tasks;

            class MyService
            {
                private async Task<int> ComputeAsync()
                {
                    {|#0:await Task.Delay(1)|};
                    return 42;
                }
            }
            """;

        await VerifyDiagnosticAsync(source,
            new DiagnosticResult(ConfigureAwaitAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("ComputeAsync"));
    }

    private static async Task VerifyNoDiagnosticsAsync(string source)
    {
        var test = new CSharpAnalyzerTest<ConfigureAwaitAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Net10Reference
        };
        await test.RunAsync();
    }

    private static async Task VerifyDiagnosticAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<ConfigureAwaitAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Net10Reference
        };
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    private static readonly ReferenceAssemblies Net10Reference =
        new("net10.0",
            new PackageIdentity("Microsoft.NETCore.App.Ref", "10.0.0"),
            System.IO.Path.Combine("ref", "net10.0"));
}
