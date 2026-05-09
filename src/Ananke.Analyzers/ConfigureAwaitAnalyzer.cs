using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace Ananke.Analyzers;

/// <summary>
/// Enforces <c>ConfigureAwait(false)</c> on every <c>await</c> expression
/// inside internal or private async methods.
/// <para>
/// Public async methods decorated with <c>[AgentJob]</c> or
/// <c>[WorkflowEntry]</c> are exempted — they run inside the Ananke
/// orchestration context where the synchronization context is intentionally
/// preserved.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConfigureAwaitAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ANANKE_ASYNC_001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Missing ConfigureAwait(false)",
        messageFormat: "Async method '{0}' has an await expression without ConfigureAwait(false). Internal helpers should use ConfigureAwait(false) to avoid context captures.",
        category: "Ananke.Async",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Internal and private async methods must call ConfigureAwait(false) on every await expression. " +
                     "Public methods marked with [AgentJob] or [WorkflowEntry] are exempted.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;

        if (!IsAsyncMethod(method))
            return;

        if (IsExemptEntryPoint(method, context.SemanticModel))
            return;

        if (!IsInternalOrPrivate(method, context.SemanticModel))
            return;

        var methodName = method.Identifier.Text;
        var awaitExpressions = method.DescendantNodes().OfType<AwaitExpressionSyntax>();

        foreach (var awaitExpr in awaitExpressions)
        {
            if (!HasConfigureAwaitFalse(awaitExpr))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, awaitExpr.GetLocation(), methodName));
            }
        }
    }

    private static bool IsAsyncMethod(MethodDeclarationSyntax method) =>
        method.Modifiers.Any(SyntaxKind.AsyncKeyword);

    /// <summary>
    /// Returns <see langword="true"/> for public methods decorated with
    /// <c>[AgentJob]</c> or <c>[WorkflowEntry]</c> — these run inside the
    /// Ananke orchestration context and are exempt from the rule.
    /// </summary>
    private static bool IsExemptEntryPoint(MethodDeclarationSyntax method, SemanticModel semanticModel)
    {
        if (!method.Modifiers.Any(SyntaxKind.PublicKeyword))
            return false;

        foreach (var attributeList in method.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var name = attribute.Name.ToString();
                if (name is "AgentJob" or "WorkflowEntry" or
                    "AgentJobAttribute" or "WorkflowEntryAttribute")
                    return true;

                // Check via semantic model for fully-qualified names
                if (semanticModel.GetSymbolInfo(attribute).Symbol is IMethodSymbol attributeSymbol)
                {
                    var containingType = attributeSymbol.ContainingType.Name;
                    if (containingType is "AgentJobAttribute" or "WorkflowEntryAttribute")
                        return true;
                }
            }
        }

        return false;
    }

    private static bool IsInternalOrPrivate(MethodDeclarationSyntax method, SemanticModel semanticModel)
    {
        // Explicit private or internal modifier
        if (method.Modifiers.Any(SyntaxKind.PrivateKeyword) ||
            method.Modifiers.Any(SyntaxKind.InternalKeyword))
            return true;

        // No accessibility modifier at all: default is private for class members
        var hasAccessModifier = method.Modifiers.Any(m =>
            m.IsKind(SyntaxKind.PublicKeyword) ||
            m.IsKind(SyntaxKind.ProtectedKeyword) ||
            m.IsKind(SyntaxKind.InternalKeyword) ||
            m.IsKind(SyntaxKind.PrivateKeyword));

        return !hasAccessModifier;
    }

    private static bool HasConfigureAwaitFalse(AwaitExpressionSyntax awaitExpr)
    {
        // Pattern: await expr.ConfigureAwait(false)
        // The awaited expression should be an invocation of ConfigureAwait with a false literal.
        if (awaitExpr.Expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.Text == "ConfigureAwait" &&
            invocation.ArgumentList.Arguments.Count == 1 &&
            invocation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.FalseLiteralExpression))
        {
            return true;
        }

        return false;
    }
}
