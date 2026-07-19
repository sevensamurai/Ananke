using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ananke.Analyzers;

/// <summary>
/// Offers to replace an <c>ANNKE002</c>/<c>ANNKE003</c>-flagged string literal with a reference
/// to the recommended replacement's <c>Ananke.Abstractions.Agents.Models</c>
/// constant, resolved by searching the current compilation for a public const string field whose
/// value equals the replacement id recorded on the diagnostic
/// (<see cref="DeprecatedModelLiteralAnalyzer.ReplacementPropertyKey"/>) — not a second hardcoded
/// id-to-constant-name table, which would just be a fourth place for the mapping to drift.
/// </summary>
/// <remarks>
/// <para>
/// Lives in a separate assembly (<c>Ananke.Analyzers.CodeFixes</c>) from
/// <see cref="DeprecatedModelLiteralAnalyzer"/> because Roslyn's <c>RS1038</c> forbids a
/// <see cref="Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer"/> assembly from referencing
/// <c>Microsoft.CodeAnalysis.Workspaces</c> (which this type needs for <c>CodeAction</c>/
/// <c>Document</c>) — that assembly isn't available during command-line/CI compilation.
/// </para>
/// <para>
/// Falls back to replacing with the bare replacement string literal (no constant reference) when
/// no matching constant is found in the compilation — still a correct fix, just not the
/// "recommended constant" form. This can happen if <c>Ananke.Abstractions</c> isn't referenced
/// by the project being fixed, or the id genuinely has no constant (shouldn't happen for data
/// sourced from <c>model-lifecycle.json</c>, but the analyzer doesn't assume it never will).
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DeprecatedModelLiteralCodeFixProvider))]
[Shared]
public sealed class DeprecatedModelLiteralCodeFixProvider : CodeFixProvider
{
    private const string ModelsMetadataName = "Ananke.Abstractions.Agents.Models";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        [DeprecatedModelLiteralAnalyzer.DiagnosticId, DeprecatedModelLiteralAnalyzer.RetiredDiagnosticId];

    // Explicitly disable FixAll (returning null, per RS1016's own suggestion): a bulk Fix-All
    // sweep replaces each flagged literal independently, with no awareness that two literals in
    // the same test (an input value and its expected-value assertion) must stay equal, or that a
    // "passthrough"/identity mapping table must not have its values diverge from its keys. A
    // solution-wide `dotnet format` run applying this fix broke 8 tests and silently changed two
    // untested production passthrough mappers into upgrade mappers before this was caught. The
    // fix is still safe to apply one at a time via the IDE code action, where a human judges
    // each site; it must never be swept in bulk.
    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        if (!diagnostic.Properties.TryGetValue(
                DeprecatedModelLiteralAnalyzer.ReplacementPropertyKey, out var replacementId)
            || replacementId is null)
        {
            return; // no recorded replacement — nothing to offer
        }

        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root?.FindNode(context.Span) is not LiteralExpressionSyntax literal)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Replace with recommended replacement ('{replacementId}')",
                createChangedDocument: ct => ReplaceLiteralAsync(context.Document, literal, replacementId, ct),
                equivalenceKey: nameof(DeprecatedModelLiteralCodeFixProvider)),
            diagnostic);
    }

    private static async Task<Document> ReplaceLiteralAsync(
        Document document, LiteralExpressionSyntax literal, string replacementId, CancellationToken ct)
    {
        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        var constantReference = semanticModel is not null
            ? FindConstantReference(semanticModel.Compilation, replacementId)
            : null;

        ExpressionSyntax newExpression = constantReference is not null
            ? SyntaxFactory.ParseExpression(constantReference)
            : SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(replacementId));

        newExpression = newExpression.WithTriviaFrom(literal);

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var newRoot = root!.ReplaceNode(literal, newExpression);
        return document.WithSyntaxRoot(newRoot);
    }

    /// <summary>
    /// Searches <c>Ananke.Abstractions.Agents.Models</c>'s nested provider classes for a public
    /// const string field whose value equals <paramref name="modelId"/>, returning a fully
    /// qualified reference (e.g. <c>global::Ananke.Abstractions.Agents.Models.OpenAI.Gpt55</c>).
    /// Returns <see langword="null"/> if the type or a matching field isn't found.
    /// </summary>
    private static string? FindConstantReference(Compilation compilation, string modelId)
    {
        var modelsType = compilation.GetTypeByMetadataName(ModelsMetadataName);
        if (modelsType is null)
            return null;

        foreach (var providerType in modelsType.GetTypeMembers())
        {
            foreach (var member in providerType.GetMembers())
            {
                if (member is IFieldSymbol { IsConst: true } field &&
                    field.Type.SpecialType == SpecialType.System_String &&
                    field.ConstantValue is string value &&
                    value == modelId)
                {
                    return $"global::{ModelsMetadataName}.{providerType.Name}.{field.Name}";
                }
            }
        }

        return null;
    }
}
