namespace Ananke.MQTT;

/// <summary>
/// The wait before each reconnect attempt: <see cref="Initial"/>, doubled per failed attempt, capped at
/// <see cref="Max"/>, less a random part of up to a quarter so clients that lost the same broker don't
/// all return at once. A wait never exceeds <see cref="Max"/>.
/// </summary>
internal sealed record MqttReconnectDelay(TimeSpan Initial, TimeSpan Max)
{
    public static MqttReconnectDelay Default { get; } = new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));

    /// <summary>The wait before attempt <paramref name="attempt"/>, counted from 0.</summary>
    public TimeSpan For(int attempt)
    {
        var ticks = Initial.Ticks;
        for (var i = 0; i < attempt && ticks < Max.Ticks; i++)
            ticks *= 2;

        var capped = Math.Min(ticks, Max.Ticks);
        var jitter = (long)(Random.Shared.NextDouble() * (capped / 4));
        return TimeSpan.FromTicks(capped - jitter);
    }
}
