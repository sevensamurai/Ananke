using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Tools.Gating;
using ModelContextProtocol.Client;

namespace Ananke.MCP;

/// <summary>
/// Distributes MCP tool calls across a pool of <see cref="McpClient"/> instances,
/// selecting the least-loaded available server per call.
/// </summary>
/// <remarks>
/// <para>
/// Adds to <see cref="ToolKit"/> via <c>ToolKitMcpExtensions.AddMcpPoolAsync</c>.
/// Tools registered this way route their <c>Execute</c> delegate through the pool
/// automatically — the agent sees a normal <see cref="ToolDefinition"/>.
/// </para>
/// <para>
/// Load balancing strategy: round-robin over healthy servers, skipping servers that
/// have exceeded their fault threshold. When all servers are faulted the next one in
/// rotation is used anyway (graceful degradation).
/// </para>
/// <para>
/// Fault signalling: when a server call fails, the invoker reports a
/// <see cref="ToolFaultEvent"/> on the optional <see cref="IToolFaultObserver"/> so
/// health state and affinity are updated automatically.
/// </para>
/// </remarks>
public sealed class McpToolInvoker
{
    // Delegate seam: either wraps a real McpClient or a test double
    private readonly Func<int, string, IReadOnlyDictionary<string, object?>, CancellationToken, Task<ToolResult>> _invoke;
    private readonly IToolFaultObserver? _faultObserver;
    private readonly int _serverCount;
    private readonly int _faultThreshold;

    private readonly Lock _lock = new();
    private readonly int[] _faultCounts;
    private int _nextIndex;

    /// <summary>
    /// Creates a pool invoker backed by real <see cref="McpClient"/> instances.
    /// </summary>
    /// <param name="clients">
    /// Connected MCP clients to pool. Caller owns their lifetime.
    /// At least one client is required.
    /// </param>
    /// <param name="faultObserver">
    /// Optional fault observer. When set, transient call failures are reported
    /// so health state and affinity tracking are updated.
    /// </param>
    /// <param name="faultThreshold">
    /// Number of consecutive failures before a server is temporarily skipped
    /// in round-robin rotation. Defaults to 3. Reset by a successful call.
    /// </param>
    public McpToolInvoker(
        IReadOnlyList<McpClient> clients,
        IToolFaultObserver? faultObserver = null,
        int faultThreshold = 3)
    {
        if (clients is null || clients.Count == 0)
            throw new ArgumentException("At least one MCP client is required.", nameof(clients));

        _faultObserver = faultObserver;
        _faultThreshold = faultThreshold;
        _serverCount = clients.Count;
        _faultCounts = new int[clients.Count];

        _invoke = async (serverIndex, toolName, args, ct) =>
        {
            var mcpArgs = ToMcpArguments(args);
            var result = await clients[serverIndex]
                .CallToolAsync(toolName, mcpArgs, cancellationToken: ct)
                .ConfigureAwait(false);
            var text = ExtractText(result);
            return result.IsError == true ? ToolResult.Error(text) : ToolResult.Ok(text);
        };
    }

    /// <summary>
    /// Internal constructor for testing — accepts a delegate instead of real MCP clients.
    /// </summary>
    internal McpToolInvoker(
        int serverCount,
        Func<int, string, IReadOnlyDictionary<string, object?>, CancellationToken, Task<ToolResult>> invoke,
        IToolFaultObserver? faultObserver = null,
        int faultThreshold = 3)
    {
        if (serverCount <= 0) throw new ArgumentOutOfRangeException(nameof(serverCount));
        _serverCount = serverCount;
        _faultCounts = new int[serverCount];
        _invoke = invoke;
        _faultObserver = faultObserver;
        _faultThreshold = faultThreshold;
    }

    /// <summary>
    /// Invokes <paramref name="toolName"/> on the least-loaded healthy server.
    /// </summary>
    internal async Task<ToolResult> InvokeAsync(
        string kitName,
        string toolName,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken ct)
    {
        var index = PickServer();

        try
        {
            var result = await _invoke(index, toolName, args, ct).ConfigureAwait(false);

            // Success — reset fault count for this server
            lock (_lock)
                _faultCounts[index] = 0;

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_lock)
                _faultCounts[index]++;

            if (_faultObserver is not null)
            {
                await _faultObserver.ReportAsync(
                    new ToolFaultEvent(kitName, toolName, ex.Message,
                        ContractBreak: false, Transient: true), ct)
                    .ConfigureAwait(false);
            }

            return ToolResult.Error($"MCP server error: {ex.Message}");
        }
    }

    /// <summary>Returns current per-server fault counts (snapshot). For diagnostics.</summary>
    public IReadOnlyList<int> FaultCounts
    {
        get { lock (_lock) { return [.. _faultCounts]; } }
    }

    private int PickServer()
    {
        lock (_lock)
        {
            // First pass: find a server below fault threshold starting from _nextIndex
            for (var i = 0; i < _serverCount; i++)
            {
                var idx = (_nextIndex + i) % _serverCount;
                if (_faultCounts[idx] < _faultThreshold)
                {
                    _nextIndex = (idx + 1) % _serverCount;
                    return idx;
                }
            }

            // All faulted — fall back to round-robin (graceful degradation)
            var fallback = _nextIndex;
            _nextIndex = (_nextIndex + 1) % _serverCount;
            return fallback;
        }
    }

    private static IReadOnlyDictionary<string, object?> ToMcpArguments(
        IReadOnlyDictionary<string, object?> args)
    {
        var dict = new Dictionary<string, object?>(args.Count);
        foreach (var (key, value) in args)
            dict[key] = value;
        return dict;
    }

    private static string ExtractText(ModelContextProtocol.Protocol.CallToolResult result)
    {
        if (result.Content is null || result.Content.Count == 0)
            return string.Empty;

        return string.Concat(
            result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>()
                .Select(b => b.Text));
    }
}
