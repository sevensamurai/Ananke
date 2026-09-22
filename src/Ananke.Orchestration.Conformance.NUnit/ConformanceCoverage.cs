using System.Reflection;
using Ananke.Orchestration.Conformance;

namespace Ananke.Orchestration.Conformance.NUnit;

/// <summary>
/// Fails when an adapter alongside the fixtures is not subject to the conformance suite.
/// </summary>
/// <remarks>
/// <para>
///D4. Without this, D4 decays back to the situation this exists to prevent — a contract, a
/// suite, and nothing shipped subject to it — the moment someone adds an adapter in a hurry. The
/// failure this guards against is not disagreement; it is "nobody got around to it".
/// </para>
/// <para>
/// <b>Coverage is proved by construction, not by naming.</b> Each fixture is instantiated and its
/// factory invoked, and the runtime type of what comes back is what counts as covered. A fixture
/// named after an adapter but wired to something else does not satisfy the gate.
/// </para>
/// </remarks>
public static class ConformanceCoverage
{
    /// <summary>
    /// Asserts every adapter type shipped beside <paramref name="fixtureAssembly"/> has a
    /// conformance fixture in it.
    /// </summary>
    /// <param name="fixtureAssembly">The test assembly holding the conformance subclasses.</param>
    /// <remarks>
    /// Adapter assemblies are discovered from <paramref name="fixtureAssembly"/>'s own output
    /// directory rather than from a list, so an adapter added to this test project is audited
    /// without anyone remembering to register it — which is the whole point.
    /// </remarks>
    public static void AssertEveryAdapterHasAFixture(Assembly fixtureAssembly)
    {
        ArgumentNullException.ThrowIfNull(fixtureAssembly);

        var implementations = AdapterAssembliesBeside(fixtureAssembly)
            .SelectMany(ConformanceContracts.ImplementationsIn)
            .ToList();

        implementations.ShouldNotBeEmptyReport(fixtureAssembly);

        var covered = CoveredSubjectTypes(fixtureAssembly);
        var uncovered = implementations.Where(type => !covered.Contains(type)).ToList();

        if (uncovered.Count == 0)
            return;

        throw new AssertionException(
            $"{uncovered.Count} adapter type(s) in {fixtureAssembly.GetName().Name} have no conformance "
            + "fixture. Subclass the matching fixture from Ananke.Orchestration.Conformance.NUnit and "
            + "wire it to a stub transport:"
            + string.Concat(uncovered.Select(t => $"{Environment.NewLine}  - {t.FullName}")));
    }

    /// <summary>
    /// Asserts <paramref name="assembly"/> declares no type subject to conformance — for a package
    /// that deliberately ships no adapter of its own, so that adding one fails here rather than
    /// silently escaping the suite.
    /// </summary>
    /// <param name="assembly">The assembly expected to hold no contract implementation.</param>
    /// <param name="because">Why it ships none.</param>
    public static void AssertShipsNoAdapter(Assembly assembly, string because)
    {
        var implementations = ConformanceContracts.ImplementationsIn(assembly);

        if (implementations.Count == 0)
            return;

        throw new AssertionException(
            $"{assembly.GetName().Name} now declares {implementations.Count} type(s) subject to "
            + $"conformance, but was expected to ship none — {because}. Either that is no longer true, "
            + "or these need conformance fixtures:"
            + string.Concat(implementations.Select(t => $"{Environment.NewLine}  - {t.FullName}")));
    }

    // ── discovery ────────────────────────────────────────────────────────────

    /// <summary>
    /// Provider adapter assemblies sitting beside the fixtures. <c>Ananke.Orchestration</c> itself is
    /// excluded deliberately: it holds decorators and composition — caching, middleware, routing —
    /// which wrap the contracts rather than implementing them for a provider, and are not what the
    /// suite is about. So are the conformance packages' own reference subjects.
    /// </summary>
    private static IEnumerable<Assembly> AdapterAssembliesBeside(Assembly fixtureAssembly)
    {
        var directory = Path.GetDirectoryName(fixtureAssembly.Location);
        if (string.IsNullOrEmpty(directory))
            yield break;

        foreach (var path in Directory.EnumerateFiles(directory, "*.dll").OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(path);

            if (!name.StartsWith("Ananke.Orchestration.", StringComparison.Ordinal)
                || name.Contains(".Conformance", StringComparison.Ordinal)
                || name.EndsWith(".Tests", StringComparison.Ordinal))
            {
                continue;
            }

            yield return Assembly.LoadFrom(path);
        }
    }

    /// <summary>
    /// The runtime types every fixture in the assembly actually puts under test, found by
    /// instantiating each fixture and invoking its subject factory.
    /// </summary>
    private static HashSet<Type> CoveredSubjectTypes(Assembly fixtureAssembly)
    {
        var covered = new HashSet<Type>();

        foreach (var fixture in fixtureAssembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false })
                     .Where(IsConformanceFixture))
        {
            if (SubjectFactory(fixture) is not { } factory)
                continue;

            var instance = Activator.CreateInstance(fixture);
            if (factory.Invoke(instance, null) is { } subject)
                covered.Add(subject.GetType());
        }

        return covered;
    }

    private static bool IsConformanceFixture(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Assembly == typeof(ConformanceCoverage).Assembly)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The fixture's <c>CreateModel</c> / <c>CreateTranslator</c> / <c>CreateMapper</c> hook, found by
    /// its shape rather than its name so a new contract needs no change here.
    /// </summary>
    private static MethodInfo? SubjectFactory(Type fixture) =>
        fixture.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.GetParameters().Length == 0
                                 && ConformanceContracts.All.Contains(m.ReturnType));

    private static void ShouldNotBeEmptyReport(this List<Type> implementations, Assembly fixtureAssembly)
    {
        if (implementations.Count == 0)
        {
            throw new AssertionException(
                $"No adapter assemblies were found beside {fixtureAssembly.GetName().Name}. The gate "
                + "would pass vacuously, which is worse than not having it.");
        }
    }
}
