namespace Ananke.Abstractions.Config;

public class CacheConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6379;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int? LockExpirySeconds { get; set; }
}
