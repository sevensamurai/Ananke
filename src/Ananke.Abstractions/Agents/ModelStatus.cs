namespace Ananke.Abstractions.Agents;

/// <summary>
/// Lifecycle stage of a model identifier, shared by the design-time
/// <c>Ananke.Design.ModelCatalog</c> and the runtime
/// <c>Ananke.Orchestration.Agents.Routing.ModelCatalog</c> so both catalogs agree on which
/// models are current, aging, or gone.
/// </summary>
public enum ModelStatus
{
    /// <summary>The recommended model for its tier — no newer replacement exists yet.</summary>
    Current,

    /// <summary>
    /// Superseded by a <see cref="Current"/> model but still fully supported by the provider.
    /// Safe to use; new code should prefer the replacement.
    /// </summary>
    Legacy,

    /// <summary>
    /// The provider has announced removal or is actively steering traffic away from this model.
    /// Still callable today — treat as a warning, not a hard failure.
    /// </summary>
    Deprecated,

    /// <summary>The provider no longer serves this model. Calls will fail.</summary>
    Retired
}
