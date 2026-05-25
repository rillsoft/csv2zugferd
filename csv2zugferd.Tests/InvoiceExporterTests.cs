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
