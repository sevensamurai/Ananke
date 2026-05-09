using Ananke.AspNetCore.Sessions;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;

internal static class InterruptEndpoint
{
    internal static void MapInterruptEndpoint(this WebApplication app, InMemorySessionStore<AdoptionSession> sessions)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Interrupt");

        app.MapPost("/api/interrupt", async (InterruptRequest request) =>
        {
            logger.LogInformation("⚡ Interrupt received for session {SessionId}: \"{Message}\"",
                request.SessionId, request.Message ?? "(no message)");

            var session = sessions.Get(request.SessionId);
            if (session is null)
            {
                logger.LogWarning("Interrupt rejected — session {SessionId} not found or completed",
                    request.SessionId);
                return Results.NotFound(new { error = "Session not found or already completed." });
            }

            var message = AgentMessage.User(
                request.Message ?? "The user interrupted the response.");

            // Fire the interrupt transition — the SM validates (guards, stack, depth)
            // and delivers the payload via OnInterrupt. That's it.
            var result = await session.Machine.FireAsync(AdoptionAction.Interrupt, message);

            if (!result.Success)
            {
                logger.LogWarning(
                    "Interrupt blocked by FSM — session {SessionId}, phase {Phase}: {Reason}",
                    request.SessionId, result.CurrentState, result.ErrorMessage);
                return Results.Conflict(new
                {
                    error = "Interrupt not allowed in current phase.",
                    reason = result.ErrorMessage,
                    phase = result.CurrentState.ToString()
                });
            }

            logger.LogInformation(
                "✅ Interrupt accepted — session {SessionId}, phase {Phase}",
                request.SessionId, result.CurrentState);
            return Results.Ok(new
            {
                status = "interrupted",
                sessionId = request.SessionId,
                phase = result.CurrentState.ToString()
            });
        })
        .WithName("Interrupt")
        .WithDescription("Interrupt the current agent generation for a session.");
    }
}
