# Ananke.Redis — Architecture

> Redis infrastructure — distributed locks and key-value persistence
> via StackExchange.Redis and RedLock.net.

## Role

Provides production-grade implementations of Ananke's infrastructure
abstractions using Redis-compatible stores. Required for distributed
`AbstractStateMachine` deployments. Depends on `Ananke.Abstractions` only:
workflow checkpoints go through `KeyValueCheckpointStore` in
`Ananke.Orchestration`, over the key-value adapter `AddRedis` registers.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `RedisDistributedLock` — `IDistributedLock` via the RedLock algorithm, multi-instance safe — `src/Ananke.Redis/RedisDistributedLock.cs`
2. `RedisDataAdapter` — `IKeyValueDataAdapter` for key-value persistence with JSON serialization — `src/Ananke.Redis/RedisDataAdapter.cs`
3. `ServiceCollectionExtensions` — `services.AddRedis(options => ...)` DI registration, the usual starting point for wiring this package in — `src/Ananke.Redis/ServiceCollectionExtensions.cs`

---

## Dependencies

- `Ananke.Abstractions` (project)
- `StackExchange.Redis`
- `RedLock.net`

## Key Types

| Type | Kind | Purpose | Source |
|------|------|---------|--------|
| `RedisDistributedLock` | Class | `IDistributedLock` via RedLock algorithm (multi-instance safe) | `src/Ananke.Redis/RedisDistributedLock.cs` |
| `RedisDataAdapter` | Class | `IKeyValueDataAdapter` — key-value persistence with JSON serialization | `src/Ananke.Redis/RedisDataAdapter.cs` |
| `ServiceCollectionExtensions` | Static class | `services.AddRedis(options => ...)` DI registration | `src/Ananke.Redis/ServiceCollectionExtensions.cs` |

## Redis API Surface Used

Only basic Redis 2.6 commands — all Redis-compatible stores support these:

| Command | Used By |
|---------|---------|
| `GET` / `SET` | `RedisDataAdapter` |
| `DEL` / `EXISTS` | `RedisDataAdapter` |
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

## Timeouts

`CacheConfig.ConnectTimeoutMs`, `SyncTimeoutMs` and `AsyncTimeoutMs` set how long connecting, a synchronous
command and an asynchronous command may wait, in milliseconds. They apply to the one connection that
`RedisDataAdapter` and the lock's RedLock endpoint share, whether it is built from the parts or from
`ConnectionString` (where a value set in `CacheConfig` wins); a connection the host registers keeps its own. `null` keeps the client's defaults (5000 ms, async
taking the sync value), which is how long an operation waits before failing when the server is unreachable. Value
reads and writes are asynchronous; the lock makes synchronous calls. The client checks async timeouts once a
second. While the connection is not connected (a server that rejects the password, or a reconnect in progress) the
lock answers `LockFailed` at once rather than waiting; a server that stays connected but doesn't answer still makes
it wait for these timeouts. The first connect, made on the first operation, is one attempt (unless the connection
string sets `connectRetry`), so it waits at most `ConnectTimeoutMs`; an operation's token ends its own wait for it,
and the connect goes on for the other callers.

```csharp
services.AddRedis(c =>
{
    c.Host = "localhost";
    c.ConnectTimeoutMs = 1000;
    c.SyncTimeoutMs = 1000;
    c.AsyncTimeoutMs = 1000;
});
```
