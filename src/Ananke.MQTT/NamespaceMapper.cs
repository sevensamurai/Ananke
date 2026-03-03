namespace Ananke.MQTT;

/// <summary>
/// Maps .NET enum types and actions to MQTT topic paths.
/// </summary>
internal static class NamespaceMapper
{
    private const string GlobalTopic = "global";

    public static string GetTopic<A>(string namespacePrefix, A action) where A : Enum
    {
        var name = typeof(A).Name;
        return $"{namespacePrefix ?? GlobalTopic}/{name}/{action}".ToLowerInvariant();
    }

    public static string GetTopicWildcard<A>(string namespacePrefix) where A : Enum
    {
        var name = typeof(A).Name;
        return $"{namespacePrefix ?? GlobalTopic}/{name}/+".ToLowerInvariant();
    }

    public static string? GetActionFromTopic(string topic)
    {
        if (string.IsNullOrEmpty(topic)) return default;

        var segments = topic.Split('/');
        return segments.Length != 0 ? segments[^1] : default;
    }
}
