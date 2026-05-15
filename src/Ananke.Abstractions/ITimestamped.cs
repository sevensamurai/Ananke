namespace Ananke.Abstractions;

/// <summary>
/// Implemented by payloads, messages, or events that carry their own event time.
/// When a state-machine transition payload implements this interface the framework
/// uses <see cref="EventTime"/> as the transition's attributed timestamp instead of
/// <see cref="System.DateTimeOffset.UtcNow"/>.  This supports back-dated events,
/// offline-device replay, and any scenario where the logical event time differs from
/// wall-clock processing time.
/// </summary>
public interface ITimestamped
{
    /// <summary>Gets the point in time at which the event logically occurred.</summary>
    DateTimeOffset EventTime { get; }
}
