using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Ananke.Redis.Tests;

/// <summary>
/// A Valkey server in a container, started by a fixture and disposed by it. When the server could not be started,
/// <see cref="StartAsync"/> returns one whose <see cref="Unavailable"/> says why, and <see cref="Require"/> ignores the
/// test with that message when Docker isn't available, and fails it otherwise.
/// </summary>
public sealed class ValkeyServer : IAsyncDisposable
{
    private const int RedisPort = 6379;

    private readonly IContainer? _container;
    private readonly bool _dockerUnavailable;

    private ValkeyServer(IContainer? container, string? unavailable, string? password = null, bool dockerUnavailable = false)
    {
        _container = container;
        Unavailable = unavailable;
        Password = password;
        _dockerUnavailable = dockerUnavailable;
    }

    /// <summary>Why there is no server, or <c>null</c> when it is running.</summary>
    public string? Unavailable { get; }

    /// <summary>The password the server requires, or <c>null</c> for none.</summary>
    public string? Password { get; }

    public string Host => Container.Hostname;

    public int Port => Container.GetMappedPublicPort(RedisPort);

    /// <summary><c>host:port</c>, for a StackExchange.Redis connection string.</summary>
    public string Endpoint => $"{Host}:{Port}";

    /// <summary>
    /// <see cref="Endpoint"/> with the client's timeouts cut to 1 s (5 s by default), for a client whose calls are
    /// expected to fail: each fails within about a second. The lock makes several synchronous calls (syncTimeout)
    /// before it answers.
    /// </summary>
    public string EndpointWithShortTimeouts => $"{Endpoint},asyncTimeout=1000,syncTimeout=1000,connectTimeout=1000";

    /// <summary>A connection string with the server's password, if it has one.</summary>
    public string ConnectionString => Password is null ? Endpoint : $"{Endpoint},password={Password}";

    private IContainer Container =>
        _container ?? throw new InvalidOperationException(Unavailable ?? "No Valkey container");

    /// <summary>Starts a server, requiring <paramref name="password"/> when one is given.</summary>
    public static async Task<ValkeyServer> StartAsync(string? password = null)
    {
        IContainer? container = null;
        try
        {
            var builder = new ContainerBuilder("valkey/valkey:8")
                .WithPortBinding(RedisPort, assignRandomHostPort: true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Ready to accept connections"));
            if (password is not null)
                builder = builder.WithCommand("--requirepass", password);
            container = builder.Build();
            await container.StartAsync().WaitAsync(TimeSpan.FromMinutes(3)).ConfigureAwait(false);
            return new ValkeyServer(container, null, password);
        }
        catch (Exception e)
        {
            if (container is not null)
                await container.DisposeAsync().ConfigureAwait(false);
            var dockerUnavailable = e is DockerUnavailableException;
            var reason = dockerUnavailable ? "Docker isn't available" : "the container did not start";
            return new ValkeyServer(
                null, $"No Valkey container: {reason} ({e.GetType().Name}: {e.Message})", dockerUnavailable: dockerUnavailable);
        }
    }

    /// <summary>
    /// When there is no server: ignores the test if Docker isn't available, and fails it if Docker is there and the
    /// container did not start.
    /// </summary>
    public void Require()
    {
        if (Unavailable is null)
            return;
        if (_dockerUnavailable)
            Assert.Ignore(Unavailable);
        Assert.Fail(Unavailable);
    }

    /// <summary>Freezes the server's process: connections stay open and nothing answers.</summary>
    public Task PauseAsync() => Container.PauseAsync();

    public Task UnpauseAsync() => Container.UnpauseAsync();

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync().ConfigureAwait(false);
    }
}
