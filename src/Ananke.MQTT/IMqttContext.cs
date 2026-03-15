using Ananke.Abstractions;

namespace Ananke.MQTT;

/// <summary>
/// Extends <see cref="IBaseContext"/> with MQTT topic-routing support.
/// The <see cref="Command"/> property is populated by <see cref="MqttChannelReader{M,A}"/>
/// from the MQTT topic's action segment.
/// <para>
/// For non-MQTT state machines, use <see cref="IBaseContext"/> directly —
/// no <c>Command</c> property needed.
/// </para>
/// </summary>
public interface IMqttContext : IBaseContext
{
    /// <summary>
    /// Command extracted from the MQTT topic by the channel reader.
    /// </summary>
    string? Command { get; set; }
}
