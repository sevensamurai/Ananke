using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;

/// <summary>
/// Interrupted phase — receives the interrupt payload and optionally runs
/// a clarification workflow before resuming the previous phase.
/// <para>
/// The <see cref="AdoptionPhase.Interrupted"/> state has its own <c>OnEnter</c>,
/// so you can plug in an agent that checks for contradictions, asks follow-up
/// questions, or enriches the conversation context before resuming.
/// </para>
/// </summary>
internal static class InterruptPhase
{
    internal static void Register(AdoptionSession session)
    {
        AgentMessage? pendingMessage = null;

        // ── OnInterrupt fires first (before entering the Interrupted state) ──
        session.Machine.OnInterrupt(async (payload, _) =>
        {
            pendingMessage = payload as AgentMessage;
            session.Logger.LogInformation("⚡ Interrupt received");
            await session.EmitAsync("interrupted", new { });
        });

        // ── OnEnter(Interrupted) — the interrupt has its own workflow slot ────
        session.Machine.OnEnter(AdoptionPhase.Interrupted, async ct =>
        {
            // Patch orphaned tool_calls left by the cancelled workflow.
            // Without this the LLM rejects the history on resume.
            session.Messages.PatchOrphanedToolCalls();

            if (pendingMessage is not null)
            {
                session.Messages.Add(pendingMessage);
                session.Logger.LogInformation("📝 Interrupt message added to conversation");

                // ── Optional: clarification workflow ─────────────────
                // Uncomment to have the agent evaluate whether the new
                // message contradicts the prior request and ask for
                // clarification if needed:
                //
                // await session.StreamAsync(
                //     StreamingChatWorkflow.Create("clarify", session.Model)
                //         .WithSystemPrompt(ClarificationPrompt)
                //         .BuildStream(session.Messages, ct));
            }

            pendingMessage = null;

            // Let the client know we are re-generating — this retires the
            // old partial bubble and creates a fresh one on the client side.
            await session.EmitAsync("resumed", new { });

            // Acknowledgment lands in the NEW bubble, visible before the
            // search workflow starts streaming the updated response.
            await session.EmitAsync("delta",
                new { text = "*📝 Got it — updating your search...*\n\n" });

            // Resume back to the phase that was interrupted (popped from stack)
            await session.Machine.FireAsync(AdoptionAction.Resume);
        });
    }
}
