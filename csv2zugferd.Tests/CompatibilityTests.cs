using Csv2Zugferd.Models;
using NUnit.Framework;
using s2industries.ZUGFeRD;
using Shouldly;

namespace Csv2Zugferd.Tests;

/// <summary>
/// Charakterisierungstests: halten das bestehende Verhalten für fehlende, leere und unbekannte Modi sowie
/// akzeptierte Schreibvarianten fest. Der Ausgangscode ist das Orakel; diese Rückfälle dürfen sich durch
/// den MCP-Ausbau nicht still ändern.
/// </summary>
[TestFixture]
public class CompatibilityTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // --- totals.mode: fehlend/auto → auto; leer oder unbekannt → manueller Zweig ---

    [TestCase(null, true)]
    [TestCase("auto", true)]
    [TestCase("AUTO", true)]
    [TestCase("manual", false)]
    [TestCase("", false)]
    [TestCase("unbekannt", false)]
    [TestCase("auto ", false)]
    public void Map_TotalsMode_Spellings_PreserveBranch(string? mode, bool expectAutoBranch)
    {
        var (config, rows) = TestHelper.CreateRoundingTestCase(mode ?? "manual");
        if (mode == null)
            config.Mapping.Totals = null;
        else
            config.Mapping.Totals!.Mode = mode;
        // Manueller Zweig liefert erkennbar andere Summen als der automatische
        if (config.Mapping.Totals != null)
            config.Mapping.Totals.GrandTotalAmount = new FieldMapping { Value = "999.99" };

        var desc = InvoiceMapper.Map(rows, config);

        desc.GrandTotalAmount.ShouldBe(expectAutoBranch ? 448.62m : 999.99m);
    }

    // --- lineItems.mode: columns (case-insensitive) → columns; alles andere → rows ---

    [TestCase("columns", 8)]
    [TestCase("COLUMNS", 8)]
    [TestCase("rows", 0)]
    [TestCase("", 0)]
    [TestCase("unbekannt", 0)]
    public void Map_LineItemsMode_Spellings_PreserveBranch(string mode, int expectedLineItems)
    {
        var (config, rows) = TestHelper.CreateRoundingTestCase();
        config.Mapping.LineItems!.Mode = mode;

        var desc = InvoiceMapper.Map(rows, config);

        // Im rows-Zweig fehlt die Spalte PRODUCT_NAME, es entsteht keine Position
        desc.TradeLineItems.Count.ShouldBe(expectedLineItems);
    }

    // --- columnPattern.style: prefix (case-insensitive) → prefix; alles andere → suffix ---

    [TestCase("suffix", 8)]
    [TestCase("SUFFIX", 8)]
    [TestCase("", 8)]
    [TestCase("unbekannt", 8)]
    [TestCase("prefix", 0)]
    [TestCase("PREFIX", 0)]
    public void Map_ColumnStyle_Spellings_PreserveBranch(string style, int expectedLineItems)
    {
        var (config, rows) = TestHelper.CreateRoundingTestCase();
        config.Mapping.LineItems!.ColumnPattern!.Style = style;

        var desc = InvoiceMapper.Map(rows, config);

        // Die Testzeile hat Suffix-Spalten (PRODUCT_NAME_0); im prefix-Zweig wird nichts gefunden
        desc.TradeLineItems.Count.ShouldBe(expectedLineItems);
    }

    // --- Fachliche Codes: Rückfälle und akzeptierte Schreibweisen ---

    [TestCase("EUR", CurrencyCodes.EUR)]
    [TestCase("eur", CurrencyCodes.EUR)]
    [TestCase("USD", CurrencyCodes.USD)]
    [TestCase("", CurrencyCodes.EUR)]
    [TestCase("Euro", CurrencyCodes.EUR)]
    public void Map_Currency_Spellings_PreserveFallback(string value, CurrencyCodes expected)
    {
        var config = CreateMinimalConfig();
        config.Mapping.Invoice!.Currency = new FieldMapping { Value = value };

        var desc = InvoiceMapper.Map([new Dictionary<string, string>()], config);

        desc.Currency.ShouldBe(expected);
    }

    [TestCase("DE", CountryCodes.DE)]
    [TestCase("de", CountryCodes.DE)]
    [TestCase("AT", CountryCodes.AT)]
    [TestCase("Deutschland", CountryCodes.DE)]
    public void Map_Country_Spellings_PreserveFallback(string value, CountryCodes expected)
    {
        var config = CreateMinimalConfig();
        config.Mapping.Buyer = new BuyerMapping
        {
            Name = new FieldMapping { Value = "Käufer" },
            Country = new FieldMapping { Value = value },
        };

        var desc = InvoiceMapper.Map([new Dictionary<string, string>()], config);

        desc.Buyer.Country.ShouldBe(expected);
    }

    [TestCase("C62", QuantityCodes.C62)]
    [TestCase("c62", QuantityCodes.C62)]
    [TestCase("DAY", QuantityCodes.DAY)]
    [TestCase("Stück", QuantityCodes.C62)]
    public void Map_UnitCode_Spellings_PreserveFallback(string value, QuantityCodes expected)
    {
        var desc = MapSingleRowsItem(fields => fields.UnitCode = new FieldMapping { Value = value });

        desc.TradeLineItems.Single().UnitCode.ShouldBe(expected);
    }

    [TestCase("VAT", TaxTypes.VAT)]
    [TestCase("vat", TaxTypes.VAT)]
    [TestCase("MwSt", TaxTypes.VAT)]
    public void Map_TaxType_Spellings_PreserveFallback(string value, TaxTypes expected)
    {
        var desc = MapSingleRowsItem(fields => fields.TaxType = new FieldMapping { Value = value });

        desc.TradeLineItems.Single().TaxType.ShouldBe(expected);
    }

    [TestCase("S", TaxCategoryCodes.S)]
    [TestCase("s", TaxCategoryCodes.S)]
    [TestCase("E", TaxCategoryCodes.E)]
    [TestCase("Standard", TaxCategoryCodes.S)]
    public void Map_TaxCategory_Spellings_PreserveFallback(string value, TaxCategoryCodes expected)
    {
        var desc = MapSingleRowsItem(fields => fields.TaxCategoryCode = new FieldMapping { Value = value });

        desc.TradeLineItems.Single().TaxCategoryCode.ShouldBe(expected);
    }

    [TestCase("FC", TaxRegistrationSchemeID.FC)]
    [TestCase("fc", TaxRegistrationSchemeID.FC)]
    [TestCase("VA", TaxRegistrationSchemeID.VA)]
    [TestCase(null, TaxRegistrationSchemeID.VA)]
    [TestCase("XX", TaxRegistrationSchemeID.VA)]
    public void Map_TaxScheme_Spellings_PreserveFallback(string? scheme, TaxRegistrationSchemeID expected)
    {
        var config = CreateMinimalConfig();
        config.Mapping.Seller = new SellerMapping
        {
            Name = new FieldMapping { Value = "Verkäufer" },
            TaxRegistrations = [new TaxRegistrationMapping { Value = "DE123456789", Scheme = scheme }],
        };

        var desc = InvoiceMapper.Map([new Dictionary<string, string>()], config);

        desc.SellerTaxRegistration.Single().SchemeID.ShouldBe(expected);
    }

    [TestCase("GermanyVatNumber", ElectronicAddressSchemeIdentifiers.GermanyVatNumber)]
    [TestCase("LuxemburgVatNumber", ElectronicAddressSchemeIdentifiers.LuxemburgVatNumber)]
    [TestCase("EM", ElectronicAddressSchemeIdentifiers.ElectronicMailSmtp)]
    [TestCase("ElectronicMailSmtp", ElectronicAddressSchemeIdentifiers.ElectronicMailSmtp)]
    [TestCase("germanyvatnumber", ElectronicAddressSchemeIdentifiers.GermanyVatNumber)]
    [TestCase("em", ElectronicAddressSchemeIdentifiers.GermanyVatNumber)]
    [TestCase(null, ElectronicAddressSchemeIdentifiers.GermanyVatNumber)]
    [TestCase("0088", ElectronicAddressSchemeIdentifiers.GermanyVatNumber)]
    public void Map_ElectronicAddressScheme_Spellings_PreserveFallback(string? scheme, ElectronicAddressSchemeIdentifiers expected)
    {
        var config = CreateMinimalConfig();
        config.Mapping.Seller = new SellerMapping
        {
            Name = new FieldMapping { Value = "Verkäufer" },
            ElectronicAddress = new FieldMapping { Value = "DE123456789", Scheme = scheme },
        };

        var desc = InvoiceMapper.Map([new Dictionary<string, string>()], config);

        desc.SellerElectronicAddress.ElectronicAddressSchemeID.ShouldBe(expected);
    }

    [TestCase("380", InvoiceType.Invoice)]
    [TestCase("381", InvoiceType.CreditNote)]
    [TestCase(" 381 ", InvoiceType.CreditNote)]
    [TestCase("CreditNote", InvoiceType.CreditNote)]
    [TestCase("creditnote", InvoiceType.CreditNote)]
    [TestCase("Correction", InvoiceType.Correction)]
    public void Map_InvoiceType_Spellings_PreserveParsing(string value, InvoiceType expected)
    {
        var config = CreateMinimalConfig();
        config.Mapping.Invoice!.TypeCode = new FieldMapping { Value = value };

        var desc = InvoiceMapper.Map([new Dictionary<string, string>()], config);

        desc.Type.ShouldBe(expected);
    }

    [TestCase("999")]
    [TestCase("Rechnung")]
    public void Map_InvoiceType_Unknown_Throws(string value)
    {
        var config = CreateMinimalConfig();
        config.Mapping.Invoice!.TypeCode = new FieldMapping { Value = value };

        Should.Throw<Exception>(() => InvoiceMapper.Map([new Dictionary<string, string>()], config));
    }

    [TestCase("58", PaymentMeansTypeCodes.SEPACreditTransfer)]
    [TestCase(" 58 ", PaymentMeansTypeCodes.SEPACreditTransfer)]
    [TestCase("SEPACreditTransfer", PaymentMeansTypeCodes.SEPACreditTransfer)]
    [TestCase("sepacredittransfer", PaymentMeansTypeCodes.SEPACreditTransfer)]
    [TestCase("59", PaymentMeansTypeCodes.SEPADirectDebit)]
    [TestCase("30", PaymentMeansTypeCodes.CreditTransferNonSEPA)]
    public void Map_PaymentMeansType_Spellings_PreserveParsing(string value, PaymentMeansTypeCodes expected)
    {
        var config = CreateMinimalConfig();
        config.Mapping.PaymentMeans = new PaymentMeansMapping { TypeCode = new FieldMapping { Value = value } };

        var desc = InvoiceMapper.Map([new Dictionary<string, string>()], config);

        desc.PaymentMeans.TypeCode.ShouldBe(expected);
    }

    [TestCase("Scheck")]
    [TestCase("9999")]
    public void Map_PaymentMeansType_Unknown_Throws(string value)
    {
        var config = CreateMinimalConfig();
        config.Mapping.PaymentMeans = new PaymentMeansMapping { TypeCode = new FieldMapping { Value = value } };

        Should.Throw<Exception>(() => InvoiceMapper.Map([new Dictionary<string, string>()], config));
    }

    // --- Exporter: Version und Profil (Schreibweisen und Rückfälle), erkennbar am Guideline-Parameter ---

    [TestCase("Extended", "urn:factur-x.eu:1p0:extended")]
    [TestCase("extended", "urn:factur-x.eu:1p0:extended")]
    [TestCase("EXTENDED", "urn:factur-x.eu:1p0:extended")]
    [TestCase("Comfort", "urn:cen.eu:en16931:2017")]
    [TestCase("EN16931", "urn:cen.eu:en16931:2017")]
    [TestCase("en16931", "urn:cen.eu:en16931:2017")]
    [TestCase("Basic", "urn:factur-x.eu:1p0:basic")]
    [TestCase("BasicWL", "urn:factur-x.eu:1p0:basicwl")]
    [TestCase("basicwl", "urn:factur-x.eu:1p0:basicwl")]
    [TestCase("Minimum", "urn:factur-x.eu:1p0:minimum")]
    [TestCase("XRechnung", "xrechnung")]
    [TestCase("", "urn:factur-x.eu:1p0:extended")]
    [TestCase("Unbekannt", "urn:factur-x.eu:1p0:extended")]
    public void SaveXml_Profile_Spellings_PreserveFallback(string profile, string expectedGuidelineFragment)
    {
        var config = TestHelper.LoadTestConfig("config_rows.yml");
        config.Zugferd.Profile = profile;
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");
        var path = Path.Combine(_tempDir, "profile.xml");

        InvoiceExporter.SaveXml(desc, path, config.Zugferd);

        var guideline = ReadGuidelineId(path);
        guideline.ShouldContain(expectedGuidelineFragment, Case.Insensitive);
        if (expectedGuidelineFragment == "urn:cen.eu:en16931:2017")
            guideline.ShouldBe("urn:cen.eu:en16931:2017");
    }

    [TestCase("2.3", "urn:factur-x.eu:1p0")]
    [TestCase("23", "urn:factur-x.eu:1p0")]
    [TestCase("2.0", "urn:zugferd.de:2p0")]
    [TestCase("20", "urn:zugferd.de:2p0")]
    [TestCase("", "urn:factur-x.eu:1p0")]
    [TestCase("3.0", "urn:factur-x.eu:1p0")]
    public void SaveXml_Version_Spellings_PreserveFallback(string version, string expectedGuidelineFragment)
    {
        var config = TestHelper.LoadTestConfig("config_rows.yml");
        config.Zugferd.Version = version;
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");
        var path = Path.Combine(_tempDir, "version.xml");

        InvoiceExporter.SaveXml(desc, path, config.Zugferd);

        ReadGuidelineId(path).ShouldContain(expectedGuidelineFragment);
    }

    [TestCase("1")]
    [TestCase("1.0")]
    public void SaveXml_Version1_WritesCrossIndustryDocument(string version)
    {
        var config = TestHelper.LoadTestConfig("config_rows.yml");
        config.Zugferd.Version = version;
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");
        var path = Path.Combine(_tempDir, "v1.xml");

        InvoiceExporter.SaveXml(desc, path, config.Zugferd);

        File.ReadAllText(path).ShouldContain("CrossIndustryDocument");
    }

    // --- Helfer ---

    private static AppConfig CreateMinimalConfig() => new()
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

    private static InvoiceDescriptor MapSingleRowsItem(Action<LineItemFieldsMapping> configure)
    {
        var config = CreateMinimalConfig();
        var fields = new LineItemFieldsMapping
        {
            Name = new FieldMapping { Value = "Artikel" },
            NetUnitPrice = new FieldMapping { Value = "10" },
            BilledQuantity = new FieldMapping { Value = "1" },
            TaxPercent = new FieldMapping { Value = "19" },
        };
        configure(fields);
        config.Mapping.LineItems = new LineItemsMapping { Mode = "rows", Fields = fields };

        return InvoiceMapper.Map([new Dictionary<string, string>()], config);
    }

    private static string ReadGuidelineId(string xmlPath)
    {
        var xml = System.Xml.Linq.XDocument.Load(xmlPath);
        return xml.Descendants().First(e => e.Name.LocalName == "GuidelineSpecifiedDocumentContextParameter")
            .Elements().First(e => e.Name.LocalName == "ID").Value;
    }
}
