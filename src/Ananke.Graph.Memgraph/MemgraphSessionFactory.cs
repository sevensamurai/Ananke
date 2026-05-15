using Ananke.Graph.Abstractions;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace Ananke.Graph.Memgraph;

/// <summary>
/// Owns the Bolt <see cref="IDriver"/> connection to Memgraph and exposes an async
/// session factory.  Register as a singleton; dispose on application shutdown.
/// </summary>
public sealed class MemgraphSessionFactory : IAsyncDisposable
{
    private readonly IDriver _driver;
    private readonly string? _database;

    /// <summary>
    /// Initialises the factory from <see cref="GraphConnectionOptions"/> supplied via
    /// <c>IOptions&lt;GraphConnectionOptions&gt;</c>.
    /// </summary>
    public MemgraphSessionFactory(IOptions<GraphConnectionOptions> options)
        : this(options.Value) { }

    /// <summary>
    /// Initialises the factory directly from <paramref name="options"/>.
    /// </summary>
    public MemgraphSessionFactory(GraphConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _database = options.Database;

        var builder = GraphDatabase.Driver(
            options.Uri,
            options.Username is not null
                ? AuthTokens.Basic(options.Username, options.Password ?? string.Empty)
                : AuthTokens.None,
            o =>
            {
                o.WithMaxConnectionPoolSize(options.MaxConnectionPoolSize);
                if (options.MaxConnectionLifetime.HasValue)
                    o.WithMaxConnectionLifetime(options.MaxConnectionLifetime.Value);
            });

        _driver = builder;
    }

    /// <summary>
    /// Opens a new async session.  The caller is responsible for disposing the
    /// returned session.
    /// </summary>
    public IAsyncSession OpenSession()
    {
        return _database is not null
            ? _driver.AsyncSession(c => c.WithDatabase(_database))
            : _driver.AsyncSession();
    }

    /// <summary>
    /// Verifies connectivity to the Memgraph instance.
    /// Throws if the server is unreachable.
    /// </summary>
    public Task VerifyConnectivityAsync(CancellationToken ct = default) =>
        _driver.VerifyConnectivityAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _driver.DisposeAsync().ConfigureAwait(false);
}
