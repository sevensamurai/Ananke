using Ananke.StateMachine;

/// <summary>
/// Adoption conversation protocol: phases, transitions, and factory.
/// </summary>
internal enum AdoptionPhase { Searching, Interrupted, Paperwork, Payment, Done }
internal enum AdoptionAction { Start, StartPaperwork, StartPayment, Complete, Interrupt, Resume }

internal static class AdoptionMachine
{
    /// <summary>
    /// Creates a configured state machine for a single adoption session.
    /// <para>The protocol:</para>
    /// <code>
    /// Searching ──[StartPaperwork]──► Paperwork ──[StartPayment]──► Payment ──[Complete]──► Done
    /// Searching ──[Interrupt]──► Interrupted ──[Resume]──► Searching (stack-based)
    /// {Searching,Paperwork} ──[Start]──► self  (re-triggers OnEnter for a new chat turn)
    /// </code>
    /// </summary>
    internal static StateMachine<AdoptionPhase, AdoptionAction> Create() =>
        StateMachine.Create<AdoptionPhase, AdoptionAction>(
            AdoptionPhase.Searching, b => b
                // Self-transitions — each chat turn fires Start to (re-)trigger OnEnter
                .From(AdoptionPhase.Searching).On(AdoptionAction.Start).To(AdoptionPhase.Searching)
                .From(AdoptionPhase.Paperwork).On(AdoptionAction.Start).To(AdoptionPhase.Paperwork)
                // Normal flow
                .From(AdoptionPhase.Searching).On(AdoptionAction.StartPaperwork).To(AdoptionPhase.Paperwork)
                .From(AdoptionPhase.Paperwork).On(AdoptionAction.StartPayment).To(AdoptionPhase.Payment)
                .From(AdoptionPhase.Payment).On(AdoptionAction.Complete).To(AdoptionPhase.Done)
                // Interrupts — dedicated phase with optional clarification workflow
                .From(AdoptionPhase.Searching).On(AdoptionAction.Interrupt).ToInterrupt(AdoptionPhase.Interrupted)
                .From(AdoptionPhase.Interrupted).On(AdoptionAction.Resume).ToResume());
}
