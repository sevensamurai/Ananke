using Ananke.Organics.Sensing;
using Ananke.Roles.Studio;
using Shouldly;

namespace Ananke.Roles.Tests;

[TestFixture]
public sealed class StudioRouterTests
{
    [Test]
    public async Task RouteAsync_KnownIntent_ReturnsExpectedWorkflowName()
    {
        var router = new StudioRouter(
            new FixedRouter("fallback"),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["review"] = "review-workflow"
            },
            "default-workflow");

        var result = await router.RouteAsync("please review this draft");

        result.ShouldBe("review-workflow");
    }

    [Test]
    public async Task RouteAsync_UnknownIntent_FallsBackToDefault()
    {
        var router = new StudioRouter(
            new ThrowingRouter(),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["review"] = "review-workflow"
            },
            "default-workflow");

        var result = await router.RouteAsync("something unrelated");

        result.ShouldBe("default-workflow");
    }

    private sealed class FixedRouter(string result) : IRequestRouter
    {
        public Task<string> RouteAsync(string userMessage, CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    private sealed class ThrowingRouter : IRequestRouter
    {
        public Task<string> RouteAsync(string userMessage, CancellationToken ct = default) =>
            throw new InvalidOperationException("no route");
    }
}
