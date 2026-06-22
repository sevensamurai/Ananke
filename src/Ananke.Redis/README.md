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

## What it registers

| Service | Implementation |
|---|---|
| `IDistributedLock` | `RedisDistributedLock` (RedLock.net) |
| `IKeyValueDataAdapter` | `RedisDistributedLock` (StackExchange.Redis) |

Both interfaces are backed by the same singleton, which manages the Redis connection and RedLock factory.

## Features

- **Distributed locking** — RedLock algorithm for safe multi-instance coordination
- **Key-value storage** — JSON serialized get/set/remove/exists via StackExchange.Redis
- **Conversation memory** — `RedisConversationMemory` for persistent chat history
- **DI-friendly** — constructed from `IOptions<CacheConfig>`; connects lazily on first use
- **Replaces in-memory** — automatically overrides the default `InMemoryDistributedLock`

## `ICheckpointStore` — manual wiring

`RedisCheckpointStore` is a Redis-backed `ICheckpointStore` (JSON-serialized checkpoints,
native `EXPIREAT` TTL). Unlike `RedisDistributedLock`/`RedisDataAdapter`, it is **not**
registered automatically by `AddRedis(...)` — it takes a `ConnectionMultiplexer` directly
and must be registered separately:

```csharp
services.AddSingleton<ICheckpointStore>(sp =>
    new RedisCheckpointStore(ConnectionMultiplexer.Connect("localhost:6379")));
```

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
