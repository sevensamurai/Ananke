using Ananke.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace Ananke.Analyzers.Tests;

[TestFixture]
public sealed class AmbientClockAnalyzerTests
{
    [Test]
    public async Task DateTimeOffsetUtcNow_ReportsDiagnostic()
    {
        var source = """
            using System;

            class MyService
            {
                public DateTimeOffset GetTimestamp() => {|#0:DateTimeOffset.UtcNow|};
            }
            """;

        await VerifyDiagnosticAsync(source,
            new DiagnosticResult(AmbientClockAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("DateTimeOffset.UtcNow"));
    }

    [Test]
    public async Task DateTimeUtcNow_ReportsDiagnostic()
    {
        var source = """
            using System;

            class MyService
            {
                public DateTime GetTimestamp() => {|#0:DateTime.UtcNow|};
            }
            """;

        await VerifyDiagnosticAsync(source,
            new DiagnosticResult(AmbientClockAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("DateTime.UtcNow"));
    }

    [Test]
    public async Task DateTimeOffsetNow_ReportsDiagnostic()
    {
        var source = """
            using System;

            class MyService
            {
                public DateTimeOffset GetTimestamp() => {|#0:DateTimeOffset.Now|};
            }
            """;

        await VerifyDiagnosticAsync(source,
            new DiagnosticResult(AmbientClockAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("DateTimeOffset.Now"));
    }

    [Test]
    public async Task DateTimeNow_ReportsDiagnostic()
    {
        var source = """
            using System;

            class MyService
            {
                public DateTime GetTimestamp() => {|#0:DateTime.Now|};
            }
            """;

        await VerifyDiagnosticAsync(source,
            new DiagnosticResult(AmbientClockAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("DateTime.Now"));
    }

    [Test]
    public async Task DefaultValueExpression_ReportsDiagnostic()
    {
        // Auto-property default-value initializers are just as much a syntax-tree member
        // access as a method body expression — confirms the analyzer catches both shapes.
        var source = """
            using System;

            class MyEvent
            {
                public DateTimeOffset OccurredAt { get; init; } = {|#0:DateTimeOffset.UtcNow|};
            }
            """;

        await VerifyDiagnosticAsync(source,
            new DiagnosticResult(AmbientClockAnalyzer.DiagnosticId, DiagnosticSeverity.Warning)
                .WithLocation(0)
                .WithArguments("DateTimeOffset.UtcNow"));
    }

    [Test]
    public async Task TimeProviderGetUtcNow_NoDiagnostic()
    {
        var source = """
            using System;

            class MyService
            {
                private readonly TimeProvider _timeProvider = TimeProvider.System;

                public DateTimeOffset GetTimestamp() => _timeProvider.GetUtcNow();
            }
            """;

        await VerifyNoDiagnosticsAsync(source);
    }

    [Test]
    public async Task UnrelatedNowProperty_NoDiagnostic()
    {
        // A custom type's own "Now" property is not System.DateTime/DateTimeOffset — must not
        // false-positive just because the member name matches.
        var source = """
            class MyClock
            {
                public static int Now => 42;
            }

            class MyService
            {
                public int GetTimestamp() => MyClock.Now;
            }
            """;

        await VerifyNoDiagnosticsAsync(source);
    }

    private static async Task VerifyNoDiagnosticsAsync(string source)
    {
        var test = new CSharpAnalyzerTest<AmbientClockAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Net10Reference
        };
        await test.RunAsync();
    }

    private static async Task VerifyDiagnosticAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<AmbientClockAnalyzer, DefaultVerifier>
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
