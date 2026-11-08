using System.Text.Json;
using Csv2Zugferd.Mcp;
using NUnit.Framework;
using Shouldly;

namespace Csv2Zugferd.Tests.Mcp;

[TestFixture]
public class JsonSchemaValidatorTests
{
    private const string Schema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "required": ["name", "mode"],
          "properties": {
            "name": { "type": "string", "minLength": 1 },
            "mode": { "type": "string", "enum": ["rows", "columns"] },
            "count": { "type": "integer", "minimum": 0, "maximum": 3 },
            "field": { "$ref": "#/$defs/fieldMapping" },
            "source": {
              "oneOf": [
                { "type": "object", "additionalProperties": false, "required": ["path"], "properties": { "path": { "type": "string" } } },
                { "type": "object", "additionalProperties": false, "required": ["yaml"], "properties": { "yaml": { "type": "string" } } }
              ]
            },
            "fixed": {
              "type": "object",
              "additionalProperties": false,
              "patternProperties": { "^[a-z]+$": { "type": "string" } }
            },
            "tags": { "type": "array", "items": { "type": "string" }, "maxItems": 2 },
            "note": { "type": ["string", "null"] }
          },
          "$defs": {
            "fieldMapping": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "column": { "type": "string" },
                "value": { "type": "string" }
              }
            }
          }
        }
        """;

    private static JsonSchemaValidator CreateValidator() => JsonSchemaValidator.FromJson(Schema);

    private static IReadOnlyList<SchemaViolation> Validate(string json)
    {
        using var document = JsonDocument.Parse(json);
        return CreateValidator().Validate(document.RootElement);
    }

    [Test]
    public void Validate_ValidInstance_ReturnsNoViolations()
    {
        var violations = Validate("""
            { "name": "a", "mode": "rows", "count": 2, "field": { "column": "X" }, "source": { "path": "c.yml" },
              "fixed": { "unitcode": "C62" }, "tags": ["a", "b"], "note": null }
            """);

        violations.ShouldBeEmpty();
    }

    [Test]
    public void Validate_MissingRequired_ReportsFieldPath()
    {
        var violations = Validate("""{ "name": "a" }""");

        violations.ShouldHaveSingleItem().Path.ShouldBe("mode");
    }

    [Test]
    public void Validate_UnknownProperty_ReportsFieldPath()
    {
        var violations = Validate("""{ "name": "a", "mode": "rows", "typo": 1 }""");

        violations.ShouldHaveSingleItem().Path.ShouldBe("typo");
    }

    [Test]
    public void Validate_NestedUnknownPropertyViaRef_ReportsDottedPath()
    {
        var violations = Validate("""{ "name": "a", "mode": "rows", "field": { "colum": "X" } }""");

        violations.ShouldHaveSingleItem().Path.ShouldBe("field.colum");
    }

    [Test]
    public void Validate_EnumMismatch_ReportsViolation()
    {
        var violations = Validate("""{ "name": "a", "mode": "Rows" }""");

        violations.ShouldHaveSingleItem().Path.ShouldBe("mode");
    }

    [Test]
    public void Validate_IntegerOutOfRangeAndFraction_ReportsViolations()
    {
        Validate("""{ "name": "a", "mode": "rows", "count": 4 }""").ShouldHaveSingleItem().Path.ShouldBe("count");
        Validate("""{ "name": "a", "mode": "rows", "count": 1.5 }""").ShouldHaveSingleItem().Path.ShouldBe("count");
        Validate("""{ "name": "a", "mode": "rows", "count": "1" }""").ShouldHaveSingleItem().Path.ShouldBe("count");
    }

    [Test]
    public void Validate_OneOf_RequiresExactlyOneAlternative()
    {
        Validate("""{ "name": "a", "mode": "rows", "source": { "path": "p", "yaml": "y" } }""").ShouldHaveSingleItem().Path.ShouldBe("source");
        Validate("""{ "name": "a", "mode": "rows", "source": {} }""").ShouldHaveSingleItem().Path.ShouldBe("source");
    }

    [Test]
    public void Validate_PatternProperties_RejectsKeyOutsidePattern()
    {
        var violations = Validate("""{ "name": "a", "mode": "rows", "fixed": { "unitCode": "C62" } }""");

        violations.ShouldHaveSingleItem().Path.ShouldBe("fixed.unitCode");
    }

    [Test]
    public void Validate_ArrayItemsAndMaxItems_ReportIndexedPaths()
    {
        Validate("""{ "name": "a", "mode": "rows", "tags": ["a", 1] }""").ShouldHaveSingleItem().Path.ShouldBe("tags[1]");
        Validate("""{ "name": "a", "mode": "rows", "tags": ["a", "b", "c"] }""").ShouldHaveSingleItem().Path.ShouldBe("tags");
    }

    [Test]
    public void Validate_NullableType_AcceptsNullAndRejectsNumber()
    {
        Validate("""{ "name": "a", "mode": "rows", "note": null }""").ShouldBeEmpty();
        Validate("""{ "name": "a", "mode": "rows", "note": 5 }""").ShouldHaveSingleItem().Path.ShouldBe("note");
    }

    [Test]
    public void Constructor_UnknownValidationKeyword_Throws()
    {
        Should.Throw<NotSupportedException>(() => JsonSchemaValidator.FromJson("""{ "type": "object", "unevaluatedProperties": false }"""));
    }

    [Test]
    public void Validate_RootTypeMismatch_ReportsRootPath()
    {
        var violations = Validate("[]");

        violations.ShouldContain(v => v.Path == "" && v.ToString().StartsWith("(root)"));
    }
}
