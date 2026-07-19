using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace Ananke.Analyzers;

/// <summary>
/// Flags direct reads of the ambient system clock — <c>DateTime.Now</c>/<c>UtcNow</c> and
/// <c>DateTimeOffset.Now</c>/<c>UtcNow</c> — anywhere in production code.
/// <para>
/// An ambient clock read makes the calling code's behavior depend on wall-clock time: it can't
/// be tested deterministically without either sleeping (slow, flaky) or restructuring the test
/// around whatever real time happens to be when it runs. Inject a <c>TimeProvider</c>
/// (defaulting to <c>TimeProvider.System</c>) and call <c>GetUtcNow()</c> instead — callers can
/// then substitute a <c>FakeTimeProvider</c> in tests and advance it explicitly.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AmbientClockAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ANANKE_TIME_001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Ambient clock read",
        messageFormat: "'{0}' reads the ambient system clock directly. Inject a TimeProvider and call GetUtcNow() instead so callers can control time in tests.",
        category: "Ananke.Time",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "DateTime.Now/UtcNow and DateTimeOffset.Now/UtcNow read the system clock " +
                     "directly, making the calling code's behavior depend on wall-clock time and " +
                     "impossible to test deterministically without sleeping. Inject a TimeProvider " +
                     "(defaulting to TimeProvider.System) and call GetUtcNow() instead.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;

        var memberName = memberAccess.Name.Identifier.Text;
        if (memberName is not ("Now" or "UtcNow"))
            return;

        if (context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol
            is not IPropertySymbol { IsStatic: true } property)
            return;

        var typeName = property.ContainingType?.ToDisplayString() switch
        {
            "System.DateTime" => "DateTime",
            "System.DateTimeOffset" => "DateTimeOffset",
            _ => null
        };
        if (typeName is null)
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, memberAccess.GetLocation(), $"{typeName}.{memberName}"));
    }
}
