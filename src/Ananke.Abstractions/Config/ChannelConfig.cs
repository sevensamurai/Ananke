namespace Ananke.Abstractions.Config;

public class ChannelConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string Namespace { get; set; } = "ananke";
    
    /// <summary>
    /// Optional: group name.
    /// Used for load balancing/allocating listeners to a topic by group.
    /// </summary>
    public string? GroupName { get; set; }
}
