using System.Text.Json;
using System.Text.Json.Nodes;

namespace Csv2Zugferd.Mcp;

/// <summary>
/// Kleine Helfer zum Aufbau der Werkzeugschemata (JSON Schema Draft 2020-12). Jeder Aufruf erzeugt frische
/// Knoten, weil ein JsonNode nur einen Elternknoten haben darf. Objektknoten tragen immer
/// <c>additionalProperties: false</c>; Dictionaries werden ausdrücklich über <see cref="Map"/> modelliert.
/// </summary>
internal static class SchemaBuilder
{
    public const string Draft202012 = "https://json-schema.org/draft/2020-12/schema";

    public static JsonObject Object(string description, IEnumerable<KeyValuePair<string, JsonNode>> properties, params string[] required)
    {
        var props = new JsonObject();
        foreach (var (name, schema) in properties)
            props[name] = schema;

        var node = new JsonObject
        {
            ["type"] = "object",
            ["description"] = description,
            ["properties"] = props,
            ["additionalProperties"] = false,
        };
        if (required.Length > 0)
            node["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray());
        return node;
    }

    /// <summary>Dictionary mit frei wählbaren Schlüsseln: explizites <c>patternProperties</c>, keine offenen Objekte.</summary>
    public static JsonObject Map(string description, JsonNode valueSchema, string keyPattern = "^.*$")
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["description"] = description,
            ["patternProperties"] = new JsonObject { [keyPattern] = valueSchema },
            ["additionalProperties"] = false,
        };
    }

    public static JsonObject String(string description, string? defaultValue = null, int? minLength = null, string? pattern = null)
    {
        var node = new JsonObject { ["type"] = "string", ["description"] = description };
        if (defaultValue != null) node["default"] = defaultValue;
        if (minLength != null) node["minLength"] = minLength;
        if (pattern != null) node["pattern"] = pattern;
        return node;
    }

    public static JsonObject NullableString(string description)
        => new() { ["type"] = new JsonArray("string", "null"), ["description"] = description };

    public static JsonObject Enum(string description, IEnumerable<string> values, string? defaultValue = null)
    {
        var node = new JsonObject
        {
            ["type"] = "string",
            ["description"] = description,
            ["enum"] = new JsonArray(values.Select(v => (JsonNode)v).ToArray()),
        };
        if (defaultValue != null) node["default"] = defaultValue;
        return node;
    }

    public static JsonObject Integer(string description, int? minimum = null, int? maximum = null, int? defaultValue = null)
    {
        var node = new JsonObject { ["type"] = "integer", ["description"] = description };
        if (minimum != null) node["minimum"] = minimum;
        if (maximum != null) node["maximum"] = maximum;
        if (defaultValue != null) node["default"] = defaultValue;
        return node;
    }

    public static JsonObject Number(string description)
        => new() { ["type"] = "number", ["description"] = description };

    public static JsonObject Boolean(string description, bool? defaultValue = null)
    {
        var node = new JsonObject { ["type"] = "boolean", ["description"] = description };
        if (defaultValue != null) node["default"] = defaultValue;
        return node;
    }

    public static JsonObject Array(string description, JsonNode items)
        => new() { ["type"] = "array", ["description"] = description, ["items"] = items };

    public static JsonObject Ref(string definition, string? description = null)
    {
        var node = new JsonObject { ["$ref"] = $"#/$defs/{definition}" };
        if (description != null) node["description"] = description;
        return node;
    }

    public static JsonObject OneOf(string description, params JsonNode[] alternatives)
        => new() { ["description"] = description, ["oneOf"] = new JsonArray(alternatives) };

    public static KeyValuePair<string, JsonNode> Prop(string name, JsonNode schema) => new(name, schema);

    /// <summary>Unveränderliche Form für <c>Tool.InputSchema</c>/<c>OutputSchema</c> und den Prüfer.</summary>
    public static JsonElement ToElement(JsonNode node) => JsonSerializer.SerializeToElement(node);
}
