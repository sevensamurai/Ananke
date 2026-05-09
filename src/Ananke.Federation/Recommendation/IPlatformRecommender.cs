using Ananke.Design;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Tools;

namespace Ananke.Federation.Recommendation;

/// <summary>
/// Evaluates how well a <see cref="WorkflowManifest"/> fits each candidate platform,
/// returning a ranked <see cref="PlatformFitReport"/>.
/// </summary>
/// <remarks>
/// <para>
/// The recommender is a pure function over the manifest, toolkit, and the embedded
/// <c>platform-capabilities.json</c> / <c>platform-profiles.json</c> resources.
/// It requires no credentials and no network access.
/// </para>
/// <para>
/// Scoring is the weighted sum of four axes — capability coverage, strength
/// alignment, cost &amp; latency fit, and governance fit — each in <c>[0, 1]</c>.
/// A <see cref="FitReasonKind.Block"/> reason zeroes the total regardless of
/// the other axes.
/// </para>
/// <para>
/// For a live-validation pass that contacts platform APIs, use
/// <see cref="EvaluateWithLiveValidationAsync"/> and supply the appropriate
/// <see cref="IPlatformValidator"/> implementations.
/// </para>
/// </remarks>
public interface IPlatformRecommender
{
    /// <summary>
    /// Scores <paramref name="manifest"/> against every candidate platform and returns
    /// a ranked report. Offline — no credentials or network access required.
    /// </summary>
    /// <param name="manifest">The workflow manifest to evaluate.</param>
    /// <param name="toolKit">The toolkit bound to the workflow.</param>
    /// <param name="candidatePlatforms">
    /// Restrict scoring to these platform identifiers. When <see langword="null"/> or empty
    /// the recommender evaluates all platforms present in <c>platform-capabilities.json</c>.
    /// </param>
    /// <param name="weights">Axis weights. When <see langword="null"/> the defaults are used.</param>
    PlatformFitReport Evaluate(
        WorkflowManifest manifest,
        ToolKit toolKit,
        IReadOnlyList<string>? candidatePlatforms = null,
        RecommendationWeights? weights = null);

    /// <summary>
    /// Performs an offline evaluation and then overlays live-validation results
    /// from each supplied <paramref name="validators"/>.  Any <c>Error</c>-level
    /// diagnostic from a live validator converts to a <see cref="FitReasonKind.Block"/>
    /// reason; warnings convert to <see cref="FitReasonKind.Minus"/>.
    /// </summary>
    /// <param name="manifest">The workflow manifest to evaluate.</param>
    /// <param name="toolKit">The toolkit bound to the workflow.</param>
    /// <param name="validators">
    /// One or more live <see cref="IPlatformValidator"/> instances to contact.
    /// Only validators whose <see cref="IPlatformValidator.Platform"/> is among the
    /// current candidate list are invoked.
    /// </param>
    /// <param name="candidatePlatforms">
    /// Restrict scoring to these platform identifiers. When <see langword="null"/> or empty
    /// the recommender evaluates all platforms present in <c>platform-capabilities.json</c>.
    /// </param>
    /// <param name="weights">Axis weights. When <see langword="null"/> the defaults are used.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PlatformFitReport> EvaluateWithLiveValidationAsync(
        WorkflowManifest manifest,
        ToolKit toolKit,
        IReadOnlyList<IPlatformValidator> validators,
        IReadOnlyList<string>? candidatePlatforms = null,
        RecommendationWeights? weights = null,
        CancellationToken ct = default);
}
