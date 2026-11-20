using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Qdrant.Client;

namespace Ananke.Qdrant.Tests;

/// <summary>
/// One Qdrant for the whole test run, in a container. Tests that need it are ignored when Docker isn't available, and
/// fail when Docker is there and the container did not start. Each test uses collections of its own, so they don't see
/// each other's points.
/// </summary>
[SetUpFixture]
public sealed class QdrantServer
{
    private const int GrpcPort = 6334;
    private const int HttpPort = 6333;

    private static IContainer? _container;
    private static bool _dockerUnavailable;

    public static QdrantClient? Client { get; private set; }

    public static string? Unavailable { get; private set; }

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        try
        {
            _container = new ContainerBuilder("qdrant/qdrant:v1.19.2")
                .WithPortBinding(GrpcPort, assignRandomHostPort: true)
                .WithPortBinding(HttpPort, assignRandomHostPort: true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(HttpPort).ForPath("/readyz")))
                .Build();
            await _container.StartAsync().WaitAsync(TimeSpan.FromMinutes(3));
            Client = new QdrantClient(_container.Hostname, _container.GetMappedPublicPort(GrpcPort));
        }
        catch (Exception e)
        {
            _dockerUnavailable = e is DockerUnavailableException;
            var reason = _dockerUnavailable ? "Docker isn't available" : "the container did not start";
            Unavailable = $"No Qdrant container: {reason} ({e.GetType().Name}: {e.Message})";
        }
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        Client?.Dispose();
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>The client, or the test is ignored (no Docker) or failed (the container did not start).</summary>
    public static QdrantClient Require()
    {
        if (Client is not null)
            return Client;
        if (_dockerUnavailable)
            Assert.Ignore(Unavailable ?? "No Qdrant container");
        Assert.Fail(Unavailable ?? "No Qdrant container");
        return null!;
    }

    /// <summary>A collection name no other test uses.</summary>
    public static string NewCollection(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
