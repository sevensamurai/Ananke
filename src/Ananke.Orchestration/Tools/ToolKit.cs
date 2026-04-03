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

    public string Name { get; }
    public IReadOnlyDictionary<string, ToolDefinition> Tools => _tools;

    public ToolKit(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    // ── Convenience overloads (0-param) ─────────────────────────────

    /// <summary>Adds a tool with no parameters (sync).</summary>
    public ToolKit AddTool(
        string name,
        string description,
        Func<ToolResult> execute)
    {
        _tools[name] = new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = [],
            Execute = (_, _) => Task.FromResult(execute())
        };
        return this;
    }

    /// <summary>Adds a tool with no parameters (async).</summary>
    public ToolKit AddTool(
        string name,
        string description,
        Func<Task<ToolResult>> execute)
    {
        _tools[name] = new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = [],
            Execute = (_, _) => execute()
        };
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
        _tools[name] = new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = [new(paramName, paramDescription, IsRequired: true)],
            Execute = (args, _) =>
            {
                var arg = new ToolArgs(args).Get(paramName);
                return Task.FromResult(execute(arg));
            }
        };
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
        _tools[name] = new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = [new(paramName, paramDescription, IsRequired: true)],
            Execute = (args, _) =>
            {
                var arg = new ToolArgs(args).Get(paramName);
                return execute(arg);
            }
        };
        return this;
    }

    // ── Convenience overloads (1-param typed) ───────────────────────

    /// <summary>Adds a tool with one typed parameter (sync).</summary>
    public ToolKit AddTool<T>(
        string name,
        string description,
        Func<T, ToolResult> execute,
        string paramName,
        string paramDescription)
    {
        _tools[name] = new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = [new(paramName, paramDescription, ToolArgs.JsonTypeFor(typeof(T)), IsRequired: true)],
            Execute = (args, _) =>
            {
                var arg = new ToolArgs(args).Get<T>(paramName);
                return Task.FromResult(execute(arg));
            }
        };
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
        _tools[name] = new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = [new(paramName, paramDescription, ToolArgs.JsonTypeFor(typeof(T)), IsRequired: true)],
            Execute = (args, _) =>
            {
                var arg = new ToolArgs(args).Get<T>(paramName);
                return execute(arg);
            }
        };
        return this;
    }

    // ── Builder overload (2+ params, metadata, cancellation) ────────

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
        _tools[name] = builder.Build(name, description);
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

        foreach (var (name, tool) in other.Tools)
            _tools[name] = tool;

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
        _tools[tool.Name] = tool;
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
