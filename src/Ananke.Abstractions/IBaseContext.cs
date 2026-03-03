namespace Ananke.Abstractions;

/// <summary>
/// Base context interface for message-driven operations.
/// The Id uniquely identifies the entity/instance being addressed.
/// </summary>
public interface IBaseContext
{
    /// <summary>
    /// Unique identifier for this context/entity
    /// </summary>
    public long Id { get; }

    /// <summary>
    /// Optional command passed through the message channel
    /// </summary>
    public string? Command { get; set; }
}
