# Ananke.Redis — Architecture

> Redis infrastructure — distributed locks, key-value persistence,
> checkpointing, and conversation memory via StackExchange.Redis.

## Role

Provides production-grade implementations of Ananke's infrastructure
abstractions using Redis-compatible stores. Required for distributed
`AbstractStateMachine` deployments and persistent conversation memory.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `RedisDistributedLock` — `IDistributedLock` via the RedLock algorithm, multi-instance safe — `src/Ananke.Redis/RedisDistributedLock.cs`
2. `RedisDataAdapter` — `IKeyValueDataAdapter` for key-value persistence with JSON serialization — `src/Ananke.Redis/RedisDataAdapter.cs`
3. `RedisConversationMemory` — `IConversationMemory` for session-scoped conversation history — `src/Ananke.Redis/RedisConversationMemory.cs`
4. `ServiceCollectionExtensions` — `services.AddRedis(options => ...)` DI registration, the usual starting point for wiring this package in — `src/Ananke.Redis/ServiceCollectionExtensions.cs`

---

## Dependencies

- `Ananke.Abstractions` (project)
- `Ananke.Orchestration` (project)
- `StackExchange.Redis`
- `RedLock.net`

## Key Types

| Type | Kind | Purpose | Source |
|------|------|---------|--------|
| `RedisDistributedLock` | Class | `IDistributedLock` via RedLock algorithm (multi-instance safe) | `src/Ananke.Redis/RedisDistributedLock.cs` |
| `RedisDataAdapter` | Class | `IKeyValueDataAdapter` — key-value persistence with JSON serialization | `src/Ananke.Redis/RedisDataAdapter.cs` |
| `RedisCheckpointStore` | Class | `ICheckpointStore` — workflow checkpoint persistence. Not auto-registered by `AddRedis(...)`; takes a `ConnectionMultiplexer` directly — see README for manual wiring | `src/Ananke.Redis/RedisCheckpointStore.cs` |
| `RedisConversationMemory` | Class | `IConversationMemory` — session-scoped conversation history | `src/Ananke.Redis/RedisConversationMemory.cs` |
| `ServiceCollectionExtensions` | Static class | `services.AddRedis(options => ...)` DI registration | `src/Ananke.Redis/ServiceCollectionExtensions.cs` |

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
