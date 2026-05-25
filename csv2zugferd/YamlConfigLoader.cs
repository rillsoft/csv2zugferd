using Csv2Zugferd.Models;
using Serilog;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Csv2Zugferd;

public static class YamlConfigLoader
{
    public static AppConfig Load(string path)
    {
        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var config = deserializer.Deserialize<AppConfig>(yaml);
        Log.Debug("YML-Konfiguration geladen: LineItems.Mode={Mode}, Totals.Mode={TotalsMode}",
            config.Mapping.LineItems?.Mode ?? "rows", config.Mapping.Totals?.Mode ?? "auto");
        return config;
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
