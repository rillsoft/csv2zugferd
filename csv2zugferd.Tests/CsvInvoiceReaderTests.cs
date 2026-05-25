using Csv2Zugferd.Models;
using NUnit.Framework;
using Shouldly;

namespace Csv2Zugferd.Tests;

[TestFixture]
public class CsvInvoiceReaderTests
{
    private CsvConfig _defaultConfig = null!;

    [SetUp]
    public void SetUp()
    {
        _defaultConfig = TestHelper.LoadTestConfig("config_rows.yml").Csv;
    }

    [Test]
    public void Read_RowsCsv_ReturnsTwoRows()
    {
        var rows = TestHelper.ReadTestCsv("test_rows.csv", _defaultConfig);

        rows.Count.ShouldBe(2);
    }

    [Test]
    public void Read_RowsCsv_HeadersCorrect()
    {
        var rows = TestHelper.ReadTestCsv("test_rows.csv", _defaultConfig);

        rows[0].ContainsKey("Rechnungsnummer").ShouldBeTrue();
    }

    [Test]
    public void Read_RowsCsv_InvoiceNumber()
    {
        var rows = TestHelper.ReadTestCsv("test_rows.csv", _defaultConfig);

        rows[0]["Rechnungsnummer"].ShouldBe("471102");
    }

    [Test]
    public void Read_RowsCsv_DecimalValues()
    {
        var rows = TestHelper.ReadTestCsv("test_rows.csv", _defaultConfig);

        rows[0]["Einzelpreis_Netto"].ShouldBe("14,95");
    }

    [Test]
    public void Read_ColumnsSuffixCsv_ReturnsOneRow()
    {
        var config = TestHelper.LoadTestConfig("config_columns_suffix.yml").Csv;
        var rows = TestHelper.ReadTestCsv("test_columns_suffix.csv", config);

        rows.Count.ShouldBe(1);
    }

    [Test]
    public void Read_ColumnsSuffixCsv_NumberedColumns()
    {
        var config = TestHelper.LoadTestConfig("config_columns_suffix.yml").Csv;
        var rows = TestHelper.ReadTestCsv("test_columns_suffix.csv", config);

        rows[0].ContainsKey("Artikel_001").ShouldBeTrue();
    }

    [Test]
    public void Read_ColumnsPrefixCsv_PrefixColumns()
    {
        var config = TestHelper.LoadTestConfig("config_columns_prefix.yml").Csv;
        var rows = TestHelper.ReadTestCsv("test_columns_prefix.csv", config);

        rows[0].ContainsKey("Pos1_Artikel").ShouldBeTrue();
    }

    [Test]
    public void Read_CaseInsensitiveHeaders()
    {
        var rows = TestHelper.ReadTestCsv("test_rows.csv", _defaultConfig);

        rows[0]["rechnungsnummer"].ShouldBe("471102");
    }

    [Test]
    public void Read_EmptyFile_ReturnsEmptyList()
    {
        var rows = TestHelper.ReadTestCsv("test_empty.csv", _defaultConfig);

        rows.Count.ShouldBe(0);
    }

    [Test]
    public void Read_TabSeparator_ParsesCorrectly()
    {
        var tabConfig = new CsvConfig
        {
            Separator = "\\t",
            Encoding = "UTF-8",
            HasHeader = true,
            DecimalSeparator = ","
        };
        var rows = TestHelper.ReadTestCsv("test_tab_separator.csv", tabConfig);

        rows.Count.ShouldBe(1);
        rows[0]["Rechnungsnummer"].ShouldBe("471102");
    }
}
