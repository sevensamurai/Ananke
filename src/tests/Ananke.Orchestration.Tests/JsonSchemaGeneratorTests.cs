using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using Shouldly;

namespace Ananke.Orchestration.Tests;

[TestFixture]
public class JsonSchemaGeneratorTests
{
    // ── Primitive types ──────────────────────────────────────────────────────

    private record Primitives
    {
        public string Text { get; init; } = string.Empty;
        public int Count { get; init; }
        public long BigNumber { get; init; }
        public double Rate { get; init; }
        public decimal Amount { get; init; }
        public bool Flag { get; init; }
    }

    [Test]
    public void Generate_FlatPrimitives_CorrectTypes()
    {
        var schema = JsonSchemaGenerator.GenerateForType(typeof(Primitives));

        var props = (Dictionary<string, object>)schema["properties"];
        ((string)((Dictionary<string, object>)props["Text"])["type"]).ShouldBe("string");
        ((string)((Dictionary<string, object>)props["Count"])["type"]).ShouldBe("integer");
        ((string)((Dictionary<string, object>)props["BigNumber"])["type"]).ShouldBe("integer");
        ((string)((Dictionary<string, object>)props["Rate"])["type"]).ShouldBe("number");
        ((string)((Dictionary<string, object>)props["Amount"])["type"]).ShouldBe("number");
        ((string)((Dictionary<string, object>)props["Flag"])["type"]).ShouldBe("boolean");
    }

    // ── Nullable types ───────────────────────────────────────────────────────
    // Note: nullable reference types (string?, MyClass?) cannot be distinguished
    // from their non-nullable equivalents at runtime via Nullable.GetUnderlyingType.
    // Only nullable value types (int?, bool?, etc.) carry the Nullable<T> wrapper.

    private record Nullables
    {
        public int? MaybeCount { get; init; }
        public bool? MaybeFlag { get; init; }
        public double? MaybeRate { get; init; }
    }

    [Test]
    public void Generate_NullableValueTypes_IncludeNullType()
    {
        var schema = JsonSchemaGenerator.GenerateForType(typeof(Nullables));
        var props = (Dictionary<string, object>)schema["properties"];

        ((string[])((Dictionary<string, object>)props["MaybeCount"])["type"])
            .ShouldBe(["integer", "null"]);

        ((string[])((Dictionary<string, object>)props["MaybeFlag"])["type"])
            .ShouldBe(["boolean", "null"]);

        ((string[])((Dictionary<string, object>)props["MaybeRate"])["type"])
            .ShouldBe(["number", "null"]);
    }

    // ── Enum ─────────────────────────────────────────────────────────────────

    private enum Status { Pending, Active, Closed }

    private record WithEnum { public Status State { get; init; } }

    [Test]
    public void Generate_EnumProperty_IncludesEnumValues()
    {
        var schema = JsonSchemaGenerator.GenerateForType(typeof(WithEnum));
        var props = (Dictionary<string, object>)schema["properties"];
        var stateSchema = (Dictionary<string, object>)props["State"];

        ((string)stateSchema["type"]).ShouldBe("string");
        var enumValues = (object[])stateSchema["enum"];
        enumValues.ShouldContain("Pending");
        enumValues.ShouldContain("Active");
        enumValues.ShouldContain("Closed");
    }

    // ── Collection types ─────────────────────────────────────────────────────

    private record WithList { public List<string> Tags { get; init; } = []; }

    private record WithArray { public int[] Scores { get; init; } = []; }

    [Test]
    public void Generate_ListProperty_ArrayType()
    {
        var schema = JsonSchemaGenerator.GenerateForType(typeof(WithList));
        var props = (Dictionary<string, object>)schema["properties"];
        var tagsSchema = (Dictionary<string, object>)props["Tags"];

        ((string)tagsSchema["type"]).ShouldBe("array");
        var items = (Dictionary<string, object>)tagsSchema["items"];
        ((string)items["type"]).ShouldBe("string");
    }

    [Test]
    public void Generate_ArrayProperty_ArrayType()
    {
        var schema = JsonSchemaGenerator.GenerateForType(typeof(WithArray));
        var props = (Dictionary<string, object>)schema["properties"];
        var scoresSchema = (Dictionary<string, object>)props["Scores"];

        ((string)scoresSchema["type"]).ShouldBe("array");
        var items = (Dictionary<string, object>)scoresSchema["items"];
        ((string)items["type"]).ShouldBe("integer");
    }

    // ── Date/time ────────────────────────────────────────────────────────────

    private record WithDates
    {
        public DateTime CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
        public DateOnly BirthDate { get; init; }
    }

    [Test]
    public void Generate_DateTimeProperties_StringDateTimeFormat()
    {
        var schema = JsonSchemaGenerator.GenerateForType(typeof(WithDates));
        var props = (Dictionary<string, object>)schema["properties"];

        foreach (var key in new[] { "CreatedAt", "UpdatedAt", "BirthDate" })
        {
            var propSchema = (Dictionary<string, object>)props[key];
            ((string)propSchema["type"]).ShouldBe("string");
            ((string)propSchema["format"]).ShouldBe("date-time");
        }
    }

    // ── Nested object ─────────────────────────────────────────────────────────

    private record Address { public string Street { get; init; } = string.Empty; }

    private record Person { public string Name { get; init; } = string.Empty; public Address Home { get; init; } = new(); }

    [Test]
    public void Generate_NestedObject_RecursiveSchema()
    {
        var schema = JsonSchemaGenerator.GenerateForType(typeof(Person));
        var props = (Dictionary<string, object>)schema["properties"];

        ((string)((Dictionary<string, object>)props["Name"])["type"]).ShouldBe("string");

        var homeSchema = (Dictionary<string, object>)props["Home"];
        ((string)homeSchema["type"]).ShouldBe("object");
        var homeProps = (Dictionary<string, object>)homeSchema["properties"];
        homeProps.ShouldContainKey("Street");
    }

    // ── All properties are required ────────────────────────────────────────

    [Test]
    public void Generate_RequiredArray_ContainsAllProperties()
    {
        var schema = JsonSchemaGenerator.GenerateForType(typeof(Primitives));
        var required = (List<string>)schema["required"];
        required.ShouldContain("Text");
        required.ShouldContain("Count");
        required.ShouldContain("Flag");
        required.Count.ShouldBe(6);
    }

    // ── additionalProperties = false ──────────────────────────────────────

    [Test]
    public void Generate_AdditionalPropertiesIsFalse()
    {
        var schema = JsonSchemaGenerator.GenerateForType(typeof(Primitives));
        ((bool)schema["additionalProperties"]).ShouldBeFalse();
    }
}
