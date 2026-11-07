using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Csv2Zugferd.Mcp;

/// <summary>Verstoß gegen ein Schema: Instanzpfad in Punktschreibweise (<c>a.b[0].c</c>, leer = Wurzel) und deutscher Text.</summary>
public sealed record SchemaViolation(string Path, string Message)
{
    public override string ToString() => $"{(Path.Length == 0 ? "(root)" : Path)}: {Message}";
}

/// <summary>
/// Prüfer für den in den Werkzeugverträgen verwendeten Teilumfang von JSON Schema Draft 2020-12.
/// Eigene Umsetzung ohne Zusatzpaket (Analogie zu CMcpSchema der RP-Richtlinie). Der Prüfer arbeitet
/// geschlossen: Ein Schema mit einem unbekannten Prüf-Schlüsselwort wird beim Erzeugen abgelehnt,
/// damit keine Zusage still ungeprüft bleibt.
/// </summary>
public sealed class JsonSchemaValidator
{
    private static readonly HashSet<string> s_annotationKeywords =
    [
        "$schema", "$id", "$comment", "title", "description", "default", "deprecated", "examples",
        "readOnly", "writeOnly", "format", "$defs",
    ];

    private static readonly HashSet<string> s_validationKeywords =
    [
        "$ref", "type", "enum", "const", "properties", "required", "additionalProperties", "patternProperties",
        "propertyNames", "minProperties", "maxProperties", "dependentRequired", "items", "prefixItems", "minItems",
        "maxItems", "uniqueItems", "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf",
        "minLength", "maxLength", "pattern", "oneOf", "anyOf", "allOf", "not",
    ];

    private readonly JsonElement _root;
    private readonly Dictionary<string, Regex> _regexCache = new(StringComparer.Ordinal);

    public JsonSchemaValidator(JsonElement schema)
    {
        _root = schema.Clone();
        CheckKeywords(_root, "#");
    }

    public static JsonSchemaValidator FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return new JsonSchemaValidator(document.RootElement);
    }

    public IReadOnlyList<SchemaViolation> Validate(JsonElement instance)
    {
        var violations = new List<SchemaViolation>();
        ValidateNode(_root, instance, string.Empty, violations);
        return violations;
    }

    public bool IsValid(JsonElement instance) => Validate(instance).Count == 0;

    // --- Schemaprüfung beim Erzeugen (fail-closed) ---

    private static void CheckKeywords(JsonElement schema, string location)
    {
        if (schema.ValueKind == JsonValueKind.True || schema.ValueKind == JsonValueKind.False)
            return;
        if (schema.ValueKind != JsonValueKind.Object)
            throw new NotSupportedException($"Schema an {location} ist weder Objekt noch boolescher Wert");

        foreach (var property in schema.EnumerateObject())
        {
            var name = property.Name;
            if (s_annotationKeywords.Contains(name))
            {
                if (name == "$defs")
                {
                    foreach (var def in property.Value.EnumerateObject())
                        CheckKeywords(def.Value, $"{location}/$defs/{def.Name}");
                }
                continue;
            }

            if (!s_validationKeywords.Contains(name))
                throw new NotSupportedException($"Nicht unterstütztes Schema-Schlüsselwort '{name}' an {location}");

            switch (name)
            {
                case "properties":
                case "patternProperties":
                    foreach (var sub in property.Value.EnumerateObject())
                        CheckKeywords(sub.Value, $"{location}/{name}/{sub.Name}");
                    break;
                case "additionalProperties":
                case "propertyNames":
                case "items":
                case "not":
                    CheckKeywords(property.Value, $"{location}/{name}");
                    break;
                case "prefixItems":
                case "oneOf":
                case "anyOf":
                case "allOf":
                    var index = 0;
                    foreach (var sub in property.Value.EnumerateArray())
                        CheckKeywords(sub, $"{location}/{name}/{index++}");
                    break;
            }
        }
    }

    // --- Instanzprüfung ---

    private void ValidateNode(JsonElement schema, JsonElement instance, string path, List<SchemaViolation> violations)
    {
        if (schema.ValueKind == JsonValueKind.True)
            return;
        if (schema.ValueKind == JsonValueKind.False)
        {
            violations.Add(new SchemaViolation(path, "Wert ist hier nicht zulässig"));
            return;
        }

        if (schema.TryGetProperty("$ref", out var reference))
            ValidateNode(Resolve(reference.GetString()!), instance, path, violations);

        if (schema.TryGetProperty("type", out var type) && !MatchesType(type, instance))
            violations.Add(new SchemaViolation(path, $"Typ {DescribeType(instance)} erwartet {DescribeExpected(type)}"));

        if (schema.TryGetProperty("enum", out var enumValues) && !enumValues.EnumerateArray().Any(v => JsonElement.DeepEquals(v, instance)))
            violations.Add(new SchemaViolation(path, $"Wert {Short(instance)} nicht in der Werteliste [{string.Join(", ", enumValues.EnumerateArray().Select(Short))}]"));

        if (schema.TryGetProperty("const", out var constValue) && !JsonElement.DeepEquals(constValue, instance))
            violations.Add(new SchemaViolation(path, $"Wert {Short(instance)} muss {Short(constValue)} sein"));

        if (instance.ValueKind == JsonValueKind.Object)
            ValidateObject(schema, instance, path, violations);
        else if (instance.ValueKind == JsonValueKind.Array)
            ValidateArray(schema, instance, path, violations);
        else if (instance.ValueKind == JsonValueKind.String)
            ValidateString(schema, instance, path, violations);
        else if (instance.ValueKind == JsonValueKind.Number)
            ValidateNumber(schema, instance, path, violations);

        if (schema.TryGetProperty("allOf", out var allOf))
        {
            foreach (var sub in allOf.EnumerateArray())
                ValidateNode(sub, instance, path, violations);
        }

        if (schema.TryGetProperty("anyOf", out var anyOf))
        {
            var matches = anyOf.EnumerateArray().Count(sub => Matches(sub, instance));
            if (matches == 0)
                violations.Add(new SchemaViolation(path, "Wert entspricht keiner der zulässigen Alternativen (anyOf)"));
        }

        if (schema.TryGetProperty("oneOf", out var oneOf))
        {
            var matches = oneOf.EnumerateArray().Count(sub => Matches(sub, instance));
            if (matches != 1)
                violations.Add(new SchemaViolation(path, matches == 0
                    ? "Wert entspricht keiner der Alternativen (oneOf)"
                    : $"Wert entspricht {matches} Alternativen, genau eine ist zulässig (oneOf)"));
        }

        if (schema.TryGetProperty("not", out var not) && Matches(not, instance))
            violations.Add(new SchemaViolation(path, "Wert entspricht einem ausgeschlossenen Schema (not)"));
    }

    private void ValidateObject(JsonElement schema, JsonElement instance, string path, List<SchemaViolation> violations)
    {
        var count = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        schema.TryGetProperty("properties", out var properties);
        schema.TryGetProperty("patternProperties", out var patternProperties);
        schema.TryGetProperty("additionalProperties", out var additional);
        schema.TryGetProperty("propertyNames", out var propertyNames);

        foreach (var member in instance.EnumerateObject())
        {
            count++;
            var memberPath = Join(path, member.Name);
            var covered = false;

            if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(member.Name, out var propertySchema))
            {
                covered = true;
                ValidateNode(propertySchema, member.Value, memberPath, violations);
            }

            if (patternProperties.ValueKind == JsonValueKind.Object)
            {
                foreach (var pattern in patternProperties.EnumerateObject())
                {
                    if (GetRegex(pattern.Name).IsMatch(member.Name))
                    {
                        covered = true;
                        ValidateNode(pattern.Value, member.Value, memberPath, violations);
                    }
                }
            }

            if (!covered && additional.ValueKind != JsonValueKind.Undefined)
            {
                if (additional.ValueKind == JsonValueKind.False)
                    violations.Add(new SchemaViolation(memberPath, "Unbekannte Eigenschaft"));
                else
                    ValidateNode(additional, member.Value, memberPath, violations);
            }

            if (propertyNames.ValueKind != JsonValueKind.Undefined)
            {
                using var nameDocument = JsonDocument.Parse(JsonSerializer.Serialize(member.Name));
                ValidateNode(propertyNames, nameDocument.RootElement, memberPath, violations);
            }

            seen.Add(member.Name);
        }

        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var name in required.EnumerateArray())
            {
                if (!seen.Contains(name.GetString()!))
                    violations.Add(new SchemaViolation(Join(path, name.GetString()!), "Pflichtfeld fehlt"));
            }
        }

        if (schema.TryGetProperty("dependentRequired", out var dependent))
        {
            foreach (var dependency in dependent.EnumerateObject())
            {
                if (!seen.Contains(dependency.Name)) continue;
                foreach (var name in dependency.Value.EnumerateArray())
                {
                    if (!seen.Contains(name.GetString()!))
                        violations.Add(new SchemaViolation(Join(path, name.GetString()!), $"Pflichtfeld fehlt, wenn '{dependency.Name}' angegeben ist"));
                }
            }
        }

        if (schema.TryGetProperty("minProperties", out var minProperties) && count < minProperties.GetInt32())
            violations.Add(new SchemaViolation(path, $"Mindestens {minProperties.GetInt32()} Eigenschaft(en) erwartet, {count} vorhanden"));
        if (schema.TryGetProperty("maxProperties", out var maxProperties) && count > maxProperties.GetInt32())
            violations.Add(new SchemaViolation(path, $"Höchstens {maxProperties.GetInt32()} Eigenschaft(en) zulässig, {count} vorhanden"));
    }

    private void ValidateArray(JsonElement schema, JsonElement instance, string path, List<SchemaViolation> violations)
    {
        var items = instance.EnumerateArray().ToList();
        schema.TryGetProperty("prefixItems", out var prefixItems);
        var prefixCount = prefixItems.ValueKind == JsonValueKind.Array ? prefixItems.GetArrayLength() : 0;

        for (var i = 0; i < items.Count; i++)
        {
            var itemPath = $"{path}[{i}]";
            if (i < prefixCount)
                ValidateNode(prefixItems[i], items[i], itemPath, violations);
            else if (schema.TryGetProperty("items", out var itemSchema))
                ValidateNode(itemSchema, items[i], itemPath, violations);
        }

        if (schema.TryGetProperty("minItems", out var minItems) && items.Count < minItems.GetInt32())
            violations.Add(new SchemaViolation(path, $"Mindestens {minItems.GetInt32()} Element(e) erwartet, {items.Count} vorhanden"));
        if (schema.TryGetProperty("maxItems", out var maxItems) && items.Count > maxItems.GetInt32())
            violations.Add(new SchemaViolation(path, $"Höchstens {maxItems.GetInt32()} Element(e) zulässig, {items.Count} vorhanden"));
        if (schema.TryGetProperty("uniqueItems", out var unique) && unique.ValueKind == JsonValueKind.True)
        {
            for (var i = 0; i < items.Count; i++)
            {
                for (var j = i + 1; j < items.Count; j++)
                {
                    if (JsonElement.DeepEquals(items[i], items[j]))
                    {
                        violations.Add(new SchemaViolation($"{path}[{j}]", "Element ist doppelt, Elemente müssen eindeutig sein"));
                        break;
                    }
                }
            }
        }
    }

    private void ValidateString(JsonElement schema, JsonElement instance, string path, List<SchemaViolation> violations)
    {
        var value = instance.GetString()!;
        var length = new StringInfo(value).LengthInTextElements;
        if (schema.TryGetProperty("minLength", out var minLength) && length < minLength.GetInt32())
            violations.Add(new SchemaViolation(path, $"Mindestens {minLength.GetInt32()} Zeichen erwartet"));
        if (schema.TryGetProperty("maxLength", out var maxLength) && length > maxLength.GetInt32())
            violations.Add(new SchemaViolation(path, $"Höchstens {maxLength.GetInt32()} Zeichen zulässig"));
        if (schema.TryGetProperty("pattern", out var pattern) && !GetRegex(pattern.GetString()!).IsMatch(value))
            violations.Add(new SchemaViolation(path, $"Wert {Short(instance)} entspricht nicht dem Muster {pattern.GetString()}"));
    }

    private static void ValidateNumber(JsonElement schema, JsonElement instance, string path, List<SchemaViolation> violations)
    {
        var value = instance.GetDecimal();
        if (schema.TryGetProperty("minimum", out var minimum) && value < minimum.GetDecimal())
            violations.Add(new SchemaViolation(path, $"Wert {value} unterschreitet das Minimum {minimum.GetDecimal()}"));
        if (schema.TryGetProperty("maximum", out var maximum) && value > maximum.GetDecimal())
            violations.Add(new SchemaViolation(path, $"Wert {value} überschreitet das Maximum {maximum.GetDecimal()}"));
        if (schema.TryGetProperty("exclusiveMinimum", out var exclusiveMinimum) && value <= exclusiveMinimum.GetDecimal())
            violations.Add(new SchemaViolation(path, $"Wert {value} muss größer als {exclusiveMinimum.GetDecimal()} sein"));
        if (schema.TryGetProperty("exclusiveMaximum", out var exclusiveMaximum) && value >= exclusiveMaximum.GetDecimal())
            violations.Add(new SchemaViolation(path, $"Wert {value} muss kleiner als {exclusiveMaximum.GetDecimal()} sein"));
        if (schema.TryGetProperty("multipleOf", out var multipleOf) && value % multipleOf.GetDecimal() != 0)
            violations.Add(new SchemaViolation(path, $"Wert {value} ist kein Vielfaches von {multipleOf.GetDecimal()}"));
    }

    private bool Matches(JsonElement schema, JsonElement instance)
    {
        var violations = new List<SchemaViolation>();
        ValidateNode(schema, instance, string.Empty, violations);
        return violations.Count == 0;
    }

    private JsonElement Resolve(string reference)
    {
        if (reference == "#")
            return _root;
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
            throw new NotSupportedException($"Nur lokale Verweise werden unterstützt: {reference}");

        var current = _root;
        foreach (var rawSegment in reference[2..].Split('/'))
        {
            var segment = Uri.UnescapeDataString(rawSegment).Replace("~1", "/").Replace("~0", "~");
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(segment, out var next))
                current = next;
            else if (current.ValueKind == JsonValueKind.Array && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < current.GetArrayLength())
                current = current[index];
            else
                throw new InvalidOperationException($"Verweis nicht auflösbar: {reference}");
        }
        return current;
    }

    private Regex GetRegex(string pattern)
    {
        if (!_regexCache.TryGetValue(pattern, out var regex))
        {
            regex = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.ECMAScript);
            _regexCache[pattern] = regex;
        }
        return regex;
    }

    private static bool MatchesType(JsonElement type, JsonElement instance)
    {
        if (type.ValueKind == JsonValueKind.Array)
            return type.EnumerateArray().Any(t => MatchesSingleType(t.GetString()!, instance));
        return MatchesSingleType(type.GetString()!, instance);
    }

    private static bool MatchesSingleType(string type, JsonElement instance) =>
        type switch
        {
            "object" => instance.ValueKind == JsonValueKind.Object,
            "array" => instance.ValueKind == JsonValueKind.Array,
            "string" => instance.ValueKind == JsonValueKind.String,
            "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => instance.ValueKind == JsonValueKind.Null,
            "number" => instance.ValueKind == JsonValueKind.Number,
            "integer" => instance.ValueKind == JsonValueKind.Number && instance.GetDecimal() == decimal.Truncate(instance.GetDecimal()),
            _ => throw new NotSupportedException($"Unbekannter Typ im Schema: {type}"),
        };

    private static string DescribeExpected(JsonElement type) =>
        type.ValueKind == JsonValueKind.Array
            ? string.Join(" oder ", type.EnumerateArray().Select(t => t.GetString()))
            : type.GetString()!;

    private static string DescribeType(JsonElement instance) =>
        instance.ValueKind switch
        {
            JsonValueKind.Object => "object",
            JsonValueKind.Array => "array",
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Null => "null",
            _ => instance.ValueKind.ToString().ToLowerInvariant(),
        };

    private static string Short(JsonElement element)
    {
        var text = element.GetRawText();
        return text.Length <= 60 ? text : text[..57] + "...";
    }

    private static string Join(string path, string name) =>
        path.Length == 0 ? name : $"{path}.{name}";
}
