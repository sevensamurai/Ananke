using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Ananke.Analyzers.Tests;

[TestFixture]
public class UndefinedJobNameAnalyzerTests
{
    [Test]
    public async Task ValidWorkflow_NoDiagnostics()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Ananke.Orchestration.Workflows;

            class Program
            {
                void Build()
                {
                    var w = new Workflow<string>("test")
                        .Job("a", (s, ct) => Task.FromResult(s))
                        .Job("b", (s, ct) => Task.FromResult(s))
                        .Then("a", "b")
                        .Then("b", Workflow.End)
                        .Build();
                }
            }
            """;

        await VerifyNoDiagnosticsAsync(source);
    }

    [Test]
    public async Task UndefinedJobInThen_ReportsDiagnostic()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Ananke.Orchestration.Workflows;

            class Program
            {
                void Build()
                {
                    var w = new Workflow<string>("test")
                        .Job("a", (s, ct) => Task.FromResult(s))
                        .Then("a", {|#0:"typo"|})
                        .Build();
                }
            }
            """;

        await VerifyDiagnosticAsync(source,
            new DiagnosticResult(UndefinedJobNameAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("typo"));
    }

    [Test]
    public async Task UndefinedJobInThenFrom_ReportsDiagnostic()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Ananke.Orchestration.Workflows;

            class Program
            {
                void Build()
                {
                    var w = new Workflow<string>("test")
                        .Job("a", (s, ct) => Task.FromResult(s))
                        .Job("b", (s, ct) => Task.FromResult(s))
                        .Then({|#0:"typo"|}, "b")
                        .Then("b", "__end__")
                        .Build();
                }
            }
            """;

        await VerifyDiagnosticAsync(source,
            new DiagnosticResult(UndefinedJobNameAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("typo"));
    }

    [Test]
    public async Task EndMarkerInThen_NoDiagnostics()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Ananke.Orchestration.Workflows;

            class Program
            {
                void Build()
                {
                    var w = new Workflow<string>("test")
                        .Job("a", (s, ct) => Task.FromResult(s))
                        .Then("a", "__end__")
                        .Build();
                }
            }
            """;

        await VerifyNoDiagnosticsAsync(source);
    }

    [Test]
    public async Task ValidChain_NoDiagnostics()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Ananke.Orchestration.Workflows;

            class Program
            {
                void Build()
                {
                    var w = new Workflow<string>("test")
                        .Job("a", (s, ct) => Task.FromResult(s))
                        .Job("b", (s, ct) => Task.FromResult(s))
                        .Chain("a", "b", "__end__")
                        .Build();
                }
            }
            """;

        await VerifyNoDiagnosticsAsync(source);
    }

    [Test]
    public async Task UndefinedJobInChain_ReportsDiagnostic()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Ananke.Orchestration.Workflows;

            class Program
            {
                void Build()
                {
                    var w = new Workflow<string>("test")
                        .Job("a", (s, ct) => Task.FromResult(s))
                        .Chain("a", {|#0:"missing"|}, "__end__")
                        .Build();
                }
            }
            """;

        await VerifyDiagnosticAsync(source,
            new DiagnosticResult(UndefinedJobNameAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("missing"));
    }

    [Test]
    public async Task UndefinedJobInOnEnter_ReportsDiagnostic()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Ananke.Orchestration.Workflows;

            class Program
            {
                void Build()
                {
                    var w = new Workflow<string>("test")
                        .Job("a", (s, ct) => Task.FromResult(s))
                        .OnEnter({|#0:"missing"|}, s => Task.CompletedTask)
                        .Then("a", "__end__")
                        .Build();
                }
            }
            """;

        await VerifyDiagnosticAsync(source,
            new DiagnosticResult(UndefinedJobNameAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("missing"));
    }

    [Test]
    public async Task ValidOnEnter_NoDiagnostics()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Ananke.Orchestration.Workflows;

            class Program
            {
                void Build()
                {
                    var w = new Workflow<string>("test")
                        .Job("a", (s, ct) => Task.FromResult(s))
                        .OnEnter("a", s => Task.CompletedTask)
                        .Then("a", "__end__")
                        .Build();
                }
            }
            """;

        await VerifyNoDiagnosticsAsync(source);
    }

    [Test]
    public async Task UndefinedJobInOnFault_ReportsDiagnostic()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Ananke.Orchestration.Workflows;

            class Program
            {
                void Build()
                {
                    var w = new Workflow<string>("test")
                        .Job("a", (s, ct) => Task.FromResult(s))
                        .OnFault({|#0:"missing"|}, (s, ex) => Task.CompletedTask)
                        .Then("a", "__end__")
                        .Build();
                }
            }
            """;

        await VerifyDiagnosticAsync(source,
            new DiagnosticResult(UndefinedJobNameAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("missing"));
    }

    [Test]
    public async Task ValidOnFault_NoDiagnostics()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Ananke.Orchestration.Workflows;

            class Program
            {
                void Build()
                {
                    var w = new Workflow<string>("test")
                        .Job("a", (s, ct) => Task.FromResult(s))
                        .OnFault("a", (s, ex) => Task.CompletedTask)
                        .Then("a", "__end__")
                        .Build();
                }
            }
            """;

        await VerifyNoDiagnosticsAsync(source);
    }

    [Test]
    public async Task NonFluentChain_NoDiagnostics()
    {
        // The analyzer only checks fluent chains — standalone .Then() calls
        // on separate statements are not guaranteed to match the same workflow.
        // In that case, no .Job() registrations are found, so no diagnostic.
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Ananke.Orchestration.Workflows;

            class Program
            {
                void Build()
                {
                    var w = new Workflow<string>("test");
                    w.Job("a", (string s, CancellationToken ct) => Task.FromResult(s));
                    w.Then("a", "b");
                }
            }
            """;

        await VerifyNoDiagnosticsAsync(source);
    }

    private static async Task VerifyNoDiagnosticsAsync(string source)
    {
        var test = new CSharpAnalyzerTest<UndefinedJobNameAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Net10Reference
        };
        test.TestState.AdditionalReferences.Add(
            typeof(Ananke.Orchestration.Workflows.Workflow).Assembly);
        test.TestState.AdditionalReferences.Add(
            typeof(Ananke.Abstractions.IBaseContext).Assembly);
        await test.RunAsync();
    }

    private static async Task VerifyDiagnosticAsync(
        string source,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<UndefinedJobNameAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Net10Reference
        };
        test.TestState.AdditionalReferences.Add(
            typeof(Ananke.Orchestration.Workflows.Workflow).Assembly);
        test.TestState.AdditionalReferences.Add(
            typeof(Ananke.Abstractions.IBaseContext).Assembly);
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    private static readonly ReferenceAssemblies Net10Reference =
        new("net10.0",
            new PackageIdentity("Microsoft.NETCore.App.Ref", "10.0.0"),
            System.IO.Path.Combine("ref", "net10.0"));
}
