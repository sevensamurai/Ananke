using System.Reflection;
using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Providers;

namespace Ananke.Orchestration.Conformance;

/// <summary>
/// The interfaces this package holds a contract for, and how to find their implementations.
/// </summary>
/// <remarks>
/// Exists so a coverage gate can ask "which types here are subject to conformance?" without
/// hard-coding a list that rots. The NUnit package builds its gate on this; a consumer on another
/// runner can build the same one.
/// </remarks>
public static class ConformanceContracts
{
    /// <summary>Every contract with a scenario list in this package.</summary>
    public static IReadOnlyList<Type> All { get; } =
    [
        typeof(IStreamingAgentModel),
        typeof(IToolSchemaTranslator),
        typeof(IJsonSchemaTranslator)
    ];

    /// <summary>
    /// Every concrete, publicly constructible type in <paramref name="assembly"/> that implements one
    /// of <see cref="All"/> — that is, everything in it a conformance fixture should cover.
    /// </summary>
    /// <param name="assembly">The adapter assembly to inspect.</param>
    public static IReadOnlyList<Type> ImplementationsIn(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return
        [
            .. assembly.GetExportedTypes()
                .Where(type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
                .Where(type => All.Any(type.IsAssignableTo))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
        ];
    }
}
