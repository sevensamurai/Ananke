using Npgsql;
using Testcontainers.PostgreSql;

namespace Ananke.Postgres.Tests;

/// <summary>
/// One Postgres with pgvector for the whole test run, in a container. Tests that need it are ignored when Docker isn't
/// available. Each test uses tables of its own, so they don't see each other's rows.
/// </summary>
[SetUpFixture]
public sealed class PostgresDatabase
{
    private static PostgreSqlContainer? _container;

    public static NpgsqlDataSource? DataSource { get; private set; }

    public static string? Unavailable { get; private set; }

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
            await _container.StartAsync().WaitAsync(TimeSpan.FromMinutes(3));
            DataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
        }
        catch (Exception e)
        {
            Unavailable = $"No Postgres container ({e.GetType().Name}: {e.Message})";
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

    /// <summary>The data source, or the test is ignored.</summary>
    public static NpgsqlDataSource Require()
    {
        if (DataSource is null)
            Assert.Ignore(Unavailable ?? "No Postgres container");
        return DataSource!;
    }

    /// <summary>A table name no other test uses.</summary>
    public static string NewTable(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
