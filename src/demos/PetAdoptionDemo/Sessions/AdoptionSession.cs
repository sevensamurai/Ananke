using Ananke.AspNetCore.Sessions;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Knowledge;
using Ananke.StateMachine;

/// <summary>
/// Adoption-specific session — adds the shelter <see cref="KnowledgeBase"/>
/// on top of the framework-provided <see cref="ChatSession{TState, TAction}"/>.
/// </summary>
/// <summary>
/// Phase-crossing context for an adoption in progress.
/// Retrieved via <see cref="ChatSession{TState, TAction}.GetContext{T}"/>.
/// </summary>
internal sealed class AdoptionContext
{
    /// <summary>Name of the pet being adopted.</summary>
    public string? PetName { get; set; }

    /// <summary>Adoption fee in dollars (extracted from the pet listing).</summary>
    public decimal? AdoptionFee { get; set; }
}

internal sealed class AdoptionSession(
    StateMachine<AdoptionPhase, AdoptionAction> machine,
    IStreamingAgentModel model,
    KnowledgeBase knowledge,
    ILogger logger)
    : ChatSession<AdoptionPhase, AdoptionAction>(machine, model, logger)
{
    internal KnowledgeBase Knowledge => knowledge;
}
