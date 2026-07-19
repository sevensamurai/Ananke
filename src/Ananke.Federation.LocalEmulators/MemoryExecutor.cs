using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Ananke.Orchestration.Tools;
using Ananke.Federation.Execution;

namespace Ananke.Federation.LocalEmulators;

/// <summary>
/// Real emulator for the <c>memory</c>, <c>memory_bank</c>,
/// <c>memory_profiles</c>, and <c>memory_search</c> capabilities
/// (Claude, Vertex AI / Gemini Enterprise, Foundry).
/// Backed by an in-process concurrent dictionary — designed for local
/// design-loop sessions and tests. State is not persisted across process restarts.
/// </summary>
/// <remarks>
/// A single <see cref="MemoryExecutor"/> instance can be registered under
/// multiple capability names via <see cref="CreateAll"/>; all share the
/// same underlying store.
/// </remarks>
internal sealed class MemoryExecutor : IPlatformNativeExecutor
{
    // key → JSON-serialized value
    private readonly ConcurrentDictionary<string, string> _store;
    private readonly string _capability;

    private MemoryExecutor(string capability, ConcurrentDictionary<string, string> store)
    {
        _capability = capability;
        _store = store;
    }

    /// <summary>Creates a new <see cref="MemoryExecutor"/> set sharing a single underlying store.</summary>
    public static IReadOnlyList<MemoryExecutor> CreateAll()
    {
        var store = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return
        [
            new("memory",          store),
            new("memory_bank",     store),
            new("memory_profiles", store),
            new("memory_search",   store)
        ];
    }

    public string Capability => _capability;
    public bool IsStub => false;

    public Task<ToolResult> ExecuteAsync(IReadOnlyDictionary<string, object?> args, CancellationToken ct = default)
    {
        var operation = args.TryGetValue("operation", out var op) ? op?.ToString() ?? "recall" : "recall";

        return operation.ToLowerInvariant() switch
        {
            "store" or "save" or "write" => Store(args),
            "recall" or "read" or "fetch" => Recall(args),
            "delete" or "remove" => Delete(args),
            "list" => ListKeys(),
            "search" => Search(args),
            _ => Task.FromResult(ToolResult.Fatal($"Unknown memory operation '{operation}'. " +
                                                  "Supported: store, recall, delete, list, search"))
        };
    }

    private Task<ToolResult> Store(IReadOnlyDictionary<string, object?> args)
    {
        if (!args.TryGetValue("key", out var keyVal) || keyVal is null)
            return Task.FromResult(ToolResult.Fatal("Missing required argument: key"));
        if (!args.TryGetValue("value", out var valVal) || valVal is null)
            return Task.FromResult(ToolResult.Fatal("Missing required argument: value"));

        var key = keyVal.ToString()!;
        var value = valVal is string s ? s : JsonSerializer.Serialize(valVal);
        _store[key] = value;
        return Task.FromResult(ToolResult.Ok($"Stored '{key}'"));
    }

    private Task<ToolResult> Recall(IReadOnlyDictionary<string, object?> args)
    {
        if (!args.TryGetValue("key", out var keyVal) || keyVal is null)
            return Task.FromResult(ToolResult.Fatal("Missing required argument: key"));

        var key = keyVal.ToString()!;
        return _store.TryGetValue(key, out var value)
            ? Task.FromResult(ToolResult.Ok(value))
            : Task.FromResult(ToolResult.Ok($"No memory found for key '{key}'"));
    }

    private Task<ToolResult> Delete(IReadOnlyDictionary<string, object?> args)
    {
        if (!args.TryGetValue("key", out var keyVal) || keyVal is null)
            return Task.FromResult(ToolResult.Fatal("Missing required argument: key"));

        var key = keyVal.ToString()!;
        _store.TryRemove(key, out _);
        return Task.FromResult(ToolResult.Ok($"Deleted '{key}'"));
    }

    private Task<ToolResult> ListKeys()
    {
        var keys = string.Join("\n", _store.Keys.OrderBy(k => k));
        return Task.FromResult(ToolResult.Ok(
            _store.IsEmpty ? "Memory is empty." : $"Stored keys:\n{keys}"));
    }

    private Task<ToolResult> Search(IReadOnlyDictionary<string, object?> args)
    {
        if (!args.TryGetValue("query", out var queryVal) || queryVal is null)
            return Task.FromResult(ToolResult.Fatal("Missing required argument: query"));

        var query = queryVal.ToString()!;
        var sb = new StringBuilder();

        foreach (var (key, value) in _store)
        {
            if (key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                value.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine($"• {key}: {value}");
            }
        }

        return Task.FromResult(ToolResult.Ok(
            sb.Length == 0 ? $"No memory entries matched '{query}'." : sb.ToString().TrimEnd()));
    }
}
