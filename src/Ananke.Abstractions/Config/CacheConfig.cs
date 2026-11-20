namespace Ananke.Abstractions.Config;

/// <summary>
/// Connection and naming settings for a cache-backed <c>IKeyValueDataAdapter</c> and <c>IDistributedLock</c>
/// (Redis in <c>Ananke.Redis</c>).
/// </summary>
public class CacheConfig
{
    /// <summary>Server host name or address. Ignored when <see cref="ConnectionString"/> is set.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>Server port. Ignored when <see cref="ConnectionString"/> is set.</summary>
    public int Port { get; set; } = 6379;

    /// <summary>User name for servers with access control lists. Ignored when <see cref="ConnectionString"/> is set.</summary>
    public string? Username { get; set; }

    /// <summary>Password. Ignored when <see cref="ConnectionString"/> is set.</summary>
    public string? Password { get; set; }

    /// <summary>Connects over TLS. Ignored when <see cref="ConnectionString"/> is set.</summary>
    public bool Ssl { get; set; }

    /// <summary>
    /// A client connection string (for Redis, the StackExchange.Redis format, e.g.
    /// <c>"cache.example.com:6380,user=app,password=…,ssl=true"</c>). When set, it is used instead of
    /// <see cref="Host"/>, <see cref="Port"/>, <see cref="Username"/>, <see cref="Password"/> and <see cref="Ssl"/>.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Database number for values and locks. <c>null</c> uses the connection's default database (0 unless the
    /// connection string names another).
    /// </summary>
    public int? Database { get; set; }

    /// <summary>
    /// Prefix put before every value key and every lock name, e.g. <c>"seren:"</c>, so that several products or
    /// machine types can share one server. <c>null</c> or empty for none.
    /// </summary>
    public string? KeyPrefix { get; set; }

    /// <summary>How long a lock is held before the server releases it, in seconds. <c>null</c> for the default.</summary>
    public int? LockExpirySeconds { get; set; }

    /// <summary>
    /// How long connecting to the server may take, in milliseconds, greater than zero. For Redis it bounds the first
    /// connect, made on the first operation in one attempt (unless the connection string sets <c>connectRetry</c>);
    /// that operation's token ends its wait sooner. <c>null</c> keeps the client's default (for Redis, 5000) or the
    /// value the connection string sets.
    /// </summary>
    public int? ConnectTimeoutMs { get; set; }

    /// <summary>
    /// How long a synchronous command may wait for its answer, in milliseconds, greater than zero. The lock makes
    /// synchronous calls. <c>null</c> keeps the client's default (for Redis, 5000) or the value the connection string
    /// sets.
    /// </summary>
    public int? SyncTimeoutMs { get; set; }

    /// <summary>
    /// How long an asynchronous command (every value read and write) may wait for its answer, in milliseconds,
    /// greater than zero. <c>null</c> keeps the client's default (for Redis, the sync timeout) or the value the connection string sets.
    /// The Redis client checks it once a second, so a command can wait up to a second longer.
    /// </summary>
    public int? AsyncTimeoutMs { get; set; }
}
