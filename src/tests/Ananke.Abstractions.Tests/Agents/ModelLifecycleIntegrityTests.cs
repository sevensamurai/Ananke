using System.Reflection;
using System.Text.RegularExpressions;
using Ananke.Abstractions.Agents;
using Shouldly;

namespace Ananke.Abstractions.Tests.Agents;

/// <summary>
/// Keeps <c>model-lifecycle.json</c> honest against itself and against the
/// <see cref="ObsoleteAttribute"/> messages on the <see cref="Models"/> constants.
/// </summary>
/// <remarks>
/// These are the two ways a model id can point somewhere useless, and neither is caught by the
/// compiler or by the existing catalog-conformance tests: an <c>[Obsolete]</c> message naming a
/// replacement the lifecycle data disagrees with, and a <c>ReplacedBy</c> pointing at a model that
/// is itself Legacy or Deprecated. Both were live in the tree until 2026-08-10.
/// </remarks>
[TestFixture]
public class ModelLifecycleIntegrityTests
{
    private static readonly Type[] ProviderClasses =
        [typeof(Models.OpenAI), typeof(Models.Anthropic), typeof(Models.Google)];

    /// <summary>Pulls the constant name out of "… is deprecated; use Models.Google.Gemini36Flash."</summary>
    private static readonly Regex SuggestedConstant = new(
        @"use\s+Models\.(?<provider>\w+)\.(?<constant>\w+)\s*\.", RegexOptions.Compiled);

    private static IEnumerable<TestCaseData> NonCurrentEntries() =>
        ModelLifecycleData.Entries.Values
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .Select(e => new TestCaseData(e).SetName($"{e.Id} ({e.Status})"));

    private static IEnumerable<TestCaseData> ObsoleteConstants()
    {
        foreach (var providerType in ProviderClasses)
        {
            foreach (var field in providerType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!field.IsLiteral || field.FieldType != typeof(string))
                    continue;

                var obsolete = field.GetCustomAttribute<ObsoleteAttribute>();
                if (obsolete?.Message is null)
                    continue;

                yield return new TestCaseData(providerType, field.Name, (string)field.GetValue(null)!, obsolete.Message)
                    .SetName($"{providerType.Name}.{field.Name}");
            }
        }
    }

    [TestCaseSource(nameof(NonCurrentEntries))]
    public void ReplacedBy_PointsAtACurrentModel_NeverAnIntermediateLegacyOne(ModelLifecycleEntry entry)
    {
        if (entry.ReplacedBy is null)
            Assert.Pass($"'{entry.Id}' records no replacement, which is allowed.");

        // Entries holds only non-Current models, so a hit means the replacement is itself stale.
        if (ModelLifecycleData.Entries.TryGetValue(entry.ReplacedBy!, out var replacement))
        {
            Assert.Fail(
                $"'{entry.Id}' ({entry.Status}) is replaced by '{entry.ReplacedBy}', which is itself " +
                $"{replacement.Status}. ModelLifecycleData documents ReplacedBy as always pointing at a " +
                $"Current model, never an intermediate one — follow the chain to " +
                $"'{replacement.ReplacedBy ?? "a Current model"}'.");
        }
    }

    [TestCaseSource(nameof(ObsoleteConstants))]
    public void ObsoleteMessage_NamesTheSameReplacementAsTheLifecycleData(
        Type providerType, string constantName, string modelId, string message)
    {
        ModelLifecycleData.Entries.ShouldContainKey(modelId,
            $"Models.{providerType.Name}.{constantName} is marked [Obsolete] but '{modelId}' has no " +
            "entry in model-lifecycle.json");

        var expected = ModelLifecycleData.Entries[modelId].ReplacedBy;
        if (expected is null)
            Assert.Pass($"'{modelId}' records no replacement, so the message is free-form.");

        var match = SuggestedConstant.Match(message);
        match.Success.ShouldBeTrue(
            $"Models.{providerType.Name}.{constantName}'s [Obsolete] message should name a replacement as " +
            $"'use Models.<Provider>.<Constant>.' so it can be checked; got: \"{message}\"");

        var suggestedType = ProviderClasses.SingleOrDefault(t => t.Name == match.Groups["provider"].Value);
        suggestedType.ShouldNotBeNull(
            $"Models.{providerType.Name}.{constantName} suggests unknown provider " +
            $"'{match.Groups["provider"].Value}'");

        var suggestedField = suggestedType!
            .GetField(match.Groups["constant"].Value, BindingFlags.Public | BindingFlags.Static);
        suggestedField.ShouldNotBeNull(
            $"Models.{providerType.Name}.{constantName} suggests " +
            $"Models.{match.Groups["provider"].Value}.{match.Groups["constant"].Value}, which does not exist");

        var suggestedId = (string)suggestedField!.GetValue(null)!;
        suggestedId.ShouldBe(expected,
            $"Models.{providerType.Name}.{constantName}'s [Obsolete] message sends callers to " +
            $"'{suggestedId}', but model-lifecycle.json replaces '{modelId}' with '{expected}'. " +
            "The attribute and the data must agree.");
    }
}
