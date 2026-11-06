using Csv2Zugferd.Models;
using NUnit.Framework;
using s2industries.ZUGFeRD;

namespace Csv2Zugferd.Tests;

public static class TestHelper
{
    public static string GetTestDataPath(string fileName)
        => Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata", fileName);

    public static AppConfig LoadTestConfig(string configFile)
        => YamlConfigLoader.Load(GetTestDataPath(configFile));

    public static List<Dictionary<string, string>> ReadTestCsv(string csvFile, CsvConfig csvConfig)
        => CsvInvoiceReader.Read(GetTestDataPath(csvFile), csvConfig);

    public static InvoiceDescriptor MapFromTestData(string configFile, string csvFile)
    {
        var config = LoadTestConfig(configFile);
        var rows = ReadTestCsv(csvFile, config.Csv);
        return InvoiceMapper.Map(rows, config);
    }

    /// <summary>
    /// Testfall mit Mengen, deren Zeilennettobeträge mehr als zwei Nachkommastellen haben
    /// (0,28 × 51,45 = 14,406; 0,38 × 51,45 = 19,551; 2,65 × 18,04 = 47,806).
    /// Erwartung: Zeilensumme 394,83; Steuerbasis 19 % = 217,89; 7 % = 176,94; Steuer 53,79; Brutto 448,62.
    /// </summary>
    public static (AppConfig Config, List<Dictionary<string, string>> Rows) CreateRoundingTestCase(string totalsMode = "auto")
    {
        var config = new AppConfig
        {
            Csv = new CsvConfig { DecimalSeparator = "." },
            Zugferd = new ZugferdConfig { Profile = "EN16931" },
            Mapping = new MappingConfig
            {
                Invoice = new InvoiceMapping
                {
                    InvoiceNumber = new FieldMapping { Value = "R-2026-001" },
                    InvoiceDate = new FieldMapping { Value = "2026-01-15" },
                    Currency = new FieldMapping { Value = "EUR" },
                },
                Seller = new SellerMapping
                {
                    Name = new FieldMapping { Value = "Lieferant GmbH" },
                    PostalCode = new FieldMapping { Value = "80333" },
                    City = new FieldMapping { Value = "München" },
                    Street = new FieldMapping { Value = "Lieferantenstraße 20" },
                    Country = new FieldMapping { Value = "DE" },
                    TaxRegistrations = [new TaxRegistrationMapping { Value = "DE123456789", Scheme = "VA" }],
                },
                Buyer = new BuyerMapping
                {
                    Name = new FieldMapping { Value = "Kunden Mitte AG" },
                    PostalCode = new FieldMapping { Value = "69876" },
                    City = new FieldMapping { Value = "Frankfurt" },
                    Street = new FieldMapping { Value = "Kundenstraße 15" },
                    Country = new FieldMapping { Value = "DE" },
                },
                LineItems = new LineItemsMapping
                {
                    Mode = "columns",
                    ColumnPattern = new ColumnPatternConfig { Style = "suffix", Separator = "_", StartIndex = 0 },
                    Fields = new LineItemFieldsMapping
                    {
                        Name = new FieldMapping { Column = "PRODUCT_NAME" },
                        NetUnitPrice = new FieldMapping { Column = "PRODUCT_PRICE" },
                        BilledQuantity = new FieldMapping { Column = "PRODUCT_QUANTITY" },
                        TaxPercent = new FieldMapping { Column = "PRODUCT_TAX" },
                    },
                },
                Totals = new TotalsMapping
                {
                    Mode = totalsMode,
                    LineTotalAmount = new FieldMapping { Value = "394.83" },
                    AllowanceTotalAmount = new FieldMapping { Value = "0.00" },
                    TaxBasisAmount = new FieldMapping { Value = "394.83" },
                    TaxTotalAmount = new FieldMapping { Value = "53.79" },
                    GrandTotalAmount = new FieldMapping { Value = "448.62" },
                    TotalPrepaidAmount = new FieldMapping { Value = "0.00" },
                },
            }
        };

        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var positions = new (string Name, string Quantity, string Price, string Tax)[]
        {
            ("Mulch", "1.5", "58.98", "7"),
            ("Schnittgut", "0.28", "51.45", "19"),
            ("Mulch", "1.5", "58.98", "7"),
            ("Schnittgut", "0.38", "51.45", "19"),
            ("Tragschicht", "2", "33.53", "19"),
            ("Tragschicht", "2", "33.53", "19"),
            ("Mindermengenzuschlag", "2", "1", "19"),
            ("Sand", "2.65", "18.04", "19"),
        };
        for (var i = 0; i < positions.Length; i++)
        {
            row[$"PRODUCT_NAME_{i}"] = positions[i].Name;
            row[$"PRODUCT_QUANTITY_{i}"] = positions[i].Quantity;
            row[$"PRODUCT_PRICE_{i}"] = positions[i].Price;
            row[$"PRODUCT_TAX_{i}"] = positions[i].Tax;
        }

        return (config, [row]);
    }
}
