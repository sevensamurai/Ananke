using A2A;
using Ananke.Orchestration.Tools;

namespace Ananke.A2A.Server;

/// <summary>
/// Fluent builder for creating an A2A <see cref="AgentCard"/> from Ananke workflow metadata.
/// </summary>
/// <example>
/// <code>
/// var card = new AgentCardBuilder()
///     .WithName("Research Agent")
///     .WithDescription("Performs research tasks")
///     .WithVersion("1.0.0")
///     .WithSkillsFrom(toolkit)
///     .WithStreamingSupport()
///     .Build("https://myagent.example.com/a2a");
/// </code>
/// </example>
public sealed class AgentCardBuilder
{
    private string _name = "Ananke Agent";
    private string _description = "An agent powered by Ananke";
    private string _version = "1.0.0";
    private readonly List<AgentSkill> _skills = [];
    private bool _streaming;
    private bool _pushNotifications;
    private List<string> _inputModes = ["text/plain"];
    private List<string> _outputModes = ["text/plain"];

    /// <summary>Sets the agent's display name.</summary>
    public AgentCardBuilder WithName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        return this;
    }

    /// <summary>Sets a human-readable description of the agent.</summary>
    public AgentCardBuilder WithDescription(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        _description = description;
        return this;
    }

    /// <summary>Sets the agent version string.</summary>
    public AgentCardBuilder WithVersion(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        _version = version;
        return this;
    }

    /// <summary>
    /// Adds A2A skills derived from an Ananke <see cref="ToolKit"/>.
    /// Each <see cref="ToolDefinition"/> in the kit becomes an <see cref="AgentSkill"/>.
    /// </summary>
    public AgentCardBuilder WithSkillsFrom(ToolKit toolkit)
    {
        ArgumentNullException.ThrowIfNull(toolkit);

        foreach (var tool in toolkit.Tools.Values)
        {
            _skills.Add(new AgentSkill
            {
                Id = tool.Name,
                Name = tool.Name,
                Description = tool.Description,
                Tags = tool.Tags is { Count: > 0 } ? [.. tool.Tags] : [toolkit.Name],
                Examples = tool.Examples is { Count: > 0 } ? [.. tool.Examples] : null
            });
        }

        return this;
    }

    /// <summary>Adds a custom <see cref="AgentSkill"/> directly.</summary>
    public AgentCardBuilder WithSkill(AgentSkill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        _skills.Add(skill);
        return this;
    }

    /// <summary>Declares that the agent supports streaming responses.</summary>
    public AgentCardBuilder WithStreamingSupport()
    {
        _streaming = true;
        return this;
    }

    /// <summary>Declares that the agent supports push notifications.</summary>
    public AgentCardBuilder WithPushNotificationSupport()
    {
        _pushNotifications = true;
        return this;
    }

    /// <summary>Sets the accepted input media types.</summary>
    public AgentCardBuilder WithInputModes(params string[] modes)
    {
        _inputModes = [.. modes];
        return this;
    }

    /// <summary>Sets the produced output media types.</summary>
    public AgentCardBuilder WithOutputModes(params string[] modes)
    {
        _outputModes = [.. modes];
        return this;
    }

    /// <summary>
    /// Builds the <see cref="AgentCard"/> with the specified agent endpoint URL.
    /// </summary>
    /// <param name="agentUrl">The URL where this agent is reachable via A2A.</param>
    public AgentCard Build(string agentUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentUrl);

        return new AgentCard
        {
            Name = _name,
            Description = _description,
            Url = agentUrl,
            Version = _version,
            DefaultInputModes = _inputModes,
            DefaultOutputModes = _outputModes,
            Capabilities = new AgentCapabilities
            {
                Streaming = _streaming,
                PushNotifications = _pushNotifications
            },
            Skills = _skills
        };
    }
}
