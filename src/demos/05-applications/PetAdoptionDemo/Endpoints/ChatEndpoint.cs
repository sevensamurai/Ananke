using Ananke.AspNetCore.Sessions;
using Ananke.AspNetCore.Sse;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;

internal static class ChatEndpoint
{
    internal static void MapChatEndpoints(
        this WebApplication app,
        SessionFactory factory,
        InMemorySessionStore<AdoptionSession> sessions)
    {
        app.MapPost("/api/chat", async (ChatRequest request, HttpContext context, CancellationToken ct) =>
        {
            context.Response.EnableSse();

            var sessionId = request.SessionId ?? Guid.NewGuid().ToString("N");

            // Atomic: returns existing session or creates one via factory.
            // CreateAsync restores conversation from distributed memory (Redis)
            // when available, falling back to client-provided history.
            var session = sessions.GetOrCreate(sessionId,
                () => factory.CreateAsync(sessionId, request.History).GetAwaiter().GetResult());

            // Append the new user message for this turn
            if (request.AudioBase64 is not null)
            {
                var audioBytes = Convert.FromBase64String(request.AudioBase64);
                session.Messages.Add(AgentMessage.UserAudio(audioBytes, request.AudioMimeType ?? "audio/wav"));
            }
            else if (request.ImageBase64 is not null)
            {
                var imageBytes = Convert.FromBase64String(request.ImageBase64);
                var imagePart = new ImagePart
                {
                    Data = imageBytes,
                    MimeType = request.ImageMimeType ?? "image/jpeg"
                };
                var parts = new List<ContentPart> { imagePart };
                if (request.Message is not null)
                    parts.Insert(0, new TextPart(request.Message));
                session.Messages.Add(AgentMessage.User(parts));
            }
            else if (request.Message is not null)
            {
                session.Messages.Add(AgentMessage.User(request.Message));
            }

            // Bind SSE output to this request's response
            session.BindResponse(context.Response.WriteSseAsync);

            try
            {
                await context.Response.WriteSseAsync("session",
                    new { sessionId, phase = session.Machine.CurrentState.ToString().ToLowerInvariant() });

                // Self-transition (re-)triggers OnEnter for the current phase
                var fireResult = await session.Machine.FireAsync(AdoptionAction.Start);
                if (!fireResult.Success)
                {
                    // Phase doesn't accept Start (Payment, Interrupted — transient, auto-advancing)
                    await context.Response.WriteSseAsync("error",
                        new { message = $"Cannot send messages during {session.Machine.CurrentState} phase." });
                    return;
                }

                var reachedDone = await session.Machine.RunSseLoopAsync(AdoptionPhase.Done);

                if (reachedDone)
                {
                    await context.Response.WriteSseAsync("phase", new { phase = "done" });
                }

                // Persist conversation to distributed memory after each turn
                await factory.SaveConversationAsync(sessionId, session.Messages);

                // Always emit "done" so the client can finalize the turn
                // (push assistant reply into its history array, reset UI, etc.).
                await context.Response.WriteSseAsync("done", new { text = "" });

                if (reachedDone)
                {
                    await factory.ClearConversationAsync(sessionId);
                    sessions.Remove(sessionId);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected — keep the session intact so the next
                // request can resume with full conversation history.
                // Still save to distributed memory so history survives restarts.
                await factory.SaveConversationAsync(sessionId, session.Messages);
                throw;
            }
            catch
            {
                sessions.Remove(sessionId);
                throw;
            }
            finally
            {
                // Unbind SSE writer — this response is about to be disposed.
                // Any later EmitAsync calls (e.g. from an interrupt arriving
                // between HTTP requests) become harmless no-ops.
                session.BindResponse((_, _) => Task.CompletedTask);
            }
        })
        .WithName("Chat")
        .WithDescription("Send a message and receive a streaming response via SSE.")
        .Produces(StatusCodes.Status200OK, contentType: "text/event-stream");
    }
}
