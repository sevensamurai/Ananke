using System.Net;
using System.Text;
using System.Text.Json;

namespace Ananke.TestHelpers.ProviderStubs;

/// <summary>
/// Terminates an adapter's handler chain with a canned provider response, chosen from the request
/// the adapter actually serialised.
/// </summary>
/// <remarks>
/// <para>
/// The conformance scenarios span text, structured output and streaming, so a single fixed payload
/// cannot satisfy them — the stub has to answer the question that was asked. What it must not do is
/// answer it the way the adapter would like: <b>every payload here is written from the provider's
/// documented wire format</b>, not from what the mapping code happens to read. A stub written the
/// other way round proves only that the adapter agrees with itself.
/// </para>
/// <para>
/// That is the boundary this suite draws. This harness proves an adapter is self-consistent
/// with the Ananke contract; it cannot prove the adapter is right about the service. The 2026-08-20
/// Gemini token under-count is the standing example — a stub would have reported the same wrong
/// number the code already believed.
/// </para>
/// </remarks>
public abstract class ScriptedProviderHandler : HttpMessageHandler
{
    /// <summary>Deterministic token counts, so usage-accounting scenarios are reliable.</summary>
    protected const int InputTokens = 9;

    /// <summary>Deterministic token counts, so usage-accounting scenarios are reliable.</summary>
    protected const int OutputTokens = 2;

    /// <summary>The text every canned reply carries, as two whitespace-separated words.</summary>
    protected const string ReplyText = "conformance ok";

    /// <summary>The object a structured-output request is answered with.</summary>
    protected const string StructuredJson = """{"result":"ok"}""";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var (payload, mediaType) = Respond(request, body);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, mediaType)
        };
    }

    /// <summary>Chooses the canned response for one request.</summary>
    /// <param name="request">The request, after every handler in the chain has run.</param>
    /// <param name="body">Its serialised body.</param>
    protected abstract (string Payload, string MediaType) Respond(HttpRequestMessage request, string body);

    /// <summary>Formats server-sent events the way every provider streams them.</summary>
    protected static string Sse(params string[] events) =>
        string.Concat(events.Select(e => e + "\n\n"));

    protected static bool Mentions(string body, string token) =>
        body.Contains(token, StringComparison.Ordinal);

    /// <summary>
    /// The name of the first tool the request declares, or <see langword="null"/> if it declares
    /// none — so a stub can answer a tool-bearing request with a tool call, the way the service
    /// would, instead of falling back to text and leaving the scenario skipped.
    /// </summary>
    /// <remarks>
    /// All three providers nest the declaration differently — OpenAI at <c>tools[].function.name</c>,
    /// Anthropic at <c>tools[].name</c>, Gemini at <c>tools[].functionDeclarations[].name</c> — so
    /// this finds the <c>tools</c> container and takes the first <c>name</c> inside it rather than
    /// hard-coding three paths.
    /// </remarks>
    protected static string? FirstToolName(string body)
    {
        if (string.IsNullOrEmpty(body))
            return null;

        using var document = JsonDocument.Parse(body);
        return FindToolsContainer(document.RootElement);

        static string? FindToolsContainer(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.NameEquals("tools") && FirstName(property.Value) is { } name)
                            return name;

                        if (FindToolsContainer(property.Value) is { } nested)
                            return nested;
                    }

                    return null;

                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        if (FindToolsContainer(item) is { } found)
                            return found;
                    }

                    return null;

                default:
                    return null;
            }
        }

        static string? FirstName(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.NameEquals("name") && property.Value.ValueKind == JsonValueKind.String)
                            return property.Value.GetString();

                        if (FirstName(property.Value) is { } nested)
                            return nested;
                    }

                    return null;

                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        if (FirstName(item) is { } found)
                            return found;
                    }

                    return null;

                default:
                    return null;
            }
        }
    }

    /// <summary>JSON-quotes a value for embedding in a canned payload.</summary>
    protected static string Quote(string value) => JsonSerializer.Serialize(value);
}
