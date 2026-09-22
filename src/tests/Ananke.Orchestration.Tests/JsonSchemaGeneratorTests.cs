using System.Text.Json.Serialization;

using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Agents.Middleware;
using Ananke.Orchestration.Agents.Routing;
using Ananke.Orchestration.Planning;
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

    // ── Derived properties ───────────────────────────────────────────────────
    // Every property that is emitted is also marked required, so a computed one that cannot be set
    // is demanded from the model and can only be satisfied by declining to answer at all.

    [Test]
    public void Generate_ADerivedProperty_IsNeitherEmittedNorRequired()
    {
        var schema = JsonSchemaGenerator.GenerateForType(typeof(Operation));

        var properties = (Dictionary<string, object>)schema["properties"];
        var required = (List<string>)schema["required"];

        properties.ShouldNotContainKey(nameof(Operation.IsNamed));
        required.ShouldNotContain(nameof(Operation.IsNamed));
    }

    // ── Described properties ─────────────────────────────────────────────────
    // A field explained only in the prompt competes with everything else there and drifts from the
    // type. The schema is applied on every call, so the meaning of a step's answer lives there.

    [Test]
    public void Generate_TheReportAStepAnswersWith_DescribesEveryField()
    {
        var report = (Dictionary<string, object>)
            JsonSchemaGenerator.GenerateForType(typeof(PlanNodeReport))["properties"];
        var operation = (Dictionary<string, object>)
            JsonSchemaGenerator.GenerateForType(typeof(Operation))["properties"];

        foreach (var (name, schema) in report.Concat(operation))
        {
            ((Dictionary<string, object>)schema).ShouldContainKey(
                "description", $"{name} reaches the model with no meaning attached");
        }
    }

    // ── Nullable types ───────────────────────────────────────────────────────
    // A nullable value type carries the Nullable<T> wrapper; a nullable reference type carries only
    // an annotation, which NullabilityInfoContext reads off the property.

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

        foreach (var key in new[] { "CreatedAt", "UpdatedAt" })
        {
            var propSchema = (Dictionary<string, object>)props[key];
            ((string)propSchema["type"]).ShouldBe("string");
            ((string)propSchema["format"]).ShouldBe("date-time");
        }
    }

    [Test]
    public void Generate_ADateOnlyProperty_IsADateRatherThanAnInstant()
    {
        // A model answering a date-time for a day writes a timestamp, and a DateOnly will not parse
        // one back.
        var schema = JsonSchemaGenerator.GenerateForType(typeof(WithDates));
        var props = (Dictionary<string, object>)schema["properties"];
        var birthDate = (Dictionary<string, object>)props["BirthDate"];

        ((string)birthDate["type"]).ShouldBe("string");
        ((string)birthDate["format"]).ShouldBe("date");
    }

    // ── What a field means ────────────────────────────────────────────────

    private record Described
    {
        // Fully qualified: NUnit has a Description attribute of its own, for naming a test.
        [System.ComponentModel.Description("Every option the step found, best first.")]
        public string Options { get; init; } = string.Empty;

        public string Plain { get; init; } = string.Empty;
    }

    [Test]
    public void Generate_ADescribedProperty_CarriesItsDescription()
    {
        var schema = JsonSchemaGenerator.GenerateForType(typeof(Described));
        var props = (Dictionary<string, object>)schema["properties"];

        ((string)((Dictionary<string, object>)props["Options"])["description"])
            .ShouldBe("Every option the step found, best first.");
    }

    [Test]
    public void Generate_APropertyWithNoDescription_CarriesNone() =>
        ((Dictionary<string, object>)((Dictionary<string, object>)
                JsonSchemaGenerator.GenerateForType(typeof(Described))["properties"])["Plain"])
            .ShouldNotContainKey("description");

    // ── Nullable reference types ──────────────────────────────────────────

    private record MaybeText
    {
        public string? Said { get; init; }
        public string Asked { get; init; } = string.Empty;
    }

    [Test]
    public void Generate_ANullableReferenceProperty_IncludesNullType()
    {
        var schema = JsonSchemaGenerator.GenerateForType(typeof(MaybeText));
        var props = (Dictionary<string, object>)schema["properties"];

        ((string[])((Dictionary<string, object>)props["Said"])["type"]).ShouldBe(["string", "null"]);
    }

    [Test]
    public void Generate_ANonNullableReferenceProperty_IsUnchanged()
    {
        var schema = JsonSchemaGenerator.GenerateForType(typeof(MaybeText));
        var props = (Dictionary<string, object>)schema["properties"];

        ((string)((Dictionary<string, object>)props["Asked"])["type"]).ShouldBe("string");
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

    // ── Dictionary ────────────────────────────────────────────────────────

    private record WithDictionary { public Dictionary<string, int> Counts { get; init; } = []; }

    private record WithReadOnlyDictionary { public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>(); }

    [Test]
    public void Generate_DictionaryProperty_UsesAdditionalProperties()
    {
        var schema = JsonSchemaGenerator.GenerateForType(typeof(WithDictionary));
        var props = (Dictionary<string, object>)schema["properties"];
        var countsSchema = (Dictionary<string, object>)props["Counts"];

        ((string)countsSchema["type"]).ShouldBe("object");
        var additionalProps = (Dictionary<string, object>)countsSchema["additionalProperties"];
        ((string)additionalProps["type"]).ShouldBe("integer");
    }

    [Test]
    public void Generate_ReadOnlyDictionaryProperty_UsesAdditionalProperties()
    {
        var schema = JsonSchemaGenerator.GenerateForType(typeof(WithReadOnlyDictionary));
        var props = (Dictionary<string, object>)schema["properties"];
        var labelsSchema = (Dictionary<string, object>)props["Labels"];

        ((string)labelsSchema["type"]).ShouldBe("object");
        var additionalProps = (Dictionary<string, object>)labelsSchema["additionalProperties"];
        ((string)additionalProps["type"]).ShouldBe("string");
    }

    // ── JsonIgnore ────────────────────────────────────────────────────────

    private record WithIgnored
    {
        public string Visible { get; init; } = string.Empty;
        [JsonIgnore] public string Hidden { get; init; } = string.Empty;
    }

    [Test]
    public void Generate_JsonIgnoreProperty_ExcludedFromSchema()
    {
        var schema = JsonSchemaGenerator.GenerateForType(typeof(WithIgnored));
        var props = (Dictionary<string, object>)schema["properties"];

        props.ShouldContainKey("Visible");
        props.ShouldNotContainKey("Hidden");

        var required = (List<string>)schema["required"];
        required.ShouldNotContain("Hidden");
    }

    // ── Recursive type depth guard ────────────────────────────────────────

    private class SelfReferencing
    {
        public string Name { get; init; } = string.Empty;
        public SelfReferencing? Child { get; init; }
    }

    [Test]
    public void Generate_RecursiveType_DoesNotThrow()
    {
        Should.NotThrow(() => JsonSchemaGenerator.GenerateForType(typeof(SelfReferencing)));
    }

    [Test]
    public void Generate_RecursiveType_StopsAtMaxDepth()
    {
        var schema = JsonSchemaGenerator.GenerateForType(typeof(SelfReferencing));

        // Walk as deep as the schema goes; must not be infinite
        static int MaxSchemaDepth(Dictionary<string, object> s, int current = 0)
        {
            if (!s.TryGetValue("properties", out var propsObj))
                return current;
            var props = (Dictionary<string, object>)propsObj;
            var deepest = current;
            foreach (var v in props.Values)
            {
                if (v is Dictionary<string, object> nested)
                    deepest = Math.Max(deepest, MaxSchemaDepth(nested, current + 1));
            }
            return deepest;
        }

        MaxSchemaDepth(schema).ShouldBeLessThanOrEqualTo(12); // generous bound above MaxDepth=10
    }
}
