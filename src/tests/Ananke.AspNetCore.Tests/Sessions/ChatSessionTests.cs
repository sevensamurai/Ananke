using Ananke.AspNetCore.Sessions;
using Ananke.StateMachine;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ananke.AspNetCore.Tests.Sessions;

[TestFixture]
public class ChatSessionTests
{
    private enum Phase { Chatting }

    private enum Go { Stay }

    [Test]
    public async Task EmitAsync_BeforeBindResponse_Throws()
    {
        // An event emitted before a response is bound has nowhere to go; it used to vanish.
        var session = new ChatSession<Phase, Go>(
            StateMachine.StateMachine.Create<Phase, Go>(Phase.Chatting, _ => { }),
            model: null!,
            NullLogger.Instance);

        await Should.ThrowAsync<InvalidOperationException>(() => session.EmitAsync("status", new { ready = true }));
    }
}
