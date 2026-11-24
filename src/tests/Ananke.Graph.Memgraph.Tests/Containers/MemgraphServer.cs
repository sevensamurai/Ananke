using Ananke.Graph.Abstractions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Ananke.Graph.Memgraph.Tests.Containers;

/// <summary>
/// One Memgraph with MAGE for the tests of this namespace, in a container. Explicit, with every test under it: it starts
/// only when those tests are asked for (<c>--filter TestCategory=Containers</c>). Tests that need it are ignored when
/// Docker isn't available, and fail when Docker is there and the container did not start. Each test uses nodes with ids
/// of its own, so they don't see each other's data.
/// </summary>
[SetUpFixture]
[Category("Containers")]
[Explicit("Starts a container. Run with: bash scripts/ci.sh --containers")]
public sealed class MemgraphServer
{
    /// <summary>The image the tests run against; the PageRank and community detection tests need MAGE.</summary>
    public const string Image = "memgraph/memgraph-mage:3.14.0";

    private const int BoltPort = 7687;

    private static IContainer? _container;
    private static bool _dockerUnavailable;

    public static MemgraphSessionFactory? Factory { get; private set; }

    public static string? Unavailable { get; private set; }

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        try
        {
            _container = new ContainerBuilder(Image)
                .WithPortBinding(BoltPort, assignRandomHostPort: true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(BoltPort))
                .Build();
            await _container.StartAsync().WaitAsync(TimeSpan.FromMinutes(3));
            Factory = new MemgraphSessionFactory(new GraphConnectionOptions
            {
                Uri = $"bolt://{_container.Hostname}:{_container.GetMappedPublicPort(BoltPort)}",
                Username = "memgraph",
                Password = string.Empty,
            });
            await Factory.VerifyConnectivityAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (Exception e)
        {
            if (Factory is not null)
                await Factory.DisposeAsync();
            Factory = null;
            _dockerUnavailable = e is DockerUnavailableException;
            var reason = _dockerUnavailable ? "Docker isn't available" : "the container did not start";
            Unavailable = $"No Memgraph container: {reason} ({e.GetType().Name}: {e.Message})";
        }
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (Factory is not null)
            await Factory.DisposeAsync();
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>The session factory, or the test is ignored (no Docker) or failed (the container did not start).</summary>
    public static MemgraphSessionFactory Require()
    {
        if (Factory is not null)
            return Factory;
        if (_dockerUnavailable)
            Assert.Ignore(Unavailable ?? "No Memgraph container");
        Assert.Fail(Unavailable ?? "No Memgraph container");
        return null!;
    }
}
