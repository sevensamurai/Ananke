using Ananke.Abstractions.Agents;
using Ananke.Organics.Sensing;
using Ananke.Platforms;
using Ananke.Roles.Roles;
using Ananke.Roles.Slack;
using Ananke.Roles.Studio;
using Shouldly;
using RolesAgentRole = Ananke.Roles.Roles.AgentRole;
using AbstractionsAgentRole = Ananke.Abstractions.Agents.AgentRole;

namespace Ananke.Roles.Tests.Slack;

[TestFixture]
public sealed class RoleAwareMessageHandlerTests
{
    private static RolesAgentRole MakeRole(string name) => new()
    {
        Name = name,
        DomainTags = ["test"],
        ModelAlias = "local",
        SystemPromptPath = "prompt.txt"
    };

    private static PlatformMessage MakeMessage(string channelId, string text = "hello") => new()
    {
        ChannelId = channelId,
        UserId = "U1",
        Message = new AgentMessage { Role = AbstractionsAgentRole.User, Content = text }
    };

    private static StudioRouter MakeRouter(string defaultWorkflow) =>
        new(new NullRequestRouter(),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            defaultWorkflow);

    [Test]
    public async Task HandleAsync_MappedChannel_RoutesToRoleName()
    {
        var catalog = new AgentRoleCatalog();
        catalog.Add(MakeRole("writer"));
        var options = new StudioOptions
        {
            ChannelRoleMap = new Dictionary<string, string> { ["C1"] = "writer" }
        };
        var channelMap = new SlackChannelMap(options, catalog);
        var router = MakeRouter("default");

        string? capturedWorkflow = null;
        var handler = new CapturingRoleAwareMessageHandler(
            channelMap, router, "default",
            (_, _, wf, _) => { capturedWorkflow = wf; return Task.CompletedTask; });

        await handler.HandleAsync(MakeMessage("C1"), NullSink.Instance);

        capturedWorkflow.ShouldBe("writer");
    }

    [Test]
    public async Task HandleAsync_UnmappedChannel_RoutesToDefault()
    {
        var catalog = new AgentRoleCatalog();
        var options = new StudioOptions
        {
            ChannelRoleMap = new Dictionary<string, string>()
        };
        var channelMap = new SlackChannelMap(options, catalog);
        var router = MakeRouter("default");

        string? capturedWorkflow = null;
        var handler = new CapturingRoleAwareMessageHandler(
            channelMap, router, "default",
            (_, _, wf, _) => { capturedWorkflow = wf; return Task.CompletedTask; });

        await handler.HandleAsync(MakeMessage("C_unknown"), NullSink.Instance);

        capturedWorkflow.ShouldBe("default");
    }

    [Test]
    public async Task HandleAsync_DoesNotThrow_WhenDefaultHandlerIsNoOp()
    {
        var catalog = new AgentRoleCatalog();
        var options = new StudioOptions();
        var channelMap = new SlackChannelMap(options, catalog);
        var router = MakeRouter("fallback");

        var handler = new RoleAwareMessageHandler(channelMap, router, "fallback");

        await Should.NotThrowAsync(() =>
            handler.HandleAsync(MakeMessage("C_any"), NullSink.Instance));
    }

    // ---- Test helpers ----

    private sealed class CapturingRoleAwareMessageHandler(
        SlackChannelMap channelMap,
        StudioRouter router,
        string defaultWorkflow,
        Func<PlatformMessage, IPlatformResponseSink, string, CancellationToken, Task> onRouted)
        : RoleAwareMessageHandler(channelMap, router, defaultWorkflow)
    {
        protected override Task OnWorkflowRoutedAsync(
            PlatformMessage message,
            IPlatformResponseSink responseSink,
            string workflowName,
            CancellationToken ct) => onRouted(message, responseSink, workflowName, ct);
    }

    private sealed class NullSink : IPlatformResponseSink
    {
        public static readonly NullSink Instance = new();
        public Task<string> SendMessageAsync(string channelId, string? threadId, string text,
            CancellationToken ct = default) => Task.FromResult(string.Empty);
        public Task UpdateMessageAsync(string channelId, string messageId, string text,
            CancellationToken ct = default) => Task.CompletedTask;
        public Task SendTypingAsync(string channelId, string? threadId,
            CancellationToken ct = default) => Task.CompletedTask;
        public Task AddReactionAsync(string channelId, string messageId, string emoji,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullRequestRouter : IRequestRouter
    {
        public Task<string> RouteAsync(string userMessage, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
    }
}
