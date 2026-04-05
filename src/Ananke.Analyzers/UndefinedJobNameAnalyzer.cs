using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace Ananke.Analyzers;

/// <summary>
/// Reports job names used in <c>Then</c>, <c>Loop</c>, <c>Join</c>, <c>Chain</c>,
/// <c>OnEnter</c>, <c>OnExit</c>, <c>OnFault</c>, <c>Timeout</c>, <c>InterruptBefore</c>, and
/// <c>InterruptAfter</c> that do not match any <c>.Job(...)</c> call on the same
/// fluent chain, and are not <c>Workflow.End</c> (<c>"__end__"</c>).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UndefinedJobNameAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ANANKE001";

    private static readonly LocalizableString Title =
        "Undefined job name in workflow";

    private static readonly LocalizableString MessageFormat =
        "Job name '{0}' is referenced but never registered via .Job() in this workflow";

    private static readonly LocalizableString Description =
        "All job names used in Then, Loop, Join, Chain, OnEnter, OnExit, OnFault, Timeout, " +
        "InterruptBefore, and InterruptAfter must match a .Job() registration on the same " +
        "fluent chain. Typos in job names produce runtime errors at Build() time.";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        category: "Ananke.Orchestration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [Rule];

    // Methods on Workflow<T> that register a job name (first string argument).
    private static readonly ImmutableHashSet<string> JobRegistrationMethods =
        ["Job", "SubFlow"];

    // Methods whose string arguments reference job names.
    private static readonly ImmutableHashSet<string> JobReferenceMethods =
        ["Then", "Loop", "Join", "Chain", "OnEnter", "OnExit", "OnFault", "Timeout", "InterruptBefore", "InterruptAfter"];

    // The end sentinel — not a real job.
    private const string EndMarker = "__end__";

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // We only care about .Then(), .Loop(), etc. — methods that reference job names.
        var methodName = GetMethodName(invocation);
        if (methodName is null || !JobReferenceMethods.Contains(methodName))
            return;

        // Walk the fluent chain upward to collect all .Job() registrations.
        var definedJobs = CollectDefinedJobNames(invocation, context.SemanticModel);
        if (definedJobs.Count == 0)
            return; // Not a recognizable Workflow<T> chain — skip.

        // Extract job name string literals from the current invocation's arguments.
        var referencedNames = ExtractJobNameArguments(invocation, methodName, context.SemanticModel);

        foreach (var (nameValue, location) in referencedNames)
        {
            if (nameValue == EndMarker)
                continue;

            if (!definedJobs.Contains(nameValue))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, location, nameValue));
            }
        }
    }

    /// <summary>
    /// Walks the fluent invocation chain and collects all first-argument string
    /// literals from <c>.Job()</c> and <c>.SubFlow()</c> calls.
    /// </summary>
    private static HashSet<string> CollectDefinedJobNames(
        InvocationExpressionSyntax startNode,
        SemanticModel semanticModel)
    {
        var names = new HashSet<string>();
        var current = startNode;

        while (current is not null)
        {
            var name = GetMethodName(current);

            if (name is not null && JobRegistrationMethods.Contains(name))
            {
                var firstArg = GetFirstStringLiteral(current);
                if (firstArg is not null)
                    names.Add(firstArg);
            }

            // Walk up: the receiver of a fluent call is the previous invocation.
            current = GetReceiverInvocation(current);
        }

        // Also walk forward from the root — the chain may define jobs after the
        // reference site. We walk from the top-level expression statement.
        var rootInvocation = FindFluentChainRoot(startNode);
        if (rootInvocation is not null && rootInvocation != startNode)
        {
            CollectJobNamesForward(rootInvocation, names);
        }

        return names;
    }

    /// <summary>
    /// Recursively descends the fluent chain from the root, collecting .Job()/.SubFlow() names.
    /// </summary>
    private static void CollectJobNamesForward(
        InvocationExpressionSyntax root,
        HashSet<string> names)
    {
        var queue = new Queue<SyntaxNode>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();

            if (node is InvocationExpressionSyntax inv)
            {
                var name = GetMethodName(inv);
                if (name is not null && JobRegistrationMethods.Contains(name))
                {
                    var firstArg = GetFirstStringLiteral(inv);
                    if (firstArg is not null)
                        names.Add(firstArg);
                }
            }

            foreach (var child in node.ChildNodes())
                queue.Enqueue(child);
        }
    }

    /// <summary>
    /// Finds the outermost invocation in a fluent chain.
    /// </summary>
    private static InvocationExpressionSyntax? FindFluentChainRoot(InvocationExpressionSyntax node)
    {
        var current = node;
        while (current.Parent is MemberAccessExpressionSyntax memberAccess &&
               memberAccess.Parent is InvocationExpressionSyntax parentInvocation)
        {
            current = parentInvocation;
        }
        return current;
    }

    /// <summary>
    /// Extracts job name references from the arguments of a reference-site method.
    /// Returns (value, location) pairs for each string literal argument that
    /// represents a job name.
    /// </summary>
    private static List<(string Value, Location Location)> ExtractJobNameArguments(
        InvocationExpressionSyntax invocation,
        string methodName,
        SemanticModel semanticModel)
    {
        var results = new List<(string, Location)>();
        var args = invocation.ArgumentList.Arguments;

        switch (methodName)
        {
            case "Then":
                // Then(string from, string to) — both args are job names
                // Then(string from, IRouter) — only first arg
                // Then(string from, ForkTarget) — only first arg
                if (args.Count >= 1)
                    AddIfStringLiteral(args[0], results);
                if (args.Count >= 2)
                    AddIfStringLiteral(args[1], results);
                break;

            case "Chain":
                // Chain(params string[]) — all args are job names
                foreach (var arg in args)
                    AddIfStringLiteral(arg, results);
                break;

            case "Loop":
                // Loop(string from, string loopTarget, string exitTarget, ...)
                if (args.Count >= 1)
                    AddIfStringLiteral(args[0], results);
                if (args.Count >= 2)
                    AddIfStringLiteral(args[1], results);
                if (args.Count >= 3)
                    AddIfStringLiteral(args[2], results);
                break;

            case "Join":
                // Join(string[] sources, string target, ...) — all string args are job names
                foreach (var arg in args)
                    AddIfStringLiteral(arg, results);
                break;

            case "OnEnter":
            case "OnExit":
            case "OnFault":
            case "Timeout":
            case "InterruptBefore":
            case "InterruptAfter":
                // First arg is the job name
                if (args.Count >= 1)
                    AddIfStringLiteral(args[0], results);
                break;
        }

        return results;
    }

    private static void AddIfStringLiteral(
        ArgumentSyntax argument,
        List<(string Value, Location Location)> results)
    {
        if (argument.Expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            var value = literal.Token.ValueText;
            results.Add((value, literal.GetLocation()));
        }
    }

    private static string? GetMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            _ => null
        };
    }

    private static string? GetFirstStringLiteral(InvocationExpressionSyntax invocation)
    {
        var args = invocation.ArgumentList.Arguments;
        if (args.Count == 0)
            return null;

        if (args[0].Expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return literal.Token.ValueText;
        }

        return null;
    }

    /// <summary>
    /// If the invocation is a fluent call (e.g. <c>expr.Method(...)</c>),
    /// returns the receiver expression if it is itself an invocation.
    /// </summary>
    private static InvocationExpressionSyntax? GetReceiverInvocation(
        InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Expression is InvocationExpressionSyntax receiver)
        {
            return receiver;
        }
        return null;
    }
}
