using Ananke.Platforms.Slack;
using Shouldly;
using SlackNet;
using SlackNet.Blocks;
using SlackNet.WebApi;

namespace Ananke.Platforms.Slack.Tests;

/// <summary>
/// Verifies that <see cref="SlackResponseSink.OpenViewAsync"/> and
/// <see cref="SlackResponseSink.UpdateViewAsync"/> delegate to <see cref="IViewsApi"/>
/// with the correct arguments.
/// </summary>
[TestFixture]
public sealed class SlackModalSinkTests
{
    [Test]
    public async Task OpenViewAsync_DelegatesToViewsApiOpen_ReturnsViewId()
    {
        var fake = new FakeViewsApi { ViewIdToReturn = "V123" };
        var sink = new SlackResponseSink(fake, new SlackAdapterOptions { BotToken = "xoxb-test" });

        var viewId = await sink.OpenViewAsync("trigger-abc", new ModalViewDefinition
        {
            Title = new PlainText("Test Modal"),
            CallbackId = "cb_test"
        });

        viewId.ShouldBe("V123");
        fake.LastTriggerId.ShouldBe("trigger-abc");
        fake.LastOpenedView.ShouldNotBeNull();
        fake.LastOpenedView!.CallbackId.ShouldBe("cb_test");
    }

    [Test]
    public async Task OpenViewAsync_ViewApiReturnsNullViewId_ReturnsEmptyString()
    {
        var fake = new FakeViewsApi { ViewIdToReturn = null };
        var sink = new SlackResponseSink(fake, new SlackAdapterOptions { BotToken = "xoxb-test" });

        var viewId = await sink.OpenViewAsync("trigger-xyz", new ModalViewDefinition
        {
            Title = new PlainText("Empty")
        });

        viewId.ShouldBe(string.Empty);
    }

    [Test]
    public async Task UpdateViewAsync_DelegatesToViewsApiUpdateByViewId()
    {
        var fake = new FakeViewsApi();
        var sink = new SlackResponseSink(fake, new SlackAdapterOptions { BotToken = "xoxb-test" });

        await sink.UpdateViewAsync("V456", new ModalViewDefinition
        {
            Title = new PlainText("Updated Modal"),
            CallbackId = "cb_updated"
        });

        fake.LastUpdatedViewId.ShouldBe("V456");
        fake.LastUpdatedView.ShouldNotBeNull();
        fake.LastUpdatedView!.CallbackId.ShouldBe("cb_updated");
    }

    // ── Fake ────────────────────────────────────────────────────────────────

    private sealed class FakeViewsApi : IViewsApi
    {
        public string? ViewIdToReturn { get; set; } = "V_FAKE";
        public string? LastTriggerId { get; private set; }
        public ModalViewDefinition? LastOpenedView { get; private set; }
        public string? LastUpdatedViewId { get; private set; }
        public ModalViewDefinition? LastUpdatedView { get; private set; }

        public Task<ViewResponse> Open(string triggerId, ViewDefinition view,
            CancellationToken cancellationToken = default)
        {
            LastTriggerId = triggerId;
            LastOpenedView = view as ModalViewDefinition;
            return Task.FromResult(new ViewResponse
            {
                View = ViewIdToReturn is null ? null! : new ModalViewInfo { Id = ViewIdToReturn }
            });
        }

        public Task<ViewResponse> UpdateByViewId(ViewDefinition view, string viewId,
            string? hash = null, CancellationToken cancellationToken = default)
        {
            LastUpdatedViewId = viewId;
            LastUpdatedView = view as ModalViewDefinition;
            return Task.FromResult(new ViewResponse());
        }

        public Task<ViewResponse> Publish(string userId, HomeViewDefinition viewDefinition,
            string? hash = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ViewResponse());

        public Task<ViewResponse> Push(string triggerId, ViewDefinition view,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ViewResponse());

        public Task<ViewResponse> UpdateByExternalId(ViewDefinition view, string externalId,
            string? hash = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ViewResponse());
    }
}
