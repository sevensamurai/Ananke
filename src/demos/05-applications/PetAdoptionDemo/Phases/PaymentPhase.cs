/// <summary>
/// Payment phase — human-in-the-loop credit card collection.
/// Emits a <c>payment_required</c> SSE event and waits for the user to submit
/// payment details via the <c>/api/payment</c> endpoint. Credit card info is handled
/// exclusively in that endpoint and is <b>never</b> stored in session state,
/// conversation history, or checkpoint data.
/// </summary>
internal static class PaymentPhase
{
    internal static void Register(AdoptionSession session)
    {
        session.Machine.OnEnter(AdoptionPhase.Payment, async ct =>
        {
            session.Logger.LogInformation("💳 Entering Payment phase — awaiting card details");
            await session.EmitAsync("phase", new { phase = "payment" });

            var ctx = session.GetContext<AdoptionContext>();
            var feeText = ctx.AdoptionFee is { } fee
                ? $"${fee:F0}"
                : "the adoption fee";

            await session.EmitAsync("delta",
                new { text = $"\n\n💳 **Payment required.** Please enter your credit card details to pay {feeText} and complete the adoption.\n\n" });
            await session.EmitAsync("payment_required", new
            {
                message = $"Please enter your credit card number to complete the adoption.",
                petName = ctx.PetName,
                amount = ctx.AdoptionFee
            });

            // The phase work ends here. The SSE stream from ChatEndpoint will close
            // after RunSseLoopAsync sees no more work (state is Payment, not Done).
            // The /api/payment endpoint picks up from here once the user submits.
        });
    }
}
