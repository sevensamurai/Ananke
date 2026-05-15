namespace Ananke.Graph.Abstractions;

/// <summary>
/// Connection settings shared across all <c>Ananke.Graph.*</c> backend adapters.
/// </summary>
public sealed record GraphConnectionOptions
{
    /// <summary>
    /// Bolt URI of the graph server (e.g. <c>bolt://localhost:7687</c> for Memgraph,
    /// <c>bolt://localhost:7474</c> for Neo4j).
    /// </summary>
    public required string Uri { get; init; }

    /// <summary>Username for authentication. Leave <see langword="null"/> to connect without credentials.</summary>
    public string? Username { get; init; }

    /// <summary>Password for authentication. Leave <see langword="null"/> to connect without credentials.</summary>
    public string? Password { get; init; }

    /// <summary>
    /// Optional name of the target database.  When <see langword="null"/> the driver's
    /// default database is used (Memgraph ignores this field; it is provided for
    /// Neo4j Enterprise multi-database compatibility).
    /// </summary>
    public string? Database { get; init; }

    /// <summary>
    /// Maximum number of idle connections kept in the Bolt connection pool.
    /// Defaults to <c>50</c>.
    /// </summary>
    public int MaxConnectionPoolSize { get; init; } = 50;

    /// <summary>
    /// Maximum lifetime of a pooled connection.  <see langword="null"/> lets the driver
    /// use its own default (typically one hour).
    /// </summary>
    public TimeSpan? MaxConnectionLifetime { get; init; }
}
