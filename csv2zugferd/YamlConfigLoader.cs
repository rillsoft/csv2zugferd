using System.Reflection;
using Csv2Zugferd.Models;
using Serilog;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Csv2Zugferd;

public static class YamlConfigLoader
{
    /// <summary>Lädt die Konfiguration aus einer Datei. Fehlende Datei → <see cref="ErrorCodes.ConfigNotFound"/>.</summary>
    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new Csv2ZugferdException(ErrorCodes.ConfigNotFound, $"Konfigurationsdatei nicht gefunden: {path}");

        return LoadFromString(File.ReadAllText(path));
    }

    /// <summary>
    /// Kern des Ladens: deserialisiert YAML-Text tolerant (unbekannte Schlüssel werden ignoriert, siehe
    /// <see cref="FindUnknownKeys"/>). Ungültiges oder leeres YAML → <see cref="ErrorCodes.ConfigInvalidYaml"/>.
    /// </summary>
    public static AppConfig LoadFromString(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        AppConfig? config;
        try
        {
            config = deserializer.Deserialize<AppConfig>(yaml);
        }
        catch (YamlException ex)
        {
            throw new Csv2ZugferdException(ErrorCodes.ConfigInvalidYaml,
                $"Ungültiges YAML in Zeile {ex.Start.Line}, Spalte {ex.Start.Column}: {Describe(ex)}", ex);
        }

        if (config == null)
            throw new Csv2ZugferdException(ErrorCodes.ConfigInvalidYaml, "Die Konfiguration ist leer");

        Log.Debug("YML-Konfiguration geladen: LineItems.Mode={Mode}, Totals.Mode={TotalsMode}",
            config.Mapping.LineItems?.Mode ?? "rows", config.Mapping.Totals?.Mode ?? "auto");
        return config;
    }

    /// <summary>
    /// Läuft den YAML-Baum gegen die Modellklassen und liefert alle Schlüssel, die kein Modell kennt, als
    /// vollständige Punktpfade mit Listenindizes (z. B. <c>mapping.invoice.number</c>,
    /// <c>mapping.seller.taxRegistrations[1].schema</c>). <c>fixedFields</c> wird gegen die drei wirksamen
    /// Schlüssel geprüft; die Schlüssel von <c>columnsFields</c> sind frei. Ungültiges YAML →
    /// <see cref="ErrorCodes.ConfigInvalidYaml"/>.
    /// </summary>
    public static IReadOnlyList<string> FindUnknownKeys(string yaml)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException ex)
        {
            throw new Csv2ZugferdException(ErrorCodes.ConfigInvalidYaml,
                $"Ungültiges YAML in Zeile {ex.Start.Line}, Spalte {ex.Start.Column}: {Describe(ex)}", ex);
        }

        var unknown = new List<string>();
        if (stream.Documents.Count > 0)
            Walk(stream.Documents[0].RootNode, typeof(AppConfig), string.Empty, unknown);
        return unknown;
    }

    /// <summary>Die drei Schlüssel von <c>lineItems.fixedFields</c>, die der Mapper liest.</summary>
    public static readonly IReadOnlyList<string> EffectiveFixedFieldKeys = ["unitCode", "taxType", "taxCategoryCode"];

    private static void Walk(YamlNode node, Type type, string path, List<string> unknown)
    {
        if (node is not YamlMappingNode mapping)
            return;

        if (TryGetDictionaryValueType(type, out var valueType))
        {
            foreach (var (keyNode, valueNode) in mapping.Children)
            {
                var key = ((YamlScalarNode)keyNode).Value ?? string.Empty;
                var childPath = Join(path, key);
                if (path == "mapping.lineItems.fixedFields" && !EffectiveFixedFieldKeys.Contains(key, StringComparer.Ordinal))
                    unknown.Add(childPath);
                Walk(valueNode, valueType, childPath, unknown);
            }
            return;
        }

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => CamelCaseNamingConvention.Instance.Apply(p.Name), p => p, StringComparer.Ordinal);

        foreach (var (keyNode, valueNode) in mapping.Children)
        {
            var key = ((YamlScalarNode)keyNode).Value ?? string.Empty;
            var childPath = Join(path, key);
            if (!properties.TryGetValue(key, out var property))
            {
                unknown.Add(childPath);
                continue;
            }

            if (TryGetListElementType(property.PropertyType, out var elementType) && valueNode is YamlSequenceNode sequence)
            {
                for (var i = 0; i < sequence.Children.Count; i++)
                    Walk(sequence.Children[i], elementType, $"{childPath}[{i}]", unknown);
                continue;
            }

            Walk(valueNode, property.PropertyType, childPath, unknown);
        }
    }

    private static bool TryGetDictionaryValueType(Type type, out Type valueType)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) && type.GetGenericArguments()[0] == typeof(string))
        {
            valueType = type.GetGenericArguments()[1];
            return true;
        }
        valueType = typeof(object);
        return false;
    }

    private static bool TryGetListElementType(Type type, out Type elementType)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            elementType = type.GetGenericArguments()[0];
            return true;
        }
        elementType = typeof(object);
        return false;
    }

    private static string Join(string path, string key) =>
        path.Length == 0 ? key : $"{path}.{key}";

    private static string Describe(YamlException ex)
    {
        var inner = ex.InnerException?.Message;
        var text = string.IsNullOrEmpty(inner) ? ex.Message : $"{ex.Message} ({inner})";
        if (inner != null && inner.Contains("Invalid cast from 'System.String' to 'Csv2Zugferd.Models.", StringComparison.Ordinal))
            text += ". Hinweis: An dieser Stelle wird ein Objekt mit Unterschlüsseln erwartet (z. B. value, column, default, rules), kein einfacher Text";
        return text;
    }

    public static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            Csv = new CsvConfig(),
            Zugferd = new ZugferdConfig(),
            Mapping = new MappingConfig
            {
                Invoice = new InvoiceMapping
                {
                    InvoiceNumber = new FieldMapping { Column = "Rechnungsnummer" },
                    InvoiceDate = new FieldMapping { Column = "Rechnungsdatum" },
                    Currency = new FieldMapping { Value = "EUR" },
                    Name = new FieldMapping { Value = "WARENRECHNUNG" },
                },
                Buyer = new BuyerMapping
                {
                    Name = new FieldMapping { Column = "Käufer_Name" },
                    PostalCode = new FieldMapping { Column = "Käufer_PLZ" },
                    City = new FieldMapping { Column = "Käufer_Ort" },
                    Street = new FieldMapping { Column = "Käufer_Strasse" },
                    Country = new FieldMapping { Column = "Käufer_Land" },
                },
                Seller = new SellerMapping
                {
                    Name = new FieldMapping { Column = "Verkäufer_Name" },
                    PostalCode = new FieldMapping { Column = "Verkäufer_PLZ" },
                    City = new FieldMapping { Column = "Verkäufer_Ort" },
                    Street = new FieldMapping { Column = "Verkäufer_Strasse" },
                    Country = new FieldMapping { Column = "Verkäufer_Land" },
                },
                LineItems = new LineItemsMapping
                {
                    Mode = "rows",
                    Fields = new LineItemFieldsMapping
                    {
                        Name = new FieldMapping { Column = "Artikel_Name" },
                        Description = new FieldMapping { Column = "Artikel_Beschreibung" },
                        NetUnitPrice = new FieldMapping { Column = "Einzelpreis_Netto" },
                        BilledQuantity = new FieldMapping { Column = "Menge" },
                        UnitCode = new FieldMapping { Value = "C62" },
                        TaxType = new FieldMapping { Value = "VAT" },
                        TaxCategoryCode = new FieldMapping { Value = "S" },
                        TaxPercent = new FieldMapping { Column = "MwSt_Prozent" },
                    },
                },
                Totals = new TotalsMapping { Mode = "auto" },
            }
        };
    }
}
