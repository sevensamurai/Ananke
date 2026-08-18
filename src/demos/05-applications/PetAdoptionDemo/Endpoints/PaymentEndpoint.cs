using Ananke.AspNetCore.Sessions;
using Ananke.AspNetCore.Sse;
using Ananke.Orchestration.Jobs;

/// <summary>
/// Handles credit card payment submission for the human-in-the-loop payment flow.
/// Payment processing is handed off to the external payment service
/// via <see cref="HandoffProxy{TMessage, TResponse}"/>.
/// Card details are used exclusively within the request handler and are
/// <b>never</b> persisted to session state, conversation history, or checkpoints.
/// </summary>
internal static class PaymentEndpoint
{
    internal static void MapPaymentEndpoint(
        this WebApplication app,
        InMemorySessionStore<AdoptionSession> sessions,
        HandoffProxy<PaymentHandoff, PaymentResult> payments,
        SessionFactory factory)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Payment");

        app.MapPost("/api/payment", async (PaymentRequest request, HttpContext context, CancellationToken ct) =>
        {
            context.Response.EnableSse();

            var session = sessions.Get(request.SessionId);
            if (session is null)
            {
                await context.Response.WriteSseAsync("error",
                    new { message = "Session not found or already completed." });
                return;
            }

            if (session.Machine.CurrentState != AdoptionPhase.Payment)
            {
                await context.Response.WriteSseAsync("error",
                    new { message = $"Session is not awaiting payment (current phase: {session.Machine.CurrentState})." });
                return;
            }

            session.BindResponse((eventName, data) => context.Response.WriteSseAsync(eventName, data));

            try
            {
                // ── Card number lives ONLY in this local scope ──────────────
                // It is never added to session.Messages, checkpoints, or logs.
                var last4 = request.CardNumber.Length >= 4
                    ? request.CardNumber[^4..]
                    : request.CardNumber;

                logger.LogInformation("💳 Processing payment for session {SessionId} (card ending {Last4})",
                    request.SessionId, last4);

                await session.EmitAsync("delta",
                    new { text = "\n\n💳 **Processing payment...** This may take a moment.\n\n" });

                // ── Handoff to payment service ──────────────────────────────
                var handoff = new PaymentHandoff
                {
                    SessionId = request.SessionId,
                    Last4 = last4
                };

                var result = await payments.SendAsync(handoff, ct);

                if (result.Success)
                {
                    var receipt = $"Transaction: {result.TransactionId}";
                    if (result.InvoiceId is not null)
                        receipt += $" · Invoice: {result.InvoiceId}";

                    await session.EmitAsync("delta",
                        new
                        {
                            text = $"✅ **Payment confirmed** (card ending in {last4})! " +
                                   $"{result.Message} 🐾\n\n" +
                                   $"{receipt}\n" +
                                   "You'll receive a confirmation email with pickup instructions shortly.\n"
                        });
                }
                else
                {
                    await session.EmitAsync("delta",
                        new { text = $"❌ **Payment failed:** {result.Message}\nPlease try again.\n" });
                    return;
                }

                await session.Machine.FireAsync(AdoptionAction.Complete);

                await session.EmitAsync("phase", new { phase = "done" });
                await session.EmitAsync("done", new { text = "" });

                await factory.ClearConversationAsync(request.SessionId);
                sessions.Remove(request.SessionId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            finally
            {
                session.BindResponse((_, _) => Task.CompletedTask);
            }
        })
        .WithName("SubmitPayment")
        .WithDescription("Submit credit card payment for an adoption. Card details are not persisted.")
        .Produces(StatusCodes.Status200OK, contentType: "text/event-stream");
    }
}
