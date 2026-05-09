namespace LogEventsDemo;

/// <summary>
/// Derives <see cref="Ananke.Learning.EmpiricalMemory.SemanticDescription"/> tags
/// from <see cref="LogEvent"/> structured fields. Maps the closed vocabulary
/// of the simulated system into the tag namespace used for empirical memory.
/// </summary>
internal static class LogTagExtractor
{
    /// <summary>
    /// Extracts weighted semantic tags from a log event.
    /// Tags follow the namespace convention: <c>service:</c>, <c>error:</c>,
    /// <c>cause:</c>, <c>infra:</c>, <c>severity:</c>.
    /// </summary>
    internal static Dictionary<string, float> ExtractTags(LogEvent evt)
    {
        var tags = new Dictionary<string, float>
        {
            [$"service:{evt.Service}"] = 1.0f,
            [$"severity:{evt.Level.ToString().ToLowerInvariant()}"] = 0.8f
        };

        if (evt.Fields.TryGetValue("error_code", out var errorCode))
            tags[$"error:{errorCode.ToLowerInvariant()}"] = 1.0f;

        if (evt.Fields.TryGetValue("cause", out var cause))
            tags[$"cause:{cause}"] = 1.0f;

        if (evt.Fields.TryGetValue("infra", out var infra))
            tags[$"infra:{infra}"] = 0.9f;

        if (evt.Fields.TryGetValue("upstream", out var upstream))
            tags[$"upstream:{upstream}"] = 0.8f;

        if (evt.Fields.TryGetValue("exception", out var exception))
            tags[$"exception:{exception.ToLowerInvariant()}"] = 0.9f;

        if (evt.Fields.TryGetValue("deploy", out var deploy))
            tags[$"deploy:{deploy}"] = 0.7f;

        if (evt.Fields.TryGetValue("scenario", out var scenario))
            tags[$"scenario:{scenario.ToLowerInvariant().Replace(' ', '-')}"] = 0.6f;

        return tags;
    }

    /// <summary>
    /// Extracts tags from a collection of correlated log events within a time window.
    /// Combines tags from all events, keeping the highest weight for each tag key.
    /// </summary>
    internal static Dictionary<string, float> ExtractWindowTags(IEnumerable<LogEvent> events)
    {
        var combined = new Dictionary<string, float>();

        foreach (var evt in events)
        {
            foreach (var (key, weight) in ExtractTags(evt))
            {
                if (!combined.TryGetValue(key, out var existing) || weight > existing)
                    combined[key] = weight;
            }
        }

        return combined;
    }
}
