namespace Ananke.Abstractions;

/// <summary>
/// Marks a property as personal data. Wherever Ananke records a value of its type, the property's
/// value is replaced by <see cref="Redaction.Marker"/>.
/// </summary>
/// <remarks>
/// Only the record is masked: a model is still shown the property in a schema, a tool still receives
/// its value, and a checkpoint still holds it. Text with no type to mark — a result a tool returns as
/// a string, an exception's message — is not masked. A positional record marks a property with
/// <c>[property: Redact]</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class RedactAttribute : Attribute
{
}
