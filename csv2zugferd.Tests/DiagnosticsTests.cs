using Csv2Zugferd.Models;
using NUnit.Framework;
using s2industries.ZUGFeRD;
using Shouldly;

namespace Csv2Zugferd.Tests;

/// <summary>Strukturierte Befunde des Mappers: Codes und Feldpfade, nie Texte.</summary>
[TestFixture]
public class DiagnosticsTests
{
    [Test]
    public void Map_InvalidDate_CollectsWarning()
    {
        var config = CreateConfig();
        config.Mapping.Invoice!.InvoiceDate = new FieldMapping { Value = "gestern" };
        var diagnostics = new List<MappingDiagnostic>();

        var desc = InvoiceMapper.Map([new Dictionary<string, string>()], config, diagnostics);

        desc.InvoiceDate.ShouldBe(DateTime.MinValue);
        var diagnostic = diagnostics.Single(d => d.Code == DiagnosticCodes.InvalidDate);
        diagnostic.Path.ShouldBe("mapping.invoice.invoiceDate");
        diagnostic.Message.ShouldContain("gestern");
    }

    [Test]
    public void Map_InvalidDecimal_CollectsWarning()
    {
        var config = CreateConfig();
        config.Mapping.LineItems = new LineItemsMapping
        {
            Mode = "rows",
            Fields = new LineItemFieldsMapping
            {
                Name = new FieldMapping { Value = "Artikel" },
                NetUnitPrice = new FieldMapping { Value = "zehn" },
                BilledQuantity = new FieldMapping { Value = "1" },
                TaxPercent = new FieldMapping { Value = "19" },
            },
        };
        var diagnostics = new List<MappingDiagnostic>();

        var desc = InvoiceMapper.Map([new Dictionary<string, string>()], config, diagnostics);

        desc.TradeLineItems.Single().NetUnitPrice.ShouldBe(0m);
        var diagnostic = diagnostics.Single(d => d.Code == DiagnosticCodes.InvalidDecimal);
        diagnostic.Path.ShouldBe("mapping.lineItems.fields.netUnitPrice");
        diagnostic.Message.ShouldContain("Zeile 1");
    }

    [Test]
    public void Map_Warnings_ContainCheckDocumentResults()
    {
        var config = TestHelper.LoadTestConfig("config_creditnote.yml");
        config.Mapping.Invoice!.PrecedingInvoices = null;
        var rows = TestHelper.ReadTestCsv("test_creditnote.csv", config.Csv);
        var diagnostics = new List<MappingDiagnostic>();

        var desc = InvoiceMapper.Map(rows, config, diagnostics);

        var expectedTexts = InvoiceMapper.CheckDocument(desc, config.Zugferd.Profile);
        expectedTexts.ShouldNotBeEmpty();
        diagnostics.Select(d => d.Message).ShouldContain(expectedTexts[0]);
        diagnostics.ShouldContain(d => d.Code == DiagnosticCodes.PrecedingInvoiceMissing && d.Path == "mapping.invoice.precedingInvoices");
        diagnostics.ShouldContain(d => d.Code == DiagnosticCodes.DeliveryDateDefaulted && d.Path == "mapping.delivery");
    }

    [Test]
    public void Map_WithoutWarningList_BehavesAsBefore()
    {
        var config = TestHelper.LoadTestConfig("config_rows.yml");
        var rows = TestHelper.ReadTestCsv("test_rows.csv", config.Csv);
        var diagnostics = new List<MappingDiagnostic>();

        var plain = InvoiceMapper.Map(rows, config);
        var withDiagnostics = InvoiceMapper.Map(rows, config, diagnostics);

        plain.InvoiceNo.ShouldBe(withDiagnostics.InvoiceNo);
        plain.GrandTotalAmount.ShouldBe(withDiagnostics.GrandTotalAmount);
        plain.TradeLineItems.Count.ShouldBe(withDiagnostics.TradeLineItems.Count);
    }

    [Test]
    public void Map_ColumnsFields_HasNoEffectAndReportsIgnored()
    {
        var (config, rows) = TestHelper.CreateRoundingTestCase();
        var reference = InvoiceMapper.Map(rows, config);
        config.Mapping.LineItems!.ColumnsFields = new Dictionary<string, string> { ["name"] = "ANDERS" };
        var diagnostics = new List<MappingDiagnostic>();

        var desc = InvoiceMapper.Map(rows, config, diagnostics);

        desc.TradeLineItems.Count.ShouldBe(reference.TradeLineItems.Count);
        desc.GrandTotalAmount.ShouldBe(reference.GrandTotalAmount);
        diagnostics.ShouldContain(d => d.Code == DiagnosticCodes.ConfigFieldIgnored && d.Path == "mapping.lineItems.columnsFields");
    }

    [Test]
    public void Map_DiscountReason_HasNoEffectAndReportsIgnored()
    {
        var config = TestHelper.LoadTestConfig("config_columns_suffix.yml");
        var rows = TestHelper.ReadTestCsv("test_columns_suffix.csv", config.Csv);
        config.Mapping.LineItems!.Fields!.DiscountReason.ShouldNotBeNull(); // Testdaten enthalten das Feld
        var diagnostics = new List<MappingDiagnostic>();

        var withReason = InvoiceMapper.Map(rows, config, diagnostics);
        config.Mapping.LineItems.Fields.DiscountReason = null;
        var withoutReason = InvoiceMapper.Map(rows, config);

        withReason.GrandTotalAmount.ShouldBe(withoutReason.GrandTotalAmount);
        withReason.TradeLineItems.Single(i => i.Name == "Test1").NetUnitPrice.ShouldBe(withoutReason.TradeLineItems.Single(i => i.Name == "Test1").NetUnitPrice);
        diagnostics.ShouldContain(d => d.Code == DiagnosticCodes.ConfigFieldIgnored && d.Path == "mapping.lineItems.fields.discountReason");
    }

    [Test]
    public void Map_ModeDependentFields_ReportIgnored()
    {
        var config = TestHelper.LoadTestConfig("config_rows.yml");
        config.Mapping.LineItems!.ColumnPattern = new ColumnPatternConfig();
        config.Mapping.LineItems.FixedFields = new Dictionary<string, FieldMapping> { ["unitCode"] = new() { Value = "C62" } };
        config.Mapping.Totals!.GrandTotalAmount = new FieldMapping { Value = "1" };
        var rows = TestHelper.ReadTestCsv("test_rows.csv", config.Csv);
        var diagnostics = new List<MappingDiagnostic>();

        InvoiceMapper.Map(rows, config, diagnostics);

        var ignored = diagnostics.Where(d => d.Code == DiagnosticCodes.ConfigFieldIgnored).Select(d => d.Path).ToList();
        ignored.ShouldContain("mapping.lineItems.columnPattern");
        ignored.ShouldContain("mapping.lineItems.fixedFields");
        ignored.ShouldContain("mapping.totals.grandTotalAmount");
    }

    [Test]
    public void Diagnostics_InvalidIban_ContainsCodeAndPath()
    {
        var config = TestHelper.LoadTestConfig("config_rows.yml");
        config.Mapping.PaymentMeans!.SellerAccounts![0].Iban = new FieldMapping { Value = "DE02 1203 0000 0000 2020 52" };
        var rows = TestHelper.ReadTestCsv("test_rows.csv", config.Csv);
        var diagnostics = new List<MappingDiagnostic>();

        InvoiceMapper.Map(rows, config, diagnostics);

        var diagnostic = diagnostics.Single(d => d.Code == DiagnosticCodes.InvalidIbanChecksum);
        diagnostic.Path.ShouldBe("mapping.paymentMeans.sellerAccounts[0].iban");
    }

    [Test]
    public void Map_UnknownCurrency_ReportsFallbackWithPath()
    {
        var config = CreateConfig();
        config.Mapping.Invoice!.Currency = new FieldMapping { Value = "Euro" };
        var diagnostics = new List<MappingDiagnostic>();

        var desc = InvoiceMapper.Map([new Dictionary<string, string>()], config, diagnostics);

        desc.Currency.ShouldBe(CurrencyCodes.EUR);
        var diagnostic = diagnostics.Single(d => d.Code == DiagnosticCodes.ConfigValueFallback);
        diagnostic.Path.ShouldBe("mapping.invoice.currency");
        diagnostic.Message.ShouldContain("EUR");
    }

    [Test]
    public void Map_InvalidRegex_ReportsRuleIndex()
    {
        var config = CreateConfig();
        config.Mapping.Invoice!.Name = new FieldMapping
        {
            Rules = [new FieldRule { When = new FieldRuleCondition { Column = "Belegart", Regex = "(" }, Value = "X" }],
        };
        var diagnostics = new List<MappingDiagnostic>();

        InvoiceMapper.Map([new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Belegart"] = "Rechnung" }], config, diagnostics);

        diagnostics.Single(d => d.Code == DiagnosticCodes.InvalidRegex).Path.ShouldBe("mapping.invoice.name.rules[0].when.regex");
    }

    [Test]
    public void Diagnostics_AllCodes_AreInCatalog()
    {
        var config = TestHelper.LoadTestConfig("config_directdebit.yml");
        config.Zugferd.Profile = "XRechnung";
        config.Mapping.PaymentMeans!.DirectDebit!.CreditorId = null;
        config.Mapping.Invoice!.Currency = new FieldMapping { Value = "Taler" };
        var rows = TestHelper.ReadTestCsv("test_directdebit.csv", config.Csv);
        var diagnostics = new List<MappingDiagnostic>();

        InvoiceMapper.Map(rows, config, diagnostics);

        diagnostics.ShouldNotBeEmpty();
        foreach (var diagnostic in diagnostics)
        {
            DiagnosticCodes.All.ShouldContain(diagnostic.Code);
            diagnostic.Path.ShouldNotBeNullOrWhiteSpace();
            diagnostic.Message.ShouldNotBeNullOrWhiteSpace();
        }
        DiagnosticCodes.All.ShouldBeUnique();
        ErrorCodes.All.ShouldBeUnique();
        ErrorCodes.All.Intersect(DiagnosticCodes.All).ShouldBeEmpty();
    }

    [Test]
    public void SaveXml_WithDiagnostics_ReportsVersionFallback()
    {
        var config = TestHelper.LoadTestConfig("config_rows.yml");
        config.Zugferd.Version = "4.0";
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");
        var diagnostics = new List<MappingDiagnostic>();
        using var stream = new MemoryStream();

        InvoiceExporter.WriteXml(desc, stream, config.Zugferd, diagnostics);

        stream.Length.ShouldBeGreaterThan(0);
        diagnostics.Single().Code.ShouldBe(DiagnosticCodes.ConfigValueFallback);
        diagnostics.Single().Path.ShouldBe("zugferd.version");
    }

    private static AppConfig CreateConfig() => new()
    {
        Csv = new CsvConfig { DecimalSeparator = "." },
        Zugferd = new ZugferdConfig(),
        Mapping = new MappingConfig
        {
            Invoice = new InvoiceMapping
            {
                InvoiceNumber = new FieldMapping { Value = "R-1" },
                InvoiceDate = new FieldMapping { Value = "01.01.2026" },
                Currency = new FieldMapping { Value = "EUR" },
            },
            Totals = new TotalsMapping { Mode = "auto" },
        },
    };
}
