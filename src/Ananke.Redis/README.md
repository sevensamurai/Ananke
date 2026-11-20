# Ananke.Redis

[![NuGet](https://img.shields.io/nuget/v/Ananke.Redis.svg)](https://www.nuget.org/packages/Ananke.Redis)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

Redis infrastructure for Ananke — `IDistributedLock` via RedLock.net and `IKeyValueDataAdapter` via StackExchange.Redis.

## Install

```bash
dotnet add package Ananke.Redis
```

## Quick start

```csharp
using Ananke.Redis;

services.AddStateMachine();
services.AddRedis(c =>
{
    c.Host = "localhost";
    c.Port = 6379;
    c.Password = "secret";
    c.LockExpirySeconds = 30;
});
```

Or with simple parameters:

```csharp
services.AddRedis("localhost", port: 6379, password: "secret");
```

Call order with `AddStateMachine()` doesn't matter — `AddRedis` replaces the in-memory fallback automatically.

## Connection

`CacheConfig` holds the connection and how keys are named:

| Field | Use |
|---|---|
| `Host`, `Port`, `Username`, `Password`, `Ssl` | Where and how to connect; `Ssl` turns on TLS |
| `ConnectionString` | A StackExchange.Redis connection string, used instead of the five fields above when set |
| `Database` | Database number for values and locks; `null` uses the connection's default |
| `KeyPrefix` | Put before every value key and every lock name, e.g. `"seren:"` |
| `LockExpirySeconds` | How long the server keeps a lock before releasing it (default 5) |
| `ConnectTimeoutMs`, `SyncTimeoutMs`, `AsyncTimeoutMs` | How long connecting and a command may wait, in milliseconds; `null` keeps the connection string's value or the client's default (5000, async taking the sync value) |

```csharp
services.AddRedis(c =>
{
    c.ConnectionString = "cache.example.com:6380,user=app,password=secret,ssl=true";
    c.KeyPrefix = "seren:";
    c.Database = 2;
});
```

Values and locks share one connection, opened on first use. A host that already has one registers its own
`IConnectionMultiplexer`, before or after `AddRedis`; it is then used for both and no other connection is opened.
The host keeps owning it: disposing the lock does not close it.

```csharp
services.AddSingleton<IConnectionMultiplexer>(await ConnectionMultiplexer.ConnectAsync(connectionString));
services.AddRedis(c => c.KeyPrefix = "seren:");
```

A value key `k` is stored as `{KeyPrefix}k`; a lock on resource `r` is the key `{KeyPrefix}redlock:r`.
`SetValueAsync(key, value, expiry)` sets a value that is gone after `expiry`.

## What it registers

| Service | Implementation |
|---|---|
| `IDistributedLock` | `RedisDistributedLock` (RedLock.net) |
| `IKeyValueDataAdapter` | `RedisDistributedLock` (StackExchange.Redis) |

Both interfaces are backed by the same singleton, which holds the Redis connection and the RedLock factory over it.

## Features

- **Distributed locking** — RedLock algorithm for safe multi-instance coordination
- **Key-value storage** — JSON serialized get/set/remove/exists via StackExchange.Redis
- **DI-friendly** — constructed from `IOptions<CacheConfig>`, or over the host's `IConnectionMultiplexer`; connects lazily on first use
- **Replaces in-memory** — automatically overrides the default `InMemoryDistributedLock`

## Checkpoints and conversation memory

`RedisConversationMemory` is in this package. Workflow checkpoints go through `KeyValueCheckpointStore` in
`Ananke.Orchestration`, over the `IKeyValueDataAdapter` that `AddRedis` registers, so that this package needs
nothing from the workflow engine.

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
