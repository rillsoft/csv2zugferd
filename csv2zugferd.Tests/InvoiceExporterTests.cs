using System.Xml.Linq;
using NUnit.Framework;
using s2industries.ZUGFeRD;
using Shouldly;

namespace Csv2Zugferd.Tests;

[TestFixture]
public class InvoiceExporterTests
{
    private InvoiceDescriptor _desc = null!;
    private Models.ZugferdConfig _zugferdConfig = null!;
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");
        _zugferdConfig = TestHelper.LoadTestConfig("config_rows.yml").Zugferd;
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Test]
    public void SaveXml_CreatesFile()
    {
        var path = Path.Combine(_tempDir, "test.xml");

        InvoiceExporter.SaveXml(_desc, path, _zugferdConfig);

        File.Exists(path).ShouldBeTrue();
    }

    [Test]
    public void SaveXml_FileNotEmpty()
    {
        var path = Path.Combine(_tempDir, "test.xml");

        InvoiceExporter.SaveXml(_desc, path, _zugferdConfig);

        new FileInfo(path).Length.ShouldBeGreaterThan(0);
    }

    [Test]
    public void SaveXml_ValidXml()
    {
        var path = Path.Combine(_tempDir, "test.xml");
        InvoiceExporter.SaveXml(_desc, path, _zugferdConfig);

        Should.NotThrow(() => XDocument.Load(path));
    }

    [Test]
    public void SaveXml_ContainsInvoiceNumber()
    {
        var path = Path.Combine(_tempDir, "test.xml");
        InvoiceExporter.SaveXml(_desc, path, _zugferdConfig);

        var content = File.ReadAllText(path);
        content.ShouldContain("471102");
    }

    [Test]
    public void SaveXml_ContainsTradeLineItems()
    {
        var path = Path.Combine(_tempDir, "test.xml");
        InvoiceExporter.SaveXml(_desc, path, _zugferdConfig);

        var content = File.ReadAllText(path);
        content.ShouldContain("IncludedSupplyChainTradeLineItem");
    }

    [Test]
    public void SaveXml_VersionAndProfile()
    {
        var path = Path.Combine(_tempDir, "test.xml");
        InvoiceExporter.SaveXml(_desc, path, _zugferdConfig);

        var content = File.ReadAllText(path);
        content.ShouldContain("urn:cen.eu:en16931:2017");
    }

    [Test]
    public void SaveXml_ContainsSellerBankAccount()
    {
        var path = Path.Combine(_tempDir, "test.xml");
        InvoiceExporter.SaveXml(_desc, path, _zugferdConfig);

        var content = File.ReadAllText(path);
        content.ShouldContain("SpecifiedTradeSettlementPaymentMeans");
        content.ShouldContain("<ram:TypeCode>58</ram:TypeCode>");
        content.ShouldContain("<ram:IBANID>DE02120300000000202051</ram:IBANID>");
        content.ShouldContain("<ram:PaymentReference>471102</ram:PaymentReference>");
    }

    [TestCase("Extended")]
    [TestCase("EN16931")]
    [TestCase("XRechnung")]
    public void SaveXml_DirectDebit(string profile)
    {
        var config = TestHelper.LoadTestConfig("config_directdebit.yml");
        config.Zugferd.Profile = profile;
        var rows = TestHelper.ReadTestCsv("test_directdebit.csv", config.Csv);
        var desc = InvoiceMapper.Map(rows, config);
        var path = Path.Combine(_tempDir, "dd.xml");

        InvoiceExporter.SaveXml(desc, path, config.Zugferd);

        var content = File.ReadAllText(path);
        content.ShouldContain("<ram:TypeCode>59</ram:TypeCode>");
        content.ShouldContain("<ram:CreditorReferenceID>DE98ZZZ09999999999</ram:CreditorReferenceID>");
        content.ShouldContain("<ram:DirectDebitMandateID>MANDAT-2013-001</ram:DirectDebitMandateID>");
        content.ShouldContain("<ram:IBANID>DE89370400440532013000</ram:IBANID>");
    }

    [Test]
    [Ignore("Requires PDF template file")]
    public void SavePdfAsync_CreatesFile()
    {
        // Requires a real PDF template – skipped in automated runs
    }

    [Test]
    [Ignore("Requires PDF template file")]
    public void SavePdfAsync_FileNotEmpty()
    {
        // Requires a real PDF template – skipped in automated runs
    }
}
