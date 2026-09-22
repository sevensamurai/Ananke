namespace Ananke.Orchestration.Agents.Routing;

/// <summary>
/// Roughly how large a model is, by the weights you must hold in memory to serve it.
/// </summary>
/// <remarks>
/// Answers "can I run this on a workstation?", so for a mixture-of-experts model this reflects the
/// <b>total</b> parameter count rather than the active one — you load all of it even though only a
/// fraction computes on any given token. <see cref="ModelClassification.ApproxParametersB"/> records
/// the active count instead, and the two deliberately disagree for MoE models.
/// </remarks>
public enum ModelSizeClass
{
    /// <summary>Not recorded. The default — an absence, not a claim that the model is small.</summary>
    Unknown,

    /// <summary>Under 2 B parameters. Runs on almost anything, including CPU-only.</summary>
    Micro,

    /// <summary>Roughly 2–8 B. The usual laptop and single-consumer-GPU tier.</summary>
    Small,

    /// <summary>Roughly 8–30 B. A workstation GPU, or CPU inference with patience.</summary>
    Medium,

    /// <summary>Over 30 B. Multi-GPU or server-class hardware.</summary>
    Large
}

/// <summary>Whether a model's weights can be obtained and served yourself.</summary>
public enum ModelWeights
{
    /// <summary>Not recorded.</summary>
    Unknown,

    /// <summary>Reachable only through a provider's API. There is nothing to self-host.</summary>
    ApiOnly,

    /// <summary>
    /// Weights are published and can be served on your own hardware, under whatever licence
    /// <see cref="ModelClassification.LicenseSpdx"/> records.
    /// </summary>
    OpenWeights
}

/// <summary>
/// Descriptive facts about a model — how big it is, whether you can host it, and under what licence
/// — as opposed to a claim about what it can do.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is deliberately not a capability.</b> <see cref="ModelCapability"/> is a bitmask consumed
/// by <see cref="ModelProfile.Satisfies"/>, so anything placed there competes with capability
/// matching and changes what routing selects. These facets never do: they answer the questions
/// people ask <i>before</i> capability matters — <i>can I run this on my own hardware?</i>,
/// <i>may we use it at all?</i>, <i>what family is it from?</i> — and are used for filtering and
/// policy, by <see cref="CapabilityModelRouter.WithPolicy"/> or by querying
/// <see cref="ModelCatalog.All"/> directly.
/// </para>
/// <para>
/// <b>They also age better than capability metadata.</b> A parameter count and a licence are fixed
/// when a model is released; an intelligence tier is a judgement that drifts as the field moves, and
/// for a locally served model the capabilities depend on quantization and the serving runtime rather
/// than on the weights alone. These facets do not move with either.
/// </para>
/// <para>
/// <b>Two honesty constraints apply to every value here.</b>
/// </para>
/// <para>
/// <i>First</i> — this describes the weights <b>as published</b>, and a hosted endpoint adds its own
/// terms on top. Running an open-weight model through a hosted provider is governed by that
/// provider's terms of service <i>and</i> the weights licence. Only the second is recorded here; the
/// first cannot be known from a catalogue.
/// </para>
/// <para>
/// <i>Second</i> — this is a filtering aid, not legal advice. Licences are recorded as the vendor
/// published them, and vendors do occasionally change them at a new release. A team with a hard
/// compliance boundary confirms the licence for the artefact it actually deploys: these facets
/// narrow the field, they do not clear it.
/// </para>
/// </remarks>
public sealed record ModelClassification
{
    /// <summary>How large the model is. See <see cref="ModelSizeClass"/> for the MoE convention.</summary>
    public ModelSizeClass SizeClass { get; init; } = ModelSizeClass.Unknown;

    /// <summary>
    /// Approximate parameter count in billions, or <see langword="null"/> when the vendor has not
    /// disclosed one — which is the norm for hosted frontier models.
    /// </summary>
    /// <remarks>
    /// For a mixture-of-experts model this is the <b>active</b> parameter count, which is what
    /// governs inference speed. The memory you need is governed by the total, and that is what
    /// <see cref="SizeClass"/> reports.
    /// </remarks>
    public double? ApproxParametersB { get; init; }

    /// <summary>Whether the weights are published for self-hosting.</summary>
    public ModelWeights Weights { get; init; } = ModelWeights.Unknown;

    /// <summary>
    /// SPDX identifier of the weights licence (e.g. <c>"apache-2.0"</c>, <c>"mit"</c>), or
    /// <see langword="null"/> when the licence is proprietary, bespoke, or simply not recorded.
    /// </summary>
    /// <remarks>
    /// Several widely used open-weight licences have no SPDX identifier at all — Meta's Llama
    /// Community Licence and Google's Gemma Terms of Use among them. Those are recorded as
    /// <see langword="null"/> with <see cref="LicenseIsOsiApproved"/> <see langword="false"/>, which
    /// is the honest reading: obtainable weights, on terms this field cannot express.
    /// </remarks>
    public string? LicenseSpdx { get; init; }

    /// <summary>
    /// Whether the weights licence is OSI-approved. Defaults to <see langword="false"/>, which is
    /// the safe direction: a policy filtering for OSI-approved licences excludes anything unrecorded
    /// rather than admitting it.
    /// </summary>
    /// <remarks>
    /// Worth knowing before relying on "open weights" as a synonym for "open source": two of the
    /// most widely deployed open-weight families — Llama and Gemma — are <b>not</b> OSI-approved,
    /// and carry use restrictions and, in Llama's case, a monthly-active-user threshold.
    /// </remarks>
    public bool LicenseIsOsiApproved { get; init; }

    /// <summary>Where the licence text lives, when there is a stable URL for it.</summary>
    public Uri? LicenseUri { get; init; }

    /// <summary>
    /// The model family this belongs to — <c>"llama"</c>, <c>"qwen"</c>, <c>"gpt"</c>. The cluster
    /// key, and the thing that makes the catalogue's grouping visible at runtime rather than only in
    /// the shape of the C# that declares it.
    /// </summary>
    public string? Family { get; init; }

    /// <summary>
    /// Nothing recorded. The default for a profile nobody has classified, and distinct from a
    /// deliberate statement that a model is proprietary — for that, see <see cref="ApiOnly"/>.
    /// </summary>
    public static ModelClassification Unspecified { get; } = new();

    /// <summary>
    /// A hosted, proprietary model of the given <paramref name="family"/>: no weights to obtain, no
    /// licence to record, and a parameter count the vendor has not published.
    /// </summary>
    /// <param name="family">Family key, e.g. <c>"gpt"</c>, <c>"claude"</c>, <c>"gemini"</c>.</param>
    public static ModelClassification ApiOnly(string family) => new()
    {
        Weights = ModelWeights.ApiOnly,
        SizeClass = ModelSizeClass.Unknown,
        Family = family
    };
}
