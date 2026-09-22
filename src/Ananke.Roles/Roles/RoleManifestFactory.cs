using Ananke.Design;
using Ananke.Design.Tools;
using Ananke.Orchestration.Tools;

namespace Ananke.Roles.Roles;

/// <summary>
/// Creates <see cref="WorkflowManifest"/> instances from role definitions.
/// </summary>
public sealed class RoleManifestFactory(IReadOnlyDictionary<string, ModelDefinition>? modelAliases = null)
{
    private readonly IReadOnlyDictionary<string, ModelDefinition> _modelAliases =
        modelAliases ?? new Dictionary<string, ModelDefinition>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a single-agent manifest for the supplied role.
    /// </summary>
    public WorkflowManifest CreateManifest(AgentRole role, ToolKit? toolKit = null)
    {
        ArgumentNullException.ThrowIfNull(role);

        var systemPrompt = File.ReadAllText(role.SystemPromptPath);
        var tools = CreateToolEntries(role, toolKit);

        return new WorkflowManifest
        {
            Name = role.Name,
            Models = new Dictionary<string, ModelDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                [role.ModelAlias] = ResolveModelDefinition(role.ModelAlias)
            },
            Tools = tools,
            Jobs = new Dictionary<string, JobDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["main"] = new JobDefinition
                {
                    Type = "agent",
                    ModelAlias = role.ModelAlias,
                    SystemPrompt = systemPrompt,
                    Tools = role.ToolNames,
                    MaxToolRounds = role.MaxToolRounds
                }
            },
            Connections = ["main -> End"],
            Profiles = new Dictionary<string, ProfileDefinition>(StringComparer.OrdinalIgnoreCase),
            Intents = role.DomainTags
        };
    }

    private ModelDefinition ResolveModelDefinition(string modelAlias)
    {
        if (_modelAliases.TryGetValue(modelAlias, out var modelDefinition))
        {
            return new ModelDefinition
            {
                Provider = modelDefinition.Provider,
                Model = modelDefinition.Model,
                Endpoint = modelDefinition.Endpoint,
                Temperature = modelDefinition.Temperature
            };
        }

        // Unresolved. Recorded as a reference rather than dressed up as a provider.
        //
        // This used to return Provider = "studio", which no provider factory has ever registered
        // and which ModelResolver rejects with "call Register(\"studio\", ...)" — advice for a
        // provider that does not exist. It was not LM Studio either: that is reached as
        // provider "openai" with a custom endpoint, as this repository does everywhere including
        // MiniAgencyDemo. It was a sentinel that leaked into a real field.
        //
        // Ref is the vocabulary for exactly this state, so an unresolved alias is
        // now visible to DeployabilityValidator as FED016 and refused by ModelResolver, instead of
        // travelling as a provider nothing can serve. Deliberately not a throw:
        // StudioOptions.ModelAliasMap defaults to empty, so building manifests without a map is a
        // supported path.
        return new ModelDefinition { Ref = modelAlias };
    }

    private static Dictionary<string, ToolManifestEntry> CreateToolEntries(AgentRole role, ToolKit? toolKit)
    {
        var tools = new Dictionary<string, ToolManifestEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var toolName in role.ToolNames)
        {
            if (toolKit is not null && toolKit.Tools.TryGetValue(toolName, out var toolDefinition))
            {
                tools[toolName] = new ToolManifestEntry
                {
                    Key = toolName,
                    Name = toolDefinition.Name,
                    Description = toolDefinition.Description,
                    Tags = toolDefinition.Tags,
                    Binding = new ToolManifestBinding
                    {
                        Kind = "builtin",
                        Reference = toolDefinition.Name
                    }
                };
                continue;
            }

            tools[toolName] = new ToolManifestEntry
            {
                Key = toolName,
                Name = toolName,
                Description = $"Tool '{toolName}' declared by role '{role.Name}'.",
                Tags = role.DomainTags,
                Binding = new ToolManifestBinding
                {
                    Kind = "builtin",
                    Reference = toolName
                }
            };
        }

        return tools;
    }
}
