namespace Ananke.Orchestration.Tools;

/// <summary>
/// Fluent builder for registering tools with 2+ parameters, metadata (<see cref="Tags"/>,
/// <see cref="Examples"/>, <see cref="Requires"/>), execution modes, or cancellation-aware handlers.
/// <para>
/// For the common 0-param and 1-param cases, prefer the convenience overloads on
/// <see cref="ToolKit"/> — they are more concise. Use the builder when you need
/// multiple parameters, per-parameter examples, tool-level metadata, or remote-backed tools.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // Local lambda tool (default)
/// toolkit.AddTool("buy_shares", "Buy shares of a stock", b =&gt; b
///     .Param("symbol", "Ticker symbol", examples: ["AAPL", "MSFT"])
///     .Param&lt;int&gt;("quantity", "Number of shares to buy")
///     .Tags("trading", "market")
///     .OnExecute(async args =&gt;
///     {
///         var symbol = args.Get("symbol");
///         var qty = args.Get&lt;int&gt;("quantity");
///         return ToolResult.Ok($"Bought {qty} shares of {symbol}");
///     }));
///
/// // Callback-backed tool (platform calls your HTTP endpoint)
/// toolkit.AddTool("get_price", "Gets stock price", b =&gt; b
///     .Param("symbol", "Ticker")
///     .Callback(new Uri("https://api.example.com/tools/get_price"))
///     .OnExecute(args =&gt; ToolResult.Ok("42.50")));  // local fallback
///
/// // Platform-native tool (no user code)
/// toolkit.AddTool("code_interpreter", "Run Python code", b =&gt; b
///     .PlatformNative("code_execution"));
/// </code>
/// </example>
public sealed class ToolBuilder
{
    private readonly List<ToolParameter> _params = [];
    private readonly List<string> _tags = [];
    private readonly List<string> _examples = [];
    private readonly List<ToolPrerequisite> _requires = [];
    private Func<ToolArgs, CancellationToken, Task<ToolResult>>? _execute;
    private ToolExecutionMode _executionMode = ToolExecutionMode.Local;
    private ToolEndpoint? _endpoint;
    private string? _platformCapability;

    /// <summary>
    /// Adds a string parameter to the tool.
    /// </summary>
    /// <param name="name">Parameter name (used as the JSON property key).</param>
    /// <param name="description">Human-readable description sent to the LLM.</param>
    /// <param name="required">When <see langword="true"/>, included in the JSON Schema <c>required</c> array.</param>
    /// <param name="examples">Optional sample values to improve LLM accuracy.</param>
    public ToolBuilder Param(string name, string description,
        bool required = true, IReadOnlyList<string>? examples = null)
    {
        _params.Add(new ToolParameter(name, description, IsRequired: required, Examples: examples));
        return this;
    }

    /// <summary>
    /// Adds a typed parameter to the tool. The JSON Schema type is inferred from <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">
    /// The CLR type. Mapped to JSON Schema: <c>int</c>/<c>long</c> → <c>"integer"</c>,
    /// <c>float</c>/<c>double</c>/<c>decimal</c> → <c>"number"</c>,
    /// <c>bool</c> → <c>"boolean"</c>, everything else → <c>"string"</c>.
    /// </typeparam>
    /// <param name="name">Parameter name (used as the JSON property key).</param>
    /// <param name="description">Human-readable description sent to the LLM.</param>
    /// <param name="required">When <see langword="true"/>, included in the JSON Schema <c>required</c> array.</param>
    /// <param name="examples">Optional sample values to improve LLM accuracy.</param>
    public ToolBuilder Param<T>(string name, string description,
        bool required = true, IReadOnlyList<string>? examples = null)
    {
        _params.Add(new ToolParameter(name, description, ToolArgs.JsonTypeFor(typeof(T)),
            IsRequired: required, Examples: examples));
        return this;
    }

    /// <summary>
    /// Adds categorisation tags for discovery and A2A skill mapping.
    /// </summary>
    public ToolBuilder Tags(params string[] tags)
    {
        _tags.AddRange(tags);
        return this;
    }

    /// <summary>
    /// Adds usage examples included in the tool description sent to the LLM.
    /// </summary>
    public ToolBuilder Examples(params string[] examples)
    {
        _examples.AddRange(examples);
        return this;
    }

    /// <summary>
    /// Declares runtime prerequisites validated by <see cref="ToolKit.CheckPrerequisitesAsync"/>.
    /// </summary>
    public ToolBuilder Requires(params ToolPrerequisite[] prerequisites)
    {
        _requires.AddRange(prerequisites);
        return this;
    }

    // ── Execution mode setters ──────────────────────────────────────

    /// <summary>
    /// Marks this tool as callback-backed: the platform POSTs tool calls to
    /// <paramref name="callbackUri"/>. Pair with <see cref="OnExecute(Func{ToolArgs, ToolResult})"/>
    /// for a local fallback used in local/hybrid mode.
    /// </summary>
    /// <param name="callbackUri">HTTP endpoint the platform calls to execute this tool.</param>
    /// <param name="authHeader">Optional auth header name (e.g. <c>"Authorization"</c>). Value resolved at deploy time.</param>
    /// <param name="verifyReachable">
    /// When <see langword="true"/> (default), adds a <see cref="ToolPrerequisite.Endpoint"/>
    /// check so <see cref="ToolKit.CheckPrerequisitesAsync"/> fails fast if the endpoint is down.
    /// </param>
    public ToolBuilder Callback(Uri callbackUri, string? authHeader = null, bool verifyReachable = true)
    {
        ArgumentNullException.ThrowIfNull(callbackUri);
        _executionMode = ToolExecutionMode.Callback;
        _endpoint = new ToolEndpoint { Uri = callbackUri, AuthHeader = authHeader };
        if (verifyReachable)
            _requires.Add(ToolPrerequisite.Endpoint(callbackUri,
                $"Callback endpoint unreachable: {callbackUri}"));
        return this;
    }

    /// <summary>
    /// Marks this tool as MCP-backed: execution is delegated to an MCP server at
    /// <paramref name="serverUri"/>. When used with <c>AddMcpServerToolsAsync</c>,
    /// the <c>Execute</c> delegate calls the MCP client automatically.
    /// </summary>
    /// <param name="serverUri">MCP server URI (e.g. <c>http://localhost:3000/mcp</c>).</param>
    /// <param name="authHeader">Optional auth header name. Value resolved at deploy time.</param>
    /// <param name="verifyReachable">
    /// When <see langword="true"/> (default), adds a <see cref="ToolPrerequisite.Endpoint"/>
    /// check so <see cref="ToolKit.CheckPrerequisitesAsync"/> fails fast if the server is down.
    /// </param>
    public ToolBuilder Mcp(Uri serverUri, string? authHeader = null, bool verifyReachable = true)
    {
        ArgumentNullException.ThrowIfNull(serverUri);
        _executionMode = ToolExecutionMode.Mcp;
        _endpoint = new ToolEndpoint { Uri = serverUri, AuthHeader = authHeader };
        if (verifyReachable)
            _requires.Add(ToolPrerequisite.Endpoint(serverUri,
                $"MCP server unreachable: {serverUri}"));
        return this;
    }

    /// <summary>
    /// Marks this tool as OpenAPI-backed: the platform reads the spec at
    /// <paramref name="specUri"/> and calls the described API directly.
    /// </summary>
    /// <param name="specUri">URL of the OpenAPI specification document.</param>
    /// <param name="authHeader">Optional auth header name. Value resolved at deploy time.</param>
    /// <param name="verifyReachable">
    /// When <see langword="true"/> (default), adds a <see cref="ToolPrerequisite.Endpoint"/>
    /// check so <see cref="ToolKit.CheckPrerequisitesAsync"/> fails fast if the spec URL is unreachable.
    /// </param>
    public ToolBuilder OpenApi(Uri specUri, string? authHeader = null, bool verifyReachable = true)
    {
        ArgumentNullException.ThrowIfNull(specUri);
        _executionMode = ToolExecutionMode.OpenApi;
        _endpoint = new ToolEndpoint { Uri = specUri, AuthHeader = authHeader };
        if (verifyReachable)
            _requires.Add(ToolPrerequisite.Endpoint(specUri,
                $"OpenAPI spec unreachable: {specUri}"));
        return this;
    }

    /// <summary>
    /// Marks this tool as a platform-native capability that requires no user code
    /// or endpoint (e.g. <c>"code_execution"</c>, <c>"web_search"</c>,
    /// <c>"vertex_extension:code_interpreter"</c>).
    /// </summary>
    /// <param name="capability">Platform capability identifier.</param>
    public ToolBuilder PlatformNative(string capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        _executionMode = ToolExecutionMode.PlatformNative;
        _platformCapability = capability;
        return this;
    }

    // ── Execute handlers ────────────────────────────────────────────

    /// <summary>
    /// Sets a synchronous execute handler.
    /// </summary>
    public ToolBuilder OnExecute(Func<ToolArgs, ToolResult> handler)
    {
        _execute = (args, _) => Task.FromResult(handler(args));
        return this;
    }

    /// <summary>
    /// Sets an asynchronous execute handler.
    /// </summary>
    public ToolBuilder OnExecute(Func<ToolArgs, Task<ToolResult>> handler)
    {
        _execute = (args, _) => handler(args);
        return this;
    }

    /// <summary>
    /// Sets an asynchronous execute handler with cancellation support.
    /// </summary>
    public ToolBuilder OnExecute(Func<ToolArgs, CancellationToken, Task<ToolResult>> handler)
    {
        _execute = handler;
        return this;
    }

    internal ToolDefinition Build(string name, string description,
        IToolExecutorStrategy? executorStrategy = null)
    {
        if (_execute is null && _executionMode == ToolExecutionMode.Local)
            throw new InvalidOperationException(
                $"Tool '{name}' has no execute handler. Call OnExecute(...) in the builder.");

        // For remote-backed tools without a local Execute, delegate to the registered
        // IToolExecutorStrategy (defaults to NullToolExecutorStrategy which returns a
        // descriptive error, preserving pre-existing behaviour).
        var strategy = executorStrategy ?? NullToolExecutorStrategy.Instance;

        var definition = new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = _params,
            Tags = _tags,
            Examples = _examples,
            Requires = _requires,
            ExecutionMode = _executionMode,
            Endpoint = _endpoint,
            PlatformCapability = _platformCapability,
            Execute = null! // set below
        };

        Func<IReadOnlyDictionary<string, object?>, CancellationToken, Task<ToolResult>> execute =
            _execute is not null
                ? (args, ct) => _execute(new ToolArgs(args), ct)
                : (args, ct) => strategy.DispatchAsync(definition, args, ct);

        definition = definition with { Execute = execute };
        return definition;
    }
}
