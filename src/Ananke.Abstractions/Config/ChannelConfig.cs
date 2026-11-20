namespace Ananke.Abstractions.Config;

public class ChannelConfig
{
    public string Host { get; set; } = "localhost";

    /// <summary>The broker's port: commonly 1883, or 8883 with <see cref="UseTls"/>.</summary>
    public int Port { get; set; } = 1883;

    public string? Username { get; set; }
    public string? Password { get; set; }
    public string Namespace { get; set; } = "ananke";

    /// <summary>
    /// Optional: group name.
    /// Used for load balancing/allocating listeners to a topic by group.
    /// </summary>
    public string? GroupName { get; set; }

    /// <summary>
    /// Connect over TLS, validating the broker's certificate against the machine's trusted roots.
    /// Default <see langword="false"/>.
    /// </summary>
    public bool UseTls { get; set; }

    /// <summary>
    /// The start of each client's id on the broker. Each client's id is <c>{ClientId}-{hash}</c>: the value cut to
    /// 14 characters, then 8 hex digits of a hash of the whole value, what the client is (reader, writer or handoff
    /// channel), its action type, its message type (a reader), the <see cref="Namespace"/> and the handoff channel's
    /// name. So one config serves a reader and a writer, an id has at most 23 characters, the length the MQTT spec
    /// has every broker accept, and it is the same after a restart, which a kept session needs; renaming a type or
    /// the namespace changes it, and the broker keeps a session under the old id. Two channels with the same id in
    /// one process: connecting the second throws. A broker disconnects a client when another connects with the same
    /// id, so two processes need two values. <see langword="null"/>: a random id.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Start every connect with no session state. <see langword="false"/> asks the broker to keep the
    /// session, and the messages it holds for it, between connects; that needs a <see cref="ClientId"/>.
    /// With a kept session, a reader's dispose leaves its topic subscribed; its <c>ClearAsync</c> unsubscribes.
    /// Default <see langword="true"/>.
    /// </summary>
    public bool CleanSession { get; set; } = true;

    /// <summary>
    /// The delivery guarantee messages are published with and topics are subscribed at.
    /// Default <see cref="ChannelQualityOfService.ExactlyOnce"/>.
    /// </summary>
    public ChannelQualityOfService QualityOfService { get; set; } = ChannelQualityOfService.ExactlyOnce;
}

/// <summary>The delivery guarantee of a channel message.</summary>
public enum ChannelQualityOfService
{
    /// <summary>Delivered at most once; may be lost.</summary>
    AtMostOnce = 0,

    /// <summary>Delivered at least once; may be delivered again.</summary>
    AtLeastOnce = 1,

    /// <summary>Delivered once.</summary>
    ExactlyOnce = 2,
}
