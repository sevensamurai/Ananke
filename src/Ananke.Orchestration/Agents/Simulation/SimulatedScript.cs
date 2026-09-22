using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ananke.Orchestration.Agents.Simulation;

/// <summary>
/// What a simulated model should answer, and to what.
/// </summary>
/// <remarks>
/// <para>
/// Kept as data so a scenario can live in a file beside the code that runs it. A scripted run written
/// as a class is invisible in a diff — a changed answer looks like any other code change — and it can
/// only be read by someone willing to read C#. As a file it is reviewable, and a reviewer can see
/// that what the model now says is not what it said last week.
/// </para>
/// <para>
/// Entries are tried <b>in order</b> and the first match wins, so put specific ones first and a
/// catch-all (no <see cref="SimulatedResponse.When"/>) last.
/// </para>
/// </remarks>
public sealed record SimulatedScript
{
    /// <summary>The entries, in the order they are tried.</summary>
    public IReadOnlyList<SimulatedResponse> Responses { get; init; } = [];
}

/// <summary>One scripted answer, and what it answers to.</summary>
public sealed record SimulatedResponse
{
    /// <summary>
    /// Text that must appear somewhere in the request — its system prompt or any message — for this
    /// entry to match. <see langword="null"/> matches any request.
    /// </summary>
    /// <remarks>
    /// Deliberately a match against what the model was <em>sent</em>, rather than a label the caller
    /// passes alongside the request. A model is given a prompt and nothing else; a script keyed on
    /// anything the request does not carry is testing a channel the real thing does not have.
    /// </remarks>
    public string? When { get; init; }

    /// <summary>
    /// Which part of the request <see cref="When"/> is looked for in. Defaults to all of it.
    /// </summary>
    /// <remarks>
    /// Worth narrowing whenever the same text can appear in more than one place. A job that pins a
    /// contract puts the goal in the system prompt, and may also render a view of surrounding work
    /// into the user turn — which contains other work items' goals. A script keyed on a goal without
    /// saying where to look would then answer as whichever entry matched first, and the run would
    /// look like the model had confused two work items.
    /// </remarks>
    public SimulatedRequestPart WhenIn { get; init; } = SimulatedRequestPart.Anywhere;

    /// <summary>
    /// What to answer, one per matching call. The last entry repeats once they run out.
    /// </summary>
    /// <remarks>
    /// This is what makes "fails, is fixed, then passes" scriptable: successive calls that match the
    /// same entry get successive answers, which is the shape of work that takes more than one go.
    /// In a file, an answer may be written as a JSON object rather than an escaped string — the
    /// object is handed to the model verbatim.
    /// </remarks>
    [JsonConverter(typeof(RawJsonStringListConverter))]
    public IReadOnlyList<string> Replies { get; init; } = [];
}

/// <summary>Which part of a request a scripted entry is matched against.</summary>
public enum SimulatedRequestPart
{
    /// <summary>The system prompt and every message.</summary>
    Anywhere = 0,

    /// <summary>The system prompt only — where a pinned contract is rendered.</summary>
    SystemPrompt,

    /// <summary>The messages only.</summary>
    Messages
}

/// <summary>
/// Reads a list whose items may be JSON strings or JSON values, as a list of strings.
/// </summary>
/// <remarks>
/// A scripted answer is usually itself JSON — a structured response the job will deserialize. Written
/// as a string it has to be escaped, which makes the one part of the file a reviewer most wants to
/// read the one part they cannot. This lets it be written as an object and hands it on verbatim.
/// </remarks>
internal sealed class RawJsonStringListConverter : JsonConverter<IReadOnlyList<string>>
{
    public override IReadOnlyList<string> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected an array of replies.");

        var replies = new List<string>();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            replies.Add(reader.TokenType is JsonTokenType.String
                ? reader.GetString()!
                : JsonDocument.ParseValue(ref reader).RootElement.GetRawText());
        }

        return replies;
    }

    public override void Write(
        Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var reply in value)
            writer.WriteStringValue(reply);
        writer.WriteEndArray();
    }
}
