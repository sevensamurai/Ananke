using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Ananke.Analyzers;

/// <summary>
/// Flags string literals equal to a <c>Deprecated</c> or <c>Retired</c> model identifier, read
/// from the single-source-of-truth <c>model-lifecycle.json</c> (the same file
/// <c>Ananke.Design.ModelCatalog</c> and <c>Ananke.Orchestration.Agents.Routing.ModelCatalog</c>
/// read at runtime via <c>Ananke.Abstractions.Agents.ModelLifecycleData</c>) so all three
/// consumers agree on lifecycle without hand-syncing three separate tables.
/// <para>
/// A literal equal to a <c>Deprecated</c> id reports <see cref="DiagnosticId"/> as a warning
/// (still callable, just superseded). A literal equal to a <c>Retired</c> id reports
/// <see cref="RetiredDiagnosticId"/> as an error — the provider no longer serves it, so the call
/// will fail regardless of what Ananke does. Two IDs, not one conditionally-severed ID: Roslyn's
/// analyzer-release-tracking tooling (<c>RS2001</c>) treats a single ID appearing with two
/// different severities as an accidental severity change, not an intentional two-tier rule.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeprecatedModelLiteralAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ANNKE002";
    public const string RetiredDiagnosticId = "ANNKE003";

    private const string Category = "Ananke.Models";

    private static readonly DiagnosticDescriptor DeprecatedRule = new(
        DiagnosticId,
        title: "Deprecated model literal",
        messageFormat: "'{0}' is a deprecated model identifier; use '{1}' instead",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "This string literal matches a model identifier the provider has marked " +
                     "deprecated. It is still callable today, but superseded — replace it with " +
                     "the recommended constant.");

    private static readonly DiagnosticDescriptor RetiredRule = new(
        RetiredDiagnosticId,
        title: "Retired model literal",
        messageFormat: "'{0}' has been retired by the provider; use '{1}' instead",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "This string literal matches a model identifier the provider no longer " +
                     "serves. The call will fail regardless of what Ananke does — replace it " +
                     "with the recommended constant.");

    /// <summary>Key into <see cref="Diagnostic.Properties"/> holding the replacement model id, so
    /// <c>DeprecatedModelLiteralCodeFixProvider</c> doesn't need to re-parse the JSON itself.</summary>
    public const string ReplacementPropertyKey = "ReplacedBy";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [DeprecatedRule, RetiredRule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var lifecycleData = LoadLifecycleData(compilationContext.Options.AdditionalFiles);
            if (lifecycleData.IsEmpty)
                return;

            compilationContext.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeStringLiteral(nodeContext, lifecycleData),
                SyntaxKind.StringLiteralExpression);
        });
    }

    private static void AnalyzeStringLiteral(
        SyntaxNodeAnalysisContext context, ImmutableDictionary<string, LifecycleEntry> lifecycleData)
    {
        var literal = (LiteralExpressionSyntax)context.Node;
        var value = literal.Token.ValueText;

        if (!lifecycleData.TryGetValue(value, out var entry))
            return;

        var replacement = entry.ReplacedBy ?? "(no replacement recorded)";
        var descriptor = entry.Status == "Retired" ? RetiredRule : DeprecatedRule;
        var properties = entry.ReplacedBy is not null
            ? ImmutableDictionary<string, string?>.Empty.Add(ReplacementPropertyKey, entry.ReplacedBy)
            : ImmutableDictionary<string, string?>.Empty;

        context.ReportDiagnostic(
            Diagnostic.Create(descriptor, literal.GetLocation(), properties, entry.Id, replacement));
    }

    // Plain struct, not a record struct — netstandard2.0 lacks System.Runtime.CompilerServices
    // .IsExternalInit, which init-only record properties compile against.
    private readonly struct LifecycleEntry
    {
        public LifecycleEntry(string id, string status, string? replacedBy)
        {
            Id = id;
            Status = status;
            ReplacedBy = replacedBy;
        }

        public string Id { get; }
        public string Status { get; }
        public string? ReplacedBy { get; }
    }

    // Only Deprecated/Retired ids are ever reported (Legacy/Current are silently allowed), but
    // the parser reads every entry — filtering happens at report time so a status typo in the
    // JSON doesn't silently vanish the entry instead of surfacing as "never matches".
    private static readonly Regex ObjectPattern = new(@"\{[^{}]*\}", RegexOptions.Compiled);
    private static readonly Regex FieldPattern = new(
        "\"(?<name>id|status|replacedBy)\"\\s*:\\s*(?:\"(?<value>[^\"]*)\"|null)",
        RegexOptions.Compiled);

    private static ImmutableDictionary<string, LifecycleEntry> LoadLifecycleData(
        ImmutableArray<AdditionalText> additionalFiles)
    {
        var file = additionalFiles.FirstOrDefault(f =>
            f.Path.EndsWith("model-lifecycle.json", StringComparison.OrdinalIgnoreCase));
        if (file is null)
            return ImmutableDictionary<string, LifecycleEntry>.Empty;

        var text = file.GetText()?.ToString();
        if (string.IsNullOrEmpty(text))
            return ImmutableDictionary<string, LifecycleEntry>.Empty;

        var builder = ImmutableDictionary.CreateBuilder<string, LifecycleEntry>(StringComparer.Ordinal);

        foreach (Match objectMatch in ObjectPattern.Matches(text))
        {
            string? id = null;
            string? status = null;
            string? replacedBy = null;

            foreach (Match fieldMatch in FieldPattern.Matches(objectMatch.Value))
            {
                var fieldValue = fieldMatch.Groups["value"].Success ? fieldMatch.Groups["value"].Value : null;
                switch (fieldMatch.Groups["name"].Value)
                {
                    case "id": id = fieldValue; break;
                    case "status": status = fieldValue; break;
                    case "replacedBy": replacedBy = fieldValue; break;
                }
            }

            if (id is not null && status is not null && status is "Deprecated" or "Retired")
                builder[id] = new LifecycleEntry(id, status, replacedBy);
        }

        return builder.ToImmutable();
    }
}
