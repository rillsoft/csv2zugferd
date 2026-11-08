using System.Reflection;
using Csv2Zugferd.Models;
using YamlDotNet.Serialization.NamingConventions;

namespace Csv2Zugferd.Mcp;

/// <summary>
/// Strukturelle Sicht auf eine geladene Konfiguration für <c>csv2zugferd_config_get</c>: konfigurierte Feld-Mappings
/// mit ihren Quellen, statisch prüfbare Werte gegen <see cref="ConfigValues"/> und der Spaltenabgleich nach der
/// Fachlogik des Mappers (<see cref="ColumnResolver"/>). Ob ein Zellwert leer ist oder eine Regel greift,
/// entscheidet ausschließlich der Probelauf.
/// </summary>
public static class ConfigInspector
{
    public sealed record ColumnCheck(IReadOnlyList<string> Referenced, IReadOnlyList<string>? Missing, IReadOnlyList<string>? Unused);

    /// <summary>Ein konfiguriertes Feld-Mapping mit Pfad; <c>IsLineItemField</c> markiert Positionsfelder (Fragmente im columns-Modus).</summary>
    private sealed record FieldReference(string Path, string SemanticPath, string? Value, string? Column, string? Default, List<FieldRule>? Rules, bool IsLineItemField);

    // --- Feld-Mappings ---

    public static IReadOnlyList<ToolResults.MappedField> CollectMappedFields(AppConfig config)
    {
        var result = new List<ToolResults.MappedField>();
        foreach (var reference in CollectReferences(config))
        {
            var sources = new List<string>();
            if (!string.IsNullOrEmpty(reference.Value)) sources.Add("value");
            if (!string.IsNullOrEmpty(reference.Column)) sources.Add("column");
            if (reference.Rules is { Count: > 0 }) sources.Add("rules");
            if (!string.IsNullOrEmpty(reference.Default)) sources.Add("default");
            result.Add(new ToolResults.MappedField(reference.Path, sources));
        }
        return result;
    }

    // --- Statische Wertprüfung ---

    public static IReadOnlyList<ToolResults.InvalidValue> CollectInvalidValues(AppConfig config)
    {
        var result = new List<ToolResults.InvalidValue>();

        void Check(string path, string? value, string setName)
        {
            if (string.IsNullOrEmpty(value)) return;
            var set = ConfigValues.FindSet(setName) ?? throw new InvalidOperationException($"Wertevorrat {setName} fehlt");
            if (!set.IsKnown(value))
                result.Add(new ToolResults.InvalidValue(path, value, set.Canonical, set.InvalidFallback));
        }

        Check("zugferd.version", config.Zugferd.Version, "version");
        Check("zugferd.profile", config.Zugferd.Profile, "profile");
        Check("zugferd.format", config.Zugferd.Format, "format");

        var lineItems = config.Mapping.LineItems;
        if (lineItems != null)
        {
            Check("mapping.lineItems.mode", lineItems.Mode, "lineItemsMode");
            if (lineItems.ColumnPattern != null)
                Check("mapping.lineItems.columnPattern.style", lineItems.ColumnPattern.Style, "columnStyle");
        }
        if (config.Mapping.Totals != null)
            Check("mapping.totals.mode", config.Mapping.Totals.Mode, "totalsMode");

        var registrations = config.Mapping.Seller?.TaxRegistrations;
        if (registrations != null)
        {
            for (var i = 0; i < registrations.Count; i++)
                Check($"mapping.seller.taxRegistrations[{i}].scheme", registrations[i].Scheme, "taxScheme");
        }

        Check("mapping.buyer.electronicAddress.scheme", config.Mapping.Buyer?.ElectronicAddress?.Scheme, "electronicAddressScheme");
        Check("mapping.seller.electronicAddress.scheme", config.Mapping.Seller?.ElectronicAddress?.Scheme, "electronicAddressScheme");

        foreach (var reference in CollectReferences(config))
        {
            var semantic = ConfigSemantics.Find(reference.SemanticPath);
            if (semantic?.ValueSet == null) continue;

            Check(reference.Path + ".value", reference.Value, semantic.ValueSet);
            Check(reference.Path + ".default", reference.Default, semantic.ValueSet);
            if (reference.Rules != null)
            {
                for (var i = 0; i < reference.Rules.Count; i++)
                    Check($"{reference.Path}.rules[{i}].value", reference.Rules[i].Value, semantic.ValueSet);
            }
        }

        return result;
    }

    // --- Spaltenabgleich ---

    /// <summary>
    /// Strukturell referenzierte Spalten nach Mapper-Semantik. Mit <paramref name="headers"/> zusätzlich fehlende
    /// Referenzen (ohne vorhandene Alternative) und unreferenzierte Spalten; Vergleiche case-insensitiv wie im Reader.
    /// </summary>
    public static ColumnCheck CheckColumns(AppConfig config, IReadOnlyList<string>? headers)
    {
        var headerSet = headers == null ? null : new HashSet<string>(headers, StringComparer.OrdinalIgnoreCase);
        var lineItems = config.Mapping.LineItems;
        var mode = ConfigValues.ParseLineItemsMode(lineItems?.Mode);
        var pattern = mode == ConfigValues.LineItemsModeColumns ? lineItems?.ColumnPattern : null;

        var nameFragment = lineItems?.Fields?.Name?.Column ?? ColumnResolver.DefaultNameFragment;
        var positions = pattern == null ? 0 : CountPositions(pattern, nameFragment, headerSet);

        var referenced = new List<string>();
        var missing = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddReference(string name, bool isFragment, bool optional = false)
        {
            var candidates = isFragment && pattern != null
                ? ColumnResolver.EnumerateCandidates(pattern, name, Math.Max(positions, 1)).ToList()
                : [name];

            foreach (var candidate in candidates)
            {
                if (!referenced.Contains(candidate, StringComparer.Ordinal))
                    referenced.Add(candidate);
            }

            if (headerSet == null) return;
            var present = candidates.Where(headerSet.Contains).ToList();
            if (present.Count == 0 && !optional)
                missing.Add(name);
            foreach (var candidate in present)
                used.Add(candidate);
        }

        foreach (var reference in CollectReferences(config))
        {
            if (!IsEffective(reference.SemanticPath, config)) continue;

            var isFragment = reference.IsLineItemField && pattern != null;
            if (isFragment && reference.SemanticPath == "mapping.lineItems.fields.name")
            {
                AddReference(nameFragment, true);
                continue;
            }

            if (!string.IsNullOrEmpty(reference.Column))
                AddReference(reference.Column, isFragment);
            if (reference.Rules != null)
            {
                foreach (var rule in reference.Rules)
                {
                    if (!string.IsNullOrEmpty(rule.When?.Column))
                        AddReference(rule.When.Column, isFragment);
                }
            }
        }

        // Standardfragmente des columns-Modus, wenn kein Fragment konfiguriert ist
        if (pattern != null)
        {
            var fields = lineItems?.Fields;
            if (fields?.Name?.Column == null)
                AddReference(ColumnResolver.DefaultNameFragment, true);
            // Standardfragmente sind optionale Lesezugriffe: fehlen sie, bleibt das Feld leer, kein Befund
            if (fields?.Description == null || (fields.Description.Column == null && string.IsNullOrEmpty(fields.Description.Value)))
                AddReference(ColumnResolver.DefaultDescriptionFragment, true, optional: true);
            if (fields?.NetUnitPrice == null || (fields.NetUnitPrice.Column == null && string.IsNullOrEmpty(fields.NetUnitPrice.Value)))
                AddReference(ColumnResolver.DefaultPriceFragment, true, optional: true);
            if (fields?.BilledQuantity == null || (fields.BilledQuantity.Column == null && string.IsNullOrEmpty(fields.BilledQuantity.Value)))
                AddReference(ColumnResolver.DefaultQuantityFragment, true, optional: true);
        }

        if (headers == null)
            return new ColumnCheck(referenced, null, null);

        var unused = headers.Where(h => !used.Contains(h)).ToList();
        return new ColumnCheck(referenced, missing.Distinct(StringComparer.Ordinal).ToList(), unused);
    }

    private static int CountPositions(ColumnPatternConfig pattern, string nameFragment, HashSet<string>? headerSet)
    {
        if (headerSet == null) return 0;
        var count = 0;
        while (count < 10_000 && headerSet.Contains(ColumnResolver.BuildColumnName(pattern, pattern.StartIndex + count, nameFragment)))
            count++;
        return count;
    }

    /// <summary>Wirkt das Feld in der konfigurierten Betriebsart? Modusabhängig ignorierte Felder referenzieren keine Spalten.</summary>
    private static bool IsEffective(string semanticPath, AppConfig config)
    {
        var semantic = ConfigSemantics.Find(semanticPath);
        if (semantic == null) return true;
        if (semantic.Status == FieldStatus.Ignored) return false;
        if (semantic.Modes.Count == 0) return true;

        var lineItemsMode = ConfigValues.ParseLineItemsMode(config.Mapping.LineItems?.Mode);
        var totalsMode = config.Mapping.Totals == null ? ConfigValues.TotalsModeAuto : ConfigValues.ParseTotalsMode(config.Mapping.Totals.Mode);
        return semantic.Modes.Contains(lineItemsMode) || semantic.Modes.Contains(totalsMode);
    }

    // --- Reflection über das Konfigurationsmodell ---

    private static IEnumerable<FieldReference> CollectReferences(AppConfig config)
    {
        var result = new List<FieldReference>();
        Walk(config.Mapping, "mapping", "mapping", result);
        return result;
    }

    private static void Walk(object? instance, string path, string semanticPath, List<FieldReference> result)
    {
        if (instance == null) return;

        foreach (var property in instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = Camel(property.Name);
            var childPath = $"{path}.{name}";
            var childSemantic = $"{semanticPath}.{name}";
            var value = property.GetValue(instance);
            if (value == null) continue;

            switch (value)
            {
                case FieldMapping mapping:
                    result.Add(new FieldReference(childPath, childSemantic, mapping.Value, mapping.Column, mapping.Default, mapping.Rules, IsLineItemPath(childSemantic)));
                    break;
                case List<TaxRegistrationMapping> registrations:
                    for (var i = 0; i < registrations.Count; i++)
                        result.Add(new FieldReference($"{childPath}[{i}]", childSemantic + "[]", registrations[i].Value, registrations[i].Column, null, null, false));
                    break;
                case Dictionary<string, FieldMapping> dictionary:
                    foreach (var (key, mapping) in dictionary)
                        result.Add(new FieldReference($"{childPath}.{key}", $"{childSemantic}.{key}", mapping.Value, mapping.Column, mapping.Default, mapping.Rules, IsLineItemPath(childSemantic)));
                    break;
                case Dictionary<string, string>:
                    break;
                case string or bool or int:
                    break;
                case System.Collections.IList list:
                    for (var i = 0; i < list.Count; i++)
                        Walk(list[i], $"{childPath}[{i}]", childSemantic + "[]", result);
                    break;
                default:
                    if (value.GetType().Namespace == "Csv2Zugferd.Models")
                        Walk(value, childPath, childSemantic, result);
                    break;
            }
        }
    }

    private static bool IsLineItemPath(string semanticPath) =>
        semanticPath.StartsWith("mapping.lineItems.fields.", StringComparison.Ordinal)
        || semanticPath.StartsWith("mapping.lineItems.fixedFields", StringComparison.Ordinal);

    private static string Camel(string name) => CamelCaseNamingConvention.Instance.Apply(name);
}
