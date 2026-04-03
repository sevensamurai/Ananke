namespace Ananke.Orchestration.Tools;

/// <summary>
/// Fluent builder for registering tools with 2+ parameters, metadata (<see cref="Tags"/>,
/// <see cref="Examples"/>, <see cref="Requires"/>), or cancellation-aware handlers.
/// <para>
/// For the common 0-param and 1-param cases, prefer the convenience overloads on
/// <see cref="ToolKit"/> — they are more concise. Use the builder when you need
/// multiple parameters, per-parameter examples, or tool-level metadata.
/// </para>
/// </summary>
/// <example>
/// <code>
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
/// </code>
/// </example>
public sealed class ToolBuilder
{
    private readonly List<ToolParameter> _params = [];
    private readonly List<string> _tags = [];
    private readonly List<string> _examples = [];
    private readonly List<ToolPrerequisite> _requires = [];
    private Func<ToolArgs, CancellationToken, Task<ToolResult>>? _execute;

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

    internal ToolDefinition Build(string name, string description)
    {
        if (_execute is null)
            throw new InvalidOperationException(
                $"Tool '{name}' has no execute handler. Call OnExecute(...) in the builder.");

        var execute = _execute;

        return new ToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = _params,
            Tags = _tags,
            Examples = _examples,
            Requires = _requires,
            Execute = (args, ct) => execute(new ToolArgs(args), ct)
        };
    }
}
