using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Csv2Zugferd.Models;
using YamlDotNet.Serialization.NamingConventions;

namespace Csv2Zugferd.Mcp;

/// <summary>
/// Baut das JSON-Schema (Draft 2020-12) der YAML-Konfiguration per Reflection aus den Modellklassen,
/// den <c>[Description]</c>-Attributen, <see cref="ConfigSemantics"/> und <see cref="ConfigValues"/>.
/// Gemeinsame Strukturen (Feld-Mapping, Wertevorräte) liegen als <c>$defs</c>; jedes Objekt trägt
/// <c>additionalProperties: false</c>; Dictionaries sind explizit modelliert. Einmal gebaut und zwischengespeichert.
/// </summary>
public static class ConfigSchemaBuilder
{
    private static readonly Lazy<JsonElement> s_schema = new(Build);

    /// <summary>Das vollständige Schema als unveränderliches Element.</summary>
    public static JsonElement Schema => s_schema.Value;

    /// <summary>Alle Punktpfade der Konfiguration, die das Schema beschreibt (für Vollständigkeitstests).</summary>
    public static IReadOnlyList<string> DescribedPaths()
    {
        var paths = new List<string>();
        CollectPaths(typeof(AppConfig), string.Empty, paths);
        return paths;
    }

    /// <summary>Zulässige Werte des Arguments <c>section</c>: jeder beschriebene Pfad plus die drei expliziten fixedFields-Schlüssel.</summary>
    public static IReadOnlyList<string> SectionPaths { get; } =
        DescribedPaths().Concat(YamlConfigLoader.EffectiveFixedFieldKeys.Select(k => "mapping.lineItems.fixedFields." + k)).ToList();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, JsonElement> s_sections = new(StringComparer.Ordinal);

    /// <summary>
    /// Teilbaum des Schemas für einen Konfigurationspfad, als eigenständiges Schema mit genau den transitiv
    /// referenzierten <c>$defs</c>. Unbekannte Pfade lehnt bereits das Eingabeschema (enum) ab.
    /// </summary>
    public static JsonElement GetSection(string section)
    {
        return s_sections.GetOrAdd(section, static key =>
        {
            var root = JsonNode.Parse(Schema.GetRawText())!.AsObject();
            JsonNode current = root;
            foreach (var segment in key.Split('.'))
            {
                var properties = current.AsObject()["properties"]?.AsObject()
                    ?? throw new ArgumentException($"Pfad {key} hat keine Eigenschaften");
                current = properties[segment] ?? throw new ArgumentException($"Unbekannter Abschnitt: {key}");
            }

            var node = current.DeepClone().AsObject();
            var allDefs = root["$defs"]!.AsObject();
            var needed = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
            CollectReferencedDefs(node, allDefs, needed);

            var result = new JsonObject
            {
                ["$schema"] = SchemaBuilder.Draft202012,
                ["title"] = $"csv2zugferd configuration: {key}",
            };
            foreach (var (name, value) in node)
                result[name] = value?.DeepClone();
            if (needed.Count > 0)
            {
                var defs = new JsonObject();
                foreach (var (name, value) in needed.OrderBy(d => d.Key, StringComparer.Ordinal))
                    defs[name] = value.DeepClone();
                result["$defs"] = defs;
            }
            return SchemaBuilder.ToElement(result);
        });
    }

    private static void CollectReferencedDefs(JsonNode? node, JsonObject allDefs, Dictionary<string, JsonNode> needed)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, value) in obj)
                {
                    if (name == "$ref" && value is JsonValue refValue && refValue.GetValue<string>() is var reference && reference.StartsWith("#/$defs/", StringComparison.Ordinal))
                    {
                        var defName = reference["#/$defs/".Length..];
                        if (!needed.ContainsKey(defName) && allDefs[defName] is { } def)
                        {
                            needed[defName] = def;
                            CollectReferencedDefs(def, allDefs, needed);
                        }
                    }
                    else
                    {
                        CollectReferencedDefs(value, allDefs, needed);
                    }
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                    CollectReferencedDefs(item, allDefs, needed);
                break;
        }
    }

    private static JsonElement Build()
    {
        var defs = new JsonObject();

        // Wertevorräte als $defs, damit Felder und Regeln darauf verweisen können
        foreach (var set in ConfigValues.All)
            defs[ValueSetDefinitionName(set)] = BuildValueSetSchema(set);

        // Generisches Feld-Mapping und die feldabhängigen Varianten
        defs["fieldRule"] = BuildFieldRuleSchema(null);
        defs["fieldMapping"] = BuildFieldMappingSchema(null, null);
        foreach (var set in ConfigValues.All.Where(s => IsFieldMappingValueSet(s.Name)))
        {
            defs[FieldMappingDefinitionName(set)] = BuildFieldMappingSchema(set, null);
            defs[FieldRuleDefinitionName(set)] = BuildFieldRuleSchema(set);
        }
        defs["electronicAddressMapping"] = BuildFieldMappingSchema(null, ConfigValues.ElectronicAddressScheme);

        var root = BuildObjectSchema(typeof(AppConfig), string.Empty, defs);
        root["$schema"] = SchemaBuilder.Draft202012;
        root["title"] = "csv2zugferd configuration";
        root["description"] = "YAML configuration of csv2zugferd (CSV settings, ZUGFeRD output settings and field mapping). Keys are lowerCamelCase. Resolution order of every field mapping: value, column, rules, default.";
        root["$defs"] = defs;

        return SchemaBuilder.ToElement(root);
    }

    // --- Objekte und Eigenschaften ---

    private static JsonObject BuildObjectSchema(Type type, string path, JsonObject defs)
    {
        var properties = new JsonObject();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = Camel(property.Name);
            var propertyPath = path.Length == 0 ? name : $"{path}.{name}";
            properties[name] = BuildPropertySchema(property, propertyPath, defs);
        }

        var node = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false,
        };
        var semantic = ConfigSemantics.Find(path);
        node["description"] = semantic != null
            ? DescribeStructural(type, semantic)
            : $"{type.Name}: one entry of {path.TrimEnd('[', ']')}.";
        return node;
    }

    private static JsonNode BuildPropertySchema(PropertyInfo property, string path, JsonObject defs)
    {
        var description = Describe(property, path);
        var semantic = ConfigSemantics.Find(path) ?? throw new InvalidOperationException($"Keine Semantik für {path}");
        var type = property.PropertyType;
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        JsonObject node;
        if (type == typeof(FieldMapping))
        {
            node = SchemaBuilder.Ref(FieldMappingReference(semantic));
        }
        else if (type == typeof(string))
        {
            node = semantic.ValueSet != null
                ? SchemaBuilder.Ref(ValueSetDefinitionName(ConfigValues.FindSet(semantic.ValueSet)!))
                : new JsonObject { ["type"] = "string" };
        }
        else if (underlying == typeof(bool))
        {
            node = new JsonObject { ["type"] = "boolean" };
        }
        else if (underlying == typeof(int))
        {
            node = new JsonObject { ["type"] = "integer" };
        }
        else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var element = type.GetGenericArguments()[0];
            node = new JsonObject
            {
                ["type"] = "array",
                ["items"] = element == typeof(FieldMapping)
                    ? SchemaBuilder.Ref("fieldMapping")
                    : BuildObjectSchema(element, path + "[]", defs),
            };
        }
        else if (type == typeof(Dictionary<string, FieldMapping>))
        {
            // fixedFields: nur die drei wirksamen Schlüssel sind zulässig
            var fixedProperties = new JsonObject();
            foreach (var key in YamlConfigLoader.EffectiveFixedFieldKeys)
            {
                var keySemantic = ConfigSemantics.Find($"{path}.{key}") ?? throw new InvalidOperationException($"Keine Semantik für {path}.{key}");
                var keyNode = SchemaBuilder.Ref(FieldMappingReference(keySemantic));
                keyNode["description"] = keySemantic.Description;
                fixedProperties[key] = keyNode;
            }
            node = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = fixedProperties,
                ["additionalProperties"] = false,
            };
        }
        else if (type == typeof(Dictionary<string, string>))
        {
            node = SchemaBuilder.Map(description, new JsonObject { ["type"] = "string", ["description"] = "Ignored value; the key is never read." });
            node["deprecated"] = true;
        }
        else if (type.Namespace == "Csv2Zugferd.Models" && type.IsClass)
        {
            node = BuildObjectSchema(type, path, defs);
        }
        else
        {
            throw new NotSupportedException($"Typ {type} an {path} wird im Schema nicht unterstützt");
        }

        node["description"] = description;
        if (semantic.Status == FieldStatus.Ignored)
            node["deprecated"] = true;
        if (semantic.DefaultValue != null && semantic.ValueSet != "booleanText" && !IsFieldMappingType(type))
            node["default"] = ToDefaultNode(semantic.DefaultValue, underlying);
        return node;
    }

    private static JsonNode ToDefaultNode(string value, Type type)
    {
        if (type == typeof(bool))
            return bool.Parse(value);
        if (type == typeof(int))
            return int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        return value;
    }

    private static string Describe(PropertyInfo property, string path)
    {
        var attribute = property.GetCustomAttribute<DescriptionAttribute>()?.Description
            ?? throw new InvalidOperationException($"Keine Beschreibung an {property.DeclaringType?.Name}.{property.Name}");
        var semantic = ConfigSemantics.Find(path);
        if (semantic == null)
            return attribute;

        var parts = new List<string> { attribute };
        if (semantic.Status == FieldStatus.Ignored)
            parts.Add("IGNORED: accepted but without effect.");
        else if (semantic.Status == FieldStatus.Conditional && semantic.Modes.Count > 0)
            parts.Add($"Effective only in mode: {string.Join(", ", semantic.Modes)}.");
        else if (semantic.Status == FieldStatus.Conditional)
            parts.Add("Conditional, see semantics.");
        if (semantic.Sources.Count > 0)
            parts.Add($"Resolution order: {string.Join(", ", semantic.Sources)}.");
        if (semantic.ValueSet != null)
            parts.Add($"Value set: {semantic.ValueSet}.");
        if (semantic.InvalidFallback != null)
            parts.Add($"Unknown values fall back to {semantic.InvalidFallback}.");
        return string.Join(" ", parts);
    }

    private static string DescribeStructural(Type type, FieldSemantic semantic) =>
        $"{type.Name}: {semantic.Description}";

    // --- Feld-Mapping und Regeln ---

    private static string FieldMappingReference(FieldSemantic semantic)
    {
        if (semantic.Path.EndsWith(".electronicAddress", StringComparison.Ordinal))
            return "electronicAddressMapping";
        if (semantic.ValueSet != null && IsFieldMappingValueSet(semantic.ValueSet))
            return FieldMappingDefinitionName(ConfigValues.FindSet(semantic.ValueSet)!);
        return "fieldMapping";
    }

    private static bool IsFieldMappingValueSet(string name) =>
        name is "currency" or "country" or "unitCode" or "taxType" or "taxCategoryCode" or "taxScheme"
            or "invoiceType" or "paymentMeansType" or "booleanText";

    private static JsonObject BuildFieldMappingSchema(ValueSet? valueSet, ValueSet? schemeSet)
    {
        JsonNode ValueNode(string description)
        {
            if (valueSet == null)
                return SchemaBuilder.String(description);
            var node = SchemaBuilder.Ref(ValueSetDefinitionName(valueSet));
            node["description"] = description + $" Allowed values: see value set {valueSet.Name}.";
            return node;
        }

        var scheme = schemeSet == null
            ? SchemaBuilder.String(ConfigSemantics.Find("fieldMapping.scheme")!.Description)
            : SchemaBuilder.Ref(ValueSetDefinitionName(schemeSet), "Scheme of the electronic address (BT-34/BT-49). " + schemeSet.Description);

        var node = SchemaBuilder.Object(
            valueSet == null
                ? "Field mapping: fixed value, CSV column, regex rules and fallback. Resolution order: value, column, rules, default."
                : $"Field mapping whose value, default and rule results must come from the value set {valueSet.Name}. Resolution order: value, column, rules, default.",
            [
                SchemaBuilder.Prop("value", ValueNode(ConfigSemantics.Find("fieldMapping.value")!.Description)),
                SchemaBuilder.Prop("column", SchemaBuilder.String(ConfigSemantics.Find("fieldMapping.column")!.Description)),
                SchemaBuilder.Prop("default", ValueNode(ConfigSemantics.Find("fieldMapping.default")!.Description)),
                SchemaBuilder.Prop("scheme", scheme),
                SchemaBuilder.Prop("rules", SchemaBuilder.Array(ConfigSemantics.Find("fieldMapping.rules")!.Description,
                    SchemaBuilder.Ref(valueSet == null ? "fieldRule" : FieldRuleDefinitionName(valueSet)))),
            ]);
        return node;
    }

    private static JsonObject BuildFieldRuleSchema(ValueSet? valueSet)
    {
        JsonNode value = valueSet == null
            ? SchemaBuilder.String(ConfigSemantics.Find("fieldMapping.rules[].value")!.Description)
            : SchemaBuilder.Ref(ValueSetDefinitionName(valueSet), ConfigSemantics.Find("fieldMapping.rules[].value")!.Description);

        return SchemaBuilder.Object("Regex rule: when the column value matches the regex, value is used.",
        [
            SchemaBuilder.Prop("when", SchemaBuilder.Object(ConfigSemantics.Find("fieldMapping.rules[].when")!.Description,
            [
                SchemaBuilder.Prop("column", SchemaBuilder.String(ConfigSemantics.Find("fieldMapping.rules[].when.column")!.Description)),
                SchemaBuilder.Prop("regex", SchemaBuilder.String(ConfigSemantics.Find("fieldMapping.rules[].when.regex")!.Description)),
            ], "column", "regex")),
            SchemaBuilder.Prop("value", value),
        ], "when", "value");
    }

    // --- Wertevorräte ---

    public static string ValueSetDefinitionName(ValueSet set) => set.Name + "Value";
    private static string FieldMappingDefinitionName(ValueSet set) => set.Name + "FieldMapping";
    private static string FieldRuleDefinitionName(ValueSet set) => set.Name + "FieldRule";

    /// <summary>
    /// Kanonische Werte als <c>enum</c>; weitere akzeptierte Schreibweisen (Groß-/Kleinschreibung, Aliase, Zahlen)
    /// als zweite Alternative mit <c>pattern</c>, damit das Schema genau die Menge beschreibt, die der Parser kennt.
    /// </summary>
    private static JsonObject BuildValueSetSchema(ValueSet set)
    {
        var canonical = new JsonObject
        {
            ["type"] = "string",
            ["enum"] = new JsonArray(set.Canonical.Select(v => (JsonNode)v).ToArray()),
        };

        var alternatives = new JsonArray { canonical };
        var patterns = new List<string>();
        var spellings = set.Canonical.Concat(set.Aliases).Distinct(StringComparer.Ordinal).ToList();
        var trimsWhitespace = set.Name == "invoiceType" || set.Name == "paymentMeansType";
        if (trimsWhitespace)
            patterns.Add("^\\s*(?:" + string.Join("|", spellings.Select(CaseInsensitivePattern)) + ")\\s*$");
        else if (set.CaseInsensitive || set.Aliases.Count > 0)
            patterns.Add("^(?:" + string.Join("|", spellings.Select(v => set.CaseInsensitive ? CaseInsensitivePattern(v) : EscapeRegex(v))) + ")$");
        if (set.AcceptsInteger)
            patterns.Add("^[0-9]+$");

        foreach (var pattern in patterns)
            alternatives.Add(new JsonObject { ["type"] = "string", ["pattern"] = pattern });
        if (set.Name == "booleanText")
            alternatives.Add(new JsonObject { ["type"] = "boolean", ["description"] = "YAML boolean; the loader reads it as text true or false." });

        var description = set.Description + (set.InvalidFallback == null
            ? string.Empty
            : $" Fallback for unknown values: {set.InvalidFallback}.");

        return new JsonObject
        {
            ["description"] = $"Value set {set.Name}. Canonical values are listed in the first alternative; the other alternatives describe further accepted spellings. {description}",
            ["anyOf"] = alternatives,
        };
    }

    private static string CaseInsensitivePattern(string value) =>
        string.Concat(value.Select(c => char.IsAsciiLetter(c) ? $"[{char.ToUpperInvariant(c)}{char.ToLowerInvariant(c)}]" : EscapeRegex(c.ToString())));

    private static string EscapeRegex(string value) =>
        System.Text.RegularExpressions.Regex.Escape(value).Replace("/", "\\/");

    // --- Pfade ---

    private static void CollectPaths(Type type, string prefix, List<string> paths)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var path = prefix.Length == 0 ? Camel(property.Name) : $"{prefix}.{Camel(property.Name)}";
            paths.Add(path);
            var type2 = property.PropertyType;
            if (type2 == typeof(FieldMapping) || type2 == typeof(string) || type2.IsValueType)
                continue;
            if (type2.IsGenericType && type2.GetGenericTypeDefinition() == typeof(List<>))
            {
                var element = type2.GetGenericArguments()[0];
                if (element != typeof(FieldMapping) && element.Namespace == "Csv2Zugferd.Models")
                    CollectPaths(element, path + "[]", paths);
                continue;
            }
            if (type2.Namespace == "Csv2Zugferd.Models" && type2.IsClass)
                CollectPaths(type2, path, paths);
        }
    }

    private static bool IsFieldMappingType(Type type) => type == typeof(FieldMapping);

    private static string Camel(string name) => CamelCaseNamingConvention.Instance.Apply(name);
}
