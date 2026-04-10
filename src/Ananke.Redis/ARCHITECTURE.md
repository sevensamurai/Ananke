# Ananke.Redis — Architecture

> Redis infrastructure — distributed locks, key-value persistence,
> checkpointing, and conversation memory via StackExchange.Redis.

## Role

Provides production-grade implementations of Ananke's infrastructure
abstractions using Redis-compatible stores. Required for distributed
`AbstractStateMachine` deployments and persistent conversation memory.

## Dependencies

- `Ananke.Abstractions` (project)
- `Ananke.Orchestration` (project)
- `StackExchange.Redis`
- `RedLock.net`

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `RedisDistributedLock` | Class | `IDistributedLock` via RedLock algorithm (multi-instance safe) |
| `RedisDataAdapter` | Class | `IKeyValueDataAdapter` — key-value persistence with JSON serialization |
| `RedisCheckpointStore` | Class | `ICheckpointStore` — workflow checkpoint persistence |
| `RedisConversationMemory` | Class | `IConversationMemory` — session-scoped conversation history |
| `ServiceCollectionExtensions` | Static class | `services.AddRedis(options => ...)` DI registration |

## Redis API Surface Used

Only basic Redis 2.6 commands — all Redis-compatible stores support these:

| Command | Used By |
|---------|---------|
| `GET` / `SET` | `RedisDataAdapter`, `RedisCheckpointStore` |
| `DEL` / `EXISTS` | `RedisDataAdapter`, `RedisCheckpointStore`, `RedisConversationMemory` |
| `EXPIRE` / `EXPIREAT` | `RedisCheckpointStore`, `RedisConversationMemory` |
| `RPUSH` / `LRANGE` | `RedisConversationMemory` |
| `SET NX EX` (Lua) | `RedisDistributedLock` (via RedLock.net) |

## Compatible Stores

| Store | Compatibility | Persistence | Recommended For |
|-------|--------------|-------------|-----------------|
| **Dragonfly** | ✅ Full (StackExchange.Redis + RedLock) | Snapshots + WAL (enable via `--dbfilename`) | Development, demos |
| **Valkey** | ✅ Full (Redis 7.2 fork) | RDB + AOF (enable via `--appendonly yes`) | Production (OSS, Linux Foundation) |
| **Redis** | ✅ Full | RDB + AOF (off by default) | Production (if licensed) |
| **KeyDB** | ✅ Full | RDB + AOF | Alternative (development slowed) |
| **Kvrocks** | ⚠️ Basic commands OK, RedLock untested | Always durable (RocksDB) | Large datasets exceeding RAM |

Pre-configured docker-compose files are in the repo root:
- `docker-compose.dragonfly.yml` — development default (with persistence)
- `docker-compose.valkey.yml` — production OSS (with AOF)

## DI Registration

```csharp
services.AddRedis(c =>
{
    c.Host = "localhost";  // Points at Dragonfly/Valkey/Redis — same port, same protocol
    c.Port = 6379;
});
```
