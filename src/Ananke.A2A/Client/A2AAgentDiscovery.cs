using A2A;

namespace Ananke.A2A.Client;

/// <summary>
/// Lightweight wrapper that describes a remote A2A agent's capabilities
/// in Ananke-friendly terms, resolved from an <see cref="AgentCard"/>.
/// </summary>
public sealed record A2AAgentCardInfo
{
    /// <summary>The agent's display name.</summary>
    public required string Name { get; init; }

    /// <summary>A human-readable description of the agent.</summary>
    public required string Description { get; init; }

    /// <summary>The agent's A2A endpoint URL.</summary>
    public required string Url { get; init; }

    /// <summary>The agent version string.</summary>
    public required string Version { get; init; }

    /// <summary>Whether the agent supports streaming responses.</summary>
    public bool SupportsStreaming { get; init; }

    /// <summary>Whether the agent supports push notifications.</summary>
    public bool SupportsPushNotifications { get; init; }

    /// <summary>Skill descriptors advertised by the agent.</summary>
    public IReadOnlyList<A2ASkillInfo> Skills { get; init; } = [];

    /// <summary>Media types the agent accepts as input.</summary>
    public IReadOnlyList<string> DefaultInputModes { get; init; } = [];

    /// <summary>Media types the agent produces as output.</summary>
    public IReadOnlyList<string> DefaultOutputModes { get; init; } = [];

    /// <summary>The raw <see cref="AgentCard"/> from the remote agent.</summary>
    public AgentCard? RawCard { get; init; }
}

/// <summary>
/// Describes a single skill advertised by a remote A2A agent.
/// </summary>
public sealed record A2ASkillInfo
{
    /// <summary>Unique skill identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable skill name.</summary>
    public required string Name { get; init; }

    /// <summary>What the skill does.</summary>
    public required string Description { get; init; }

    /// <summary>Keywords for matching.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>
/// Resolves and caches <see cref="AgentCard"/> metadata from remote A2A agent endpoints,
/// converting them to Ananke-friendly <see cref="A2AAgentCardInfo"/> descriptors.
/// </summary>
/// <example>
/// <code>
/// var discovery = new A2AAgentDiscovery();
/// var info = await discovery.DiscoverAsync(new Uri("http://localhost:5100/"));
/// Console.WriteLine($"Agent: {info.Name}, Skills: {info.Skills.Count}");
/// </code>
/// </example>
public sealed class A2AAgentDiscovery
{
    private readonly HttpClient? _httpClient;

    public A2AAgentDiscovery(HttpClient? httpClient = null)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Resolves the <see cref="AgentCard"/> at <paramref name="baseUri"/> and returns
    /// an Ananke-friendly descriptor.
    /// </summary>
    /// <param name="baseUri">
    /// The base URI of the remote agent. The well-known agent card path is resolved automatically.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<A2AAgentCardInfo> DiscoverAsync(Uri baseUri, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(baseUri);

        var resolver = _httpClient is not null
            ? new A2ACardResolver(baseUri, _httpClient)
            : new A2ACardResolver(baseUri);

        var card = await resolver.GetAgentCardAsync(ct).ConfigureAwait(false);
        return MapCard(card);
    }

    private static A2AAgentCardInfo MapCard(AgentCard card)
    {
        var skills = card.Skills?
            .Select(s => new A2ASkillInfo
            {
                Id = s.Id ?? string.Empty,
                Name = s.Name ?? string.Empty,
                Description = s.Description ?? string.Empty,
                Tags = s.Tags ?? []
            })
            .ToList()
            ?? [];

        return new A2AAgentCardInfo
        {
            Name = card.Name ?? string.Empty,
            Description = card.Description ?? string.Empty,
            Url = card.Url ?? string.Empty,
            Version = card.Version ?? string.Empty,
            SupportsStreaming = card.Capabilities?.Streaming ?? false,
            SupportsPushNotifications = card.Capabilities?.PushNotifications ?? false,
            Skills = skills,
            DefaultInputModes = card.DefaultInputModes ?? [],
            DefaultOutputModes = card.DefaultOutputModes ?? [],
            RawCard = card
        };
    }
}
