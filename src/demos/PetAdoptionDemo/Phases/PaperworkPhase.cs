using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Tools;

/// <summary>
/// Paperwork phase — guides the user through the adoption application.
/// The <c>submit_application</c> tool fires <see cref="AdoptionAction.StartPayment"/>
/// to transition the session into the Payment phase.
/// </summary>
internal static class PaperworkPhase
{
    const string Prompt = """
        You are the adoption paperwork assistant at Happy Tails Shelter.
        The user has selected a pet and you are helping them complete the adoption application.

        You have access to the following tools:
        - get_requirements: Look up what documents and information are needed for this adoption.
        - submit_application: Submit the completed adoption application.
          IMPORTANT: Only call this after confirming the user has all required information ready.

        Guidelines:
        - Walk the user through the requirements step by step.
        - Be reassuring — the paperwork is quick and straightforward.
        - Confirm all details before submitting.
        - After submission, let the user know payment is the final step.
        """;

    internal static void Register(AdoptionSession session)
    {
        var tools = CreateTools(session);

        session.Machine.OnEnter(AdoptionPhase.Paperwork, async ct =>
        {
            session.Logger.LogInformation("📋 Entering Paperwork phase");
            await session.EmitAsync("phase", new { phase = "paperwork" });

            // The start_adoption tool fires the state-machine transition while
            // the Search-phase workflow is still mid–tool-call, so the tool result
            // message hasn't been appended yet.  Patch the orphan so the LLM API
            // accepts the conversation history.
            session.Messages.PatchOrphanedToolCalls();

            session.Messages.Add(AgentMessage.User(
                "I've selected a pet. Please help me with the adoption paperwork."));

            await session.StreamAsync(
                StreamingChatWorkflow.Create("paperwork", session.Model)
                    .WithSystemPrompt(Prompt)
                    .WithTools(tools)
                    .WithMaxToolRounds(10)
                    .BuildStream(session.Messages, ct));
        });
    }

    private static ToolKit CreateTools(AdoptionSession session) => new ToolKit("paperwork")
        .AddTool(
            name: "get_requirements",
            description:
                "Look up the documents and information needed to complete the adoption application.",
            execute: () =>
            {
                session.Logger.LogDebug("get_requirements called");
                var ctx = session.GetContext<AdoptionContext>();
                var feeText = ctx.AdoptionFee is { } fee
                    ? $"${fee:F0}"
                    : "see adoption fee schedule";
                return ToolResult.Ok(
                    "**Required for adoption:**\n" +
                    "1. Valid photo ID (driver's license or passport)\n" +
                    "2. Proof of address (utility bill or lease)\n" +
                    "3. Landlord approval letter (if renting)\n" +
                    "4. Veterinary reference (if you have other pets)\n\n" +
                    $"**Adoption fee:** {feeText}\n" +
                    "(includes spay/neuter, vaccinations, and microchip)");
            })
        .AddTool(
            name: "submit_application",
            description:
                "Submit the completed adoption application. " +
                "Only call this after confirming the user has the required documents ready.",
            execute: async (string applicantName) =>
            {
                session.Logger.LogInformation("📋 Application submitted by {Name}", applicantName);
                await Task.Delay(1000);

                // Fire-and-forget: let the streaming workflow finish this tool round
                // before PaymentPhase OnEnter runs. RunSseLoopAsync picks up the new phase.
                _ = session.Machine.FireAsync(AdoptionAction.StartPayment);

                return ToolResult.Ok(
                    $"✅ Application submitted for **{applicantName}**! " +
                    $"Proceeding to payment.");
            },
            paramName: "applicant_name",
            paramDescription: "Full name of the person adopting");
}
