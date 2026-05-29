using System.Text.Json;
using Ananke.Roles.Roles;

namespace MiniAgencyDemo;

internal static class MiniAgencyRoleLoader
{
    public static IReadOnlyList<AgentRole> Load(string rolesPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rolesPath);

        var json = File.ReadAllText(rolesPath);
        var roles = JsonSerializer.Deserialize<List<AgentRole>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("roles.json did not contain any role definitions.");

        var root = Path.GetDirectoryName(rolesPath)
            ?? throw new InvalidOperationException("Could not determine the demo directory for role prompt resolution.");

        return roles
            .Select(role => role with
            {
                SystemPromptPath = Path.GetFullPath(Path.Combine(root, role.SystemPromptPath))
            })
            .ToArray();
    }
}
