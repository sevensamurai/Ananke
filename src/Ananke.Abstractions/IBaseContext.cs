namespace Ananke.Abstractions;

/// <summary>
/// Base context interface for state machine operations.
/// The Id uniquely identifies the entity/instance being addressed.
/// </summary>
public interface IBaseContext
{
    /// <summary>
    /// Unique identifier for this context/entity
    /// </summary>
    public string Id { get; }
}
