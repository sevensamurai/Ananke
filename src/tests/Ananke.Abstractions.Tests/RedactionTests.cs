using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Shouldly;

namespace Ananke.Abstractions.Tests;

/// <summary>
/// What a record holds of a value whose type marks a property with <see cref="RedactAttribute"/>.
/// </summary>
/// <remarks>
/// Two ways in: a value serialized with <see cref="Redaction.Masking"/>, and JSON text — a tool
/// call's arguments — masked by the types of its members. Text that cannot be read as its type is
/// masked whole: whatever it holds cannot be told apart from a marked value.
/// </remarks>
[TestFixture]
public class RedactionTests
{
    private sealed record Guest
    {
        [Redact]
        public string Email { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;
    }

    private sealed record Booking
    {
        public string Reference { get; init; } = string.Empty;

        public List<Guest> Guests { get; init; } = [];

        public Dictionary<string, Guest> ByRoom { get; init; } = [];
    }

    private sealed record Plain
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed record Tree
    {
        public string Name { get; init; } = string.Empty;

        public List<Tree> Children { get; init; } = [];
    }

    private sealed record Renamed
    {
        [Redact]
        [JsonPropertyName("mail")]
        public string Email { get; init; } = string.Empty;
    }

    private sealed record Optional
    {
        [Redact]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Email { get; init; }
    }

    private sealed record Pairs
    {
        public List<KeyValuePair<string, Guest>> Guests { get; init; } = [];
    }

    // ── A value as it is serialized ──

    [Test]
    public void Masking_AMarkedProperty_IsWrittenAsTheMarkerAndTheRestAsUsual()
    {
        var json = JsonSerializer.Serialize(
            new Booking { Reference = "BK1", Guests = [new Guest { Email = "ana@example.com", Name = "Ana" }] },
            Redaction.Masking(AnankeJson.Display));

        json.ShouldNotContain("ana@");
        json.ShouldBe("""{"reference":"BK1","guests":[{"email":"[redacted]","name":"Ana"}],"byRoom":{}}""");
    }

    [Test]
    public void Masking_AMarkedValueHeldAsAnObject_IsWrittenAsTheMarker() =>
        JsonSerializer.Serialize<object>(new Guest { Email = "ana@example.com" }, Redaction.Masking(AnankeJson.Display))
            .ShouldNotContain("ana@");

    [Test]
    public void Masking_TheSameOptions_AreMaskedOnce() =>
        Redaction.Masking(AnankeJson.Display).ShouldBeSameAs(Redaction.Masking(AnankeJson.Display));

    // ── JSON text, by the types of its members ──

    [Test]
    public void Mask_AMarkedProperty_IsReplacedAndTheRestKept()
    {
        var guest = Parse(MaskedAs<Guest>("""{"Email":"ana@example.com","Name":"Ana"}"""))["a"]!;

        guest["Email"]!.GetValue<string>().ShouldBe(Redaction.Marker);
        guest["Name"]!.GetValue<string>().ShouldBe("Ana");
    }

    [Test]
    public void Mask_MarkedPropertiesInAListAndADictionary_AreReplaced()
    {
        var masked = MaskedAs<Booking>("""
            {"reference":"BK1",
             "guests":[{"email":"ana@example.com","name":"Ana"},{"email":"bo@example.com","name":"Bo"}],
             "byRoom":{"12":{"email":"cy@example.com","name":"Cy"}}}
            """);

        masked.ShouldNotContain("@example.com");
        Parse(masked)["a"]!["reference"]!.GetValue<string>().ShouldBe("BK1");
        Parse(masked)["a"]!["byRoom"]!["12"]!["name"]!.GetValue<string>().ShouldBe("Cy");
    }

    [Test]
    public void Mask_ANameAsJsonPropertyNameWritesIt_IsMatched() =>
        MaskedAs<Renamed>("""{"mail":"ana@example.com"}""").ShouldNotContain("ana@");

    [Test]
    public void Mask_AMarkedPropertyIgnoredOnlyWhenNull_IsReplaced() =>
        MaskedAs<Optional>("""{"Email":"ana@example.com"}""").ShouldNotContain("ana@");

    [Test]
    public void Mask_AMarkedPropertyInsideAKeyValuePair_IsReplaced() =>
        MaskedAs<Pairs>("""{"Guests":[{"Key":"12","Value":{"Email":"ana@example.com","Name":"Ana"}}]}""")
            .ShouldNotContain("ana@");

    [Test]
    public void Mask_ATypeWithNoMarks_ReturnsTheTextAsWritten()
    {
        const string json = """{ "a" : { "Name" : "Ana" } }""";

        Redaction.Mask(json, Declared<Plain>()).ShouldBe(json);
    }

    [Test]
    public void Mask_ATypeThatHoldsItself_IsReadWithoutAMark()
    {
        const string json = """{ "a" : { "Name" : "root", "Children" : [ { "Name" : "leaf" } ] } }""";

        Redaction.Mask(json, Declared<Tree>()).ShouldBe(json);
    }

    [Test]
    public void Mask_AMemberWithNoDeclaredType_IsKeptAsWritten() =>
        Parse(Redaction.Mask("""{"a":{"Email":"ana@example.com"},"note":"ana@example.com"}""", Declared<Guest>()))
            ["note"]!.GetValue<string>().ShouldBe("ana@example.com");

    [Test]
    public void Mask_TextThatDoesNotParse_IsReplacedWhole() =>
        Redaction.Mask("""{"a":{"Email":"ana@example.com" """, Declared<Guest>()).ShouldBe(Redaction.Marker);

    [Test]
    public void Mask_TextWithADuplicatePropertyName_IsReplacedWhole() =>
        Redaction.Mask("""{"a":{"Email":"ana@example.com","Email":"bo@example.com"}}""", Declared<Guest>())
            .ShouldBe(Redaction.Marker);

    [Test]
    public void Mask_AValueNotShapedLikeItsType_IsReplacedWhole() =>
        MaskedAs<Booking>("""{"guests":"ana@example.com"}""").ShouldNotContain("ana@");

    [Test]
    public void Mask_MembersWhoseNamesDifferOnlyInCase_DoesNotThrow() =>
        Should.NotThrow(() => Redaction.Mask(
            """{"guests":[],"Guests":[]}""",
            new Dictionary<string, Type> { ["guests"] = typeof(Guest[]), ["Guests"] = typeof(Guest[]) }));

    [Test]
    public void Mask_NoArguments_IsKeptAsWritten() =>
        Redaction.Mask("", Declared<Guest>()).ShouldBe("");

    /// <summary>One member, <c>a</c>, declared as <typeparamref name="T"/>.</summary>
    private static Dictionary<string, Type> Declared<T>() => new() { ["a"] = typeof(T) };

    /// <summary><paramref name="json"/> as the value of member <c>a</c>, declared as <typeparamref name="T"/>, masked.</summary>
    private static string MaskedAs<T>(string json) => Redaction.Mask($$"""{"a":{{json}}}""", Declared<T>());

    private static JsonNode Parse(string json) => JsonNode.Parse(json) ?? throw new JsonException("null");
}
