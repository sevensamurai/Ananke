using DotNet.Testcontainers.Builders;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Ananke.Postgres.Tests.Containers;

/// <summary>
/// One Postgres with pgvector for the whole test run, in a container. Explicit, with every test under it: it starts only
/// when those tests are asked for (<c>--filter TestCategory=Containers</c>). Tests that need it are ignored when Docker
/// isn't available, and fail when Docker is there and the container did not start. Each test uses tables of its own, so
/// they don't see each other's rows.
/// </summary>
[SetUpFixture]
[Category("Containers")]
[Explicit("Starts a container. Run with: bash scripts/ci.sh --containers")]
public sealed class PostgresDatabase
{
    private static PostgreSqlContainer? _container;
    private static bool _dockerUnavailable;

    public static NpgsqlDataSource? DataSource { get; private set; }

    /// <summary>The container's connection string, password included, for a data source with settings of its own.</summary>
    public static string? ConnectionString { get; private set; }

    public static string? Unavailable { get; private set; }

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
            await _container.StartAsync().WaitAsync(TimeSpan.FromMinutes(3));
            ConnectionString = _container.GetConnectionString();
            DataSource = NpgsqlDataSource.Create(ConnectionString);
        }
        catch (Exception e)
        {
            _dockerUnavailable = e is DockerUnavailableException;
            var reason = _dockerUnavailable ? "Docker isn't available" : "the container did not start";
            Unavailable = $"No Postgres container: {reason} ({e.GetType().Name}: {e.Message})";
        }
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (DataSource is not null)
            await DataSource.DisposeAsync();
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>The data source, or the test is ignored (no Docker) or failed (the container did not start).</summary>
    public static NpgsqlDataSource Require()
    {
        if (DataSource is not null)
            return DataSource;
        if (_dockerUnavailable)
            Assert.Ignore(Unavailable ?? "No Postgres container");
        Assert.Fail(Unavailable ?? "No Postgres container");
        return null!;
    }

    /// <summary>A table name no other test uses.</summary>
    public static string NewTable(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
