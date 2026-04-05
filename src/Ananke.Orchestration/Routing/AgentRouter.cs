using System.Text.Json;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Tools;

namespace Ananke.Orchestration.Routing;

/// <summary>
/// An <see cref="IRouter{TState}"/> that delegates routing decisions to an LLM agent.
/// The model receives a description of the current state and the available route options,
/// then returns the name of the next job to execute.
/// Optionally supports tool-calling so the agent can gather information before deciding.
/// </summary>
/// <remarks>
/// Use <see cref="Workflow.DecideWithAgent{TState}"/> to obtain a <see cref="Builder"/>:
/// <code>
/// .Then("analyze", Workflow.DecideWithAgent&lt;MyState&gt;(model)
///     .WithPrompt(s =&gt; $"Current: {s.Summary}")
///     .WithOptions("validate", "enrich", Workflow.End)
///     .Build())
/// </code>
/// </remarks>
public sealed class AgentRouter<TState> : IRouter<TState>
{
    private readonly IAgentModel _model;
    private readonly Func<TState, string> _promptBuilder;
    private readonly IReadOnlyList<string> _options;
    private readonly string? _systemPrompt;
    private readonly IReadOnlyList<AgentTool>? _tools;
    private readonly IReadOnlyDictionary<string, ToolDefinition>? _toolExecutors;
    private readonly int _maxToolRounds;

    internal AgentRouter(
        IAgentModel model,
        Func<TState, string> promptBuilder,
        IReadOnlyList<string> options,
        string? systemPrompt,
        IReadOnlyList<AgentTool>? tools,
        IReadOnlyDictionary<string, ToolDefinition>? toolExecutors,
        int maxToolRounds)
    {
        _model = model;
        _promptBuilder = promptBuilder;
        _options = options;
        _systemPrompt = systemPrompt;
        _tools = tools;
        _toolExecutors = toolExecutors;
        _maxToolRounds = maxToolRounds;
    }

    public async Task<string> RouteAsync(TState state, CancellationToken ct)
    {
        var optionsList = string.Join(", ", _options);
        var system = _systemPrompt ?? $"""
            You are a workflow routing agent. Based on the current state, decide the next step.
            Available options: {optionsList}
            Respond with ONLY the name of the next step. No explanation, no formatting.
            """;

        var messages = new List<AgentMessage> { AgentMessage.User(_promptBuilder(state)) };

        var request = new AgentRequest
        {
            SystemPrompt = system,
            Messages = messages,
            Tools = _tools
        };

        var response = await _model.GenerateAsync(request, ct);

        var round = 0;
        while (response.RequiresAction && _toolExecutors is not null)
        {
            if (++round > _maxToolRounds)
                break;

            messages.Add(AgentMessage.Assistant(response.Text ?? string.Empty, response.ToolCalls));

            foreach (var call in response.ToolCalls!)
            {
                var args = ParseToolArgs(call.Arguments);
                var toolResult = _toolExecutors.TryGetValue(call.FunctionName, out var exec)
                    ? await exec.ExecuteAsync(args, ct)
                    : ToolResult.Error($"Unknown tool: {call.FunctionName}");
                messages.Add(AgentMessage.ToolResult(call.Id, toolResult.Value));
            }

            request = request with { Messages = messages };
            response = await _model.GenerateAsync(request, ct);
        }

        var choice = response.Text?.Trim() ?? string.Empty;
        return MatchOption(choice, optionsList);
    }

    private string MatchOption(string choice, string optionsList)
    {
        foreach (var option in _options)
        {
            if (string.Equals(option, choice, StringComparison.OrdinalIgnoreCase))
                return option;
        }

        foreach (var option in _options)
        {
            if (choice.Contains(option, StringComparison.OrdinalIgnoreCase))
                return option;
        }

        throw new InvalidOperationException(
            $"Agent router returned '{choice}', which does not match any available option: {optionsList}.");
    }

    private static IReadOnlyDictionary<string, object?> ParseToolArgs(string arguments)
    {
        var dict = new Dictionary<string, object?>();
        using var doc = JsonDocument.Parse(arguments);
        foreach (var prop in doc.RootElement.EnumerateObject())
            dict[prop.Name] = prop.Value.Clone();
        return dict;
    }

    /// <summary>Fluent builder for <see cref="AgentRouter{TState}"/>.</summary>
    public sealed class Builder
    {
        private readonly IAgentModel _model;
        private readonly List<string> _options = [];
        private Func<TState, string>? _promptBuilder;
        private string? _systemPrompt;
        private ToolKit? _toolKit;
        private int _maxToolRounds = 3;

        public Builder(IAgentModel model)
        {
            ArgumentNullException.ThrowIfNull(model);
            _model = model;
        }

        /// <summary>Defines how to describe the current state to the model.</summary>
        public Builder WithPrompt(Func<TState, string> promptBuilder)
        {
            _promptBuilder = promptBuilder;
            return this;
        }

        /// <summary>Overrides the default system prompt used by the router.</summary>
        public Builder WithSystemPrompt(string systemPrompt)
        {
            _systemPrompt = systemPrompt;
            return this;
        }

        /// <summary>Adds available routing options (job names or <see cref="Workflow.End"/>).</summary>
        public Builder WithOptions(params string[] options)
        {
            _options.AddRange(options);
            return this;
        }

        /// <summary>Provides tools the agent can call before making its routing decision.</summary>
        public Builder WithTools(ToolKit toolKit)
        {
            _toolKit = toolKit;
            return this;
        }

        /// <summary>Maximum number of tool-calling rounds before forcing a decision. Default is 3.</summary>
        public Builder WithMaxToolRounds(int max)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);
            _maxToolRounds = max;
            return this;
        }

        public AgentRouter<TState> Build()
        {
            ArgumentNullException.ThrowIfNull(_promptBuilder, "Prompt builder is required. Call WithPrompt().");

            if (_options.Count == 0)
                throw new InvalidOperationException("At least one routing option is required. Call WithOptions().");

            IReadOnlyList<AgentTool>? tools = null;
            IReadOnlyDictionary<string, ToolDefinition>? toolExecutors = null;

            if (_toolKit is not null)
            {
                tools = _toolKit.Tools.Values
                    .Select(t => new AgentTool(t.Name, t.Description, t.ParametersJsonSchema))
                    .ToList();
                toolExecutors = _toolKit.Tools;
            }

            return new AgentRouter<TState>(
                _model, _promptBuilder, _options, _systemPrompt,
                tools, toolExecutors, _maxToolRounds);
        }
    }
}
