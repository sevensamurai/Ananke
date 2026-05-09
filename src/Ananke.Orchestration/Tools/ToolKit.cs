using Ananke.Orchestration.Workflows;
using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tools.Routing;
using Ananke.Orchestration.Tools.Gating;

namespace Ananke.Orchestration.Tools;

/// <summary>
/// A named collection of <see cref="ToolDefinition"/> instances made available to an
/// <c>AgentJob</c> for tool-calling workflows. Build a kit once and share it across agents.
/// </summary>
/// <remarks>
/// <para><b>Quick-add (0 or 1 parameter):</b></para>
/// <code>
/// var kit = new ToolKit("stock")
///     .AddTool("ping", "Returns pong", () =&gt; "pong")
///     .AddTool("get_price", "Gets price", GetPrice, "symbol", "Ticker");
/// </code>
/// <para><b>Builder (2+ parameters, metadata, cancellation):</b></para>
/// <code>
/// kit.AddTool("buy", "Buys shares", b =&gt; b
///     .Param("symbol", "Ticker", examples: ["AAPL", "MSFT"])
///     .Param&lt;int&gt;("quantity", "Shares to buy")
///     .Tags("trading")
///     .OnExecute(async args =&gt; ToolResult.Ok(
///         $"Bought {args.Get&lt;int&gt;("quantity")} {args.Get("symbol")}")));
/// </code>
/// </remarks>
public sealed class ToolKit
{
    private readonly Dictionary<string, ToolDefinition> _tools = [];
    private readonly HashSet<string> _pinnedToolNames = [];
    private IToolMemory? _memory;
    private IToolFaultObserver? _faultObserver;
    private ISmartToolRouter? _router;

    public string Name { get; }
    public IReadOnlyDictionary<string, ToolDefinition> Tools => _tools;

    /// <summary>The <see cref="IToolMemory"/> registered on this kit, or <see langword="null"/> if none.</summary>
    public IToolMemory? Memory => _memory;

    /// <summary>
    /// The <see cref="ISmartToolRouter"/> registered on this kit, or <see langword="null"/> if none.
    /// When set, <c>SmartToolRouterMiddleware</c> uses this router to narrow
    /// the tool window before each model turn.
    /// </summary>
    public ISmartToolRouter? Router => _router;

    /// <summary>
    /// Tool names that are always included in the routing window regardless of semantic relevance
    /// (autonomic reflexes — e.g. <c>list_tools</c>, <c>help</c>).
    /// </summary>
    public IReadOnlySet<string> PinnedToolNames => _pinnedToolNames;

    public ToolKit(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>
    /// Registers an <see cref="IToolMemory"/> with this kit.
    /// Once registered, every subsequent <c>AddTool</c> call automatically
    /// upserts the tool into the memory so the thalamic gate can recall it.
    /// </summary>
    /// <remarks>
    /// Call <see cref="PopulateMemoryAsync"/> to back-fill entries for tools
    /// that were registered before this call.
    /// </remarks>
    /// <returns>This <see cref="ToolKit"/> for fluent chaining.</returns>
    public ToolKit WithMemory(IToolMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        _memory = memory;
        return this;
    }

    /// <summary>
    /// Registers an <see cref="ISmartToolRouter"/> with this kit.
    /// When set, <c>SmartToolRouterMiddleware</c> will use this router
    /// to narrow the tool window before each model turn.
    /// </summary>
    /// <returns>This <see cref="ToolKit"/> for fluent chaining.</returns>
    public ToolKit WithRouter(ISmartToolRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        _router = router;
        return this;
    }

    /// <summary>
    /// Marks the named tool as always-on: the router will include it in every turn
    /// regardless of semantic relevance — analogous to autonomic reflexes
    /// (e.g. pin <c>list_tools</c> or <c>help</c>).
    /// </summary>
    /// <remarks>
    /// If the tool is not yet registered in this kit, the name is still recorded
    /// and will be honoured once the tool is added.
    /// </remarks>
    /// <returns>This <see cref="ToolKit"/> for fluent chaining.</returns>
    public ToolKit PinTool(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        _pinnedToolNames.Add(toolName);
        return this;
    }

    /// <summary>
    /// Registers an <see cref="IToolFaultObserver"/> with this kit.
    /// Once registered, every tool's <c>Execute</c> delegate is wrapped so that
    /// <see cref="ToolResult.Fatal"/> results and non-retryable errors automatically
    /// report a <see cref="ToolFaultEvent"/>.
    /// </summary>
    /// <remarks>
    /// Tools registered <em>before</em> this call are also wrapped retroactively.
    /// Call this before <see cref="PopulateMemoryAsync"/> for the cleanest setup.
    /// </remarks>
    /// <returns>This <see cref="ToolKit"/> for fluent chaining.</returns>
    public ToolKit WithFaultObserver(IToolFaultObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _faultObserver = observer;

        // Wrap tools that were registered before this call
        foreach (var key in _tools.Keys.ToList())
            _tools[key] = WrapWithFaultObserver(_tools[key]);

        return this;
    }

    /// <summary>
    /// Upserts all currently registered tools into <see cref="Memory"/>.
    /// Use this to back-fill the memory after calling <see cref="WithMemory"/> on
    /// a kit that was already populated with tools.
    /// A no-op when no memory is registered.
    /// </summary>
    public async Task PopulateMemoryAsync(CancellationToken ct = default)
    {
        if (_memory is null) return;
        foreach (var tool in _tools.Values)
            await UpsertToolMemoryAsync(tool, ct).ConfigureAwait(false);
    }

    // ── Convenience overloads (0-param) ─────────────────────────────

    /// <summary>Adds a tool with no parameters (sync).</summary>
    public ToolKit AddTool(
        string name,
        string description,
        Func<ToolResult> execute)
    {
        RegisterTool(new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = [],
            Execute = (_, _) => Task.FromResult(execute())
        });
        return this;
    }

    /// <summary>Adds a tool with no parameters (async).</summary>
    public ToolKit AddTool(
        string name,
        string description,
        Func<Task<ToolResult>> execute)
    {
        RegisterTool(new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = [],
            Execute = (_, _) => execute()
        });
        return this;
    }

    // ── Convenience overloads (1-param string) ──────────────────────

    /// <summary>Adds a tool with one string parameter (sync).</summary>
    public ToolKit AddTool(
        string name,
        string description,
        Func<string, ToolResult> execute,
        string paramName,
        string paramDescription)
    {
        RegisterTool(new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = [new(paramName, paramDescription, IsRequired: true)],
            Execute = (args, _) =>
            {
                var arg = new ToolArgs(args).Get(paramName);
                return Task.FromResult(execute(arg));
            }
        });
        return this;
    }

    /// <summary>Adds a tool with one string parameter (async).</summary>
    public ToolKit AddTool(
        string name,
        string description,
        Func<string, Task<ToolResult>> execute,
        string paramName,
        string paramDescription)
    {
        RegisterTool(new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = [new(paramName, paramDescription, IsRequired: true)],
            Execute = (args, _) =>
            {
                var arg = new ToolArgs(args).Get(paramName);
                return execute(arg);
            }
        });
        return this;
    }

    // ── Convenience overloads (1-param typed)

    /// <summary>Adds a tool with one typed parameter (sync).</summary>
    public ToolKit AddTool<T>(
        string name,
        string description,
        Func<T, ToolResult> execute,
        string paramName,
        string paramDescription)
    {
        RegisterTool(new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = [new(paramName, paramDescription, ToolArgs.JsonTypeFor(typeof(T)), IsRequired: true)],
            Execute = (args, _) =>
            {
                var arg = new ToolArgs(args).Get<T>(paramName);
                return Task.FromResult(execute(arg));
            }
        });
        return this;
    }

    /// <summary>Adds a tool with one typed parameter (async).</summary>
    public ToolKit AddTool<T>(
        string name,
        string description,
        Func<T, Task<ToolResult>> execute,
        string paramName,
        string paramDescription)
    {
        RegisterTool(new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = [new(paramName, paramDescription, ToolArgs.JsonTypeFor(typeof(T)), IsRequired: true)],
            Execute = (args, _) =>
            {
                var arg = new ToolArgs(args).Get<T>(paramName);
                return execute(arg);
            }
        });
        return this;
    }

    // ── Builder overload

    /// <summary>
    /// Adds a tool configured via a <see cref="ToolBuilder"/>. Use this for tools with
    /// 2+ parameters, per-parameter examples, tags, prerequisites, or cancellation support.
    /// </summary>
    /// <param name="name">Tool name (used by the LLM to invoke it).</param>
    /// <param name="description">Human-readable description sent to the LLM.</param>
    /// <param name="configure">Builder callback — call <c>Param</c>, <c>OnExecute</c>, etc.</param>
    public ToolKit AddTool(string name, string description, Action<ToolBuilder> configure)
    {
        var builder = new ToolBuilder();
        configure(builder);
        RegisterTool(builder.Build(name, description));
        return this;
    }

    /// <summary>
    /// Copies all tools from <paramref name="other"/> into this kit.
    /// If both kits contain a tool with the same name, the tool from <paramref name="other"/> wins.
    /// </summary>
    /// <returns>This <see cref="ToolKit"/> for fluent chaining.</returns>
    public ToolKit Merge(ToolKit other)
    {
        ArgumentNullException.ThrowIfNull(other);

        foreach (var (_, tool) in other.Tools)
            RegisterTool(tool);

        return this;
    }

    /// <summary>
    /// Registers a pre-built <see cref="ToolDefinition"/> directly.
    /// Use this when the tool is created externally (e.g. bridged from an MCP server).
    /// </summary>
    /// <returns>This <see cref="ToolKit"/> for fluent chaining.</returns>
    public ToolKit AddTool(ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        RegisterTool(tool);
        return this;
    }

    /// <summary>
    /// Checks all <see cref="ToolPrerequisite"/> entries declared by tools in this kit.
    /// Returns a result indicating which prerequisites passed and which failed.
    /// Call at startup (e.g. after <see cref="Workflow{TState}.Validate"/>) to fail fast
    /// before any agent tries to invoke a tool with missing dependencies.
    /// </summary>
    /// <example>
    /// <code>
    /// var result = await toolkit.CheckPrerequisitesAsync();
    /// if (!result.IsSuccess)
    ///     throw new InvalidOperationException(result.Summary);
    /// </code>
    /// </example>
    private Task UpsertToolMemoryAsync(ToolDefinition tool, CancellationToken ct)
    {
        var entry = new ToolMemoryEntry
        {
            ToolName = tool.Name,
            KitName = Name,
            Description = tool.Description,
            Tags = tool.Tags
        };
        return _memory!.UpsertAsync(entry, ct);
    }

    /// <summary>
    /// Replaces the <c>Execute</c> delegate of an existing tool by name.
    /// Preserves all other tool metadata (description, parameters, mode, capability, etc.).
    /// No-op when no tool with <paramref name="toolName"/> is registered.
    /// </summary>
    /// <param name="toolName">Name of the tool whose executor should be replaced.</param>
    /// <param name="newExecute">New async execute delegate.</param>
    /// <returns>
    /// <see langword="true"/> when the tool was found and patched;
    /// <see langword="false"/> when no tool with the given name is registered.
    /// </returns>
    public bool ReplaceExecutor(
        string toolName,
        Func<IReadOnlyDictionary<string, object?>, CancellationToken, Task<ToolResult>> newExecute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(newExecute);

        if (!_tools.TryGetValue(toolName, out var existing))
            return false;

        var patched = existing with { Execute = newExecute };
        _tools[toolName] = _faultObserver is not null ? WrapWithFaultObserver(patched) : patched;
        return true;
    }

    /// <summary>Stores a tool, wrapping it with the fault observer if one is registered.</summary>
    private void RegisterTool(ToolDefinition def)
    {
        _tools[def.Name] = _faultObserver is not null ? WrapWithFaultObserver(def) : def;
        if (_memory is not null)
            _ = UpsertToolMemoryAsync(def, CancellationToken.None);
    }

    private ToolDefinition WrapWithFaultObserver(ToolDefinition tool)
    {
        var observer = _faultObserver!;
        var kitName = Name;
        var original = tool.Execute;

        return tool with
        {
            Execute = async (args, ct) =>
            {
                var result = await original(args, ct).ConfigureAwait(false);

                if (result.IsError && !result.IsRetryable)
                {
                    var fault = new Gating.ToolFaultEvent(
                        KitName: kitName,
                        ToolName: tool.Name,
                        Reason: result.Value,
                        ContractBreak: true,
                        Transient: false);
                    await observer.ReportAsync(fault, ct).ConfigureAwait(false);
                }
                else if (result.IsError && result.IsRetryable)
                {
                    var fault = new Gating.ToolFaultEvent(
                        KitName: kitName,
                        ToolName: tool.Name,
                        Reason: result.Value,
                        ContractBreak: false,
                        Transient: true);
                    await observer.ReportAsync(fault, ct).ConfigureAwait(false);
                }

                return result;
            }
        };
    }

    public async Task<PrerequisiteCheckResult> CheckPrerequisitesAsync(CancellationToken ct = default)
    {
        var checked_ = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var passed = new List<string>();
        var failures = new List<PrerequisiteFailure>();

        foreach (var tool in _tools.Values)
        {
            foreach (var req in tool.Requires)
            {
                if (!checked_.Add(req.Name))
                    continue;

                var ok = await req.Check(ct).ConfigureAwait(false);
                if (ok)
                    passed.Add(req.Name);
                else
                    failures.Add(new PrerequisiteFailure(req.Name, tool.Name, req.InstallHint));
            }
        }

        return new PrerequisiteCheckResult(passed, failures);
    }
}

/// <summary>
/// Describes a single prerequisite check failure — which binary is missing,
/// which tool needs it, and how to install it.
/// </summary>
public sealed record PrerequisiteFailure(string Prerequisite, string ToolName, string InstallHint);

/// <summary>
/// The outcome of <see cref="ToolKit.CheckPrerequisitesAsync"/>. Contains the list of
/// passed and failed prerequisites, plus a human-readable <see cref="Summary"/>.
/// </summary>
public sealed record PrerequisiteCheckResult(
    IReadOnlyList<string> Passed,
    IReadOnlyList<PrerequisiteFailure> Failures)
{
    public bool IsSuccess => Failures.Count == 0;

    /// <summary>
    /// A multi-line summary suitable for logging or exception messages.
    /// Lists every missing prerequisite with its install hint.
    /// </summary>
    public string Summary
    {
        get
        {
            if (IsSuccess)
                return $"All prerequisites satisfied ({Passed.Count} checked).";

            var lines = Failures.Select(f =>
                $"  ✗ '{f.Prerequisite}' required by tool '{f.ToolName}' — {f.InstallHint}");
            return $"Missing prerequisites:\n{string.Join("\n", lines)}";
        }
    }
}
