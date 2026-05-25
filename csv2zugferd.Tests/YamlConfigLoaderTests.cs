using Csv2Zugferd.Models;
using NUnit.Framework;
using Shouldly;

namespace Csv2Zugferd.Tests;

[TestFixture]
public class YamlConfigLoaderTests
{
    [Test]
    public void Load_RowsConfig_ReturnsValidConfig()
    {
        var config = TestHelper.LoadTestConfig("config_rows.yml");

        config.Mapping.LineItems!.Mode.ShouldBe("rows");
    }

    [Test]
    public void Load_ColumnsSuffixConfig_ReturnsColumnsMode()
    {
        var config = TestHelper.LoadTestConfig("config_columns_suffix.yml");

        config.Mapping.LineItems!.Mode.ShouldBe("columns");
        config.Mapping.LineItems.ColumnPattern!.Style.ShouldBe("suffix");
    }

    [Test]
    public void Load_ColumnsPrefixConfig_ReturnsPrefixStyle()
    {
        var config = TestHelper.LoadTestConfig("config_columns_prefix.yml");

        config.Mapping.LineItems!.ColumnPattern!.Style.ShouldBe("prefix");
    }

    [Test]
    public void Load_RowsConfig_CsvSettings()
    {
        var config = TestHelper.LoadTestConfig("config_rows.yml");

        config.Csv.Separator.ShouldBe(";");
        config.Csv.DecimalSeparator.ShouldBe(",");
    }

    [Test]
    public void Load_RowsConfig_ZugferdSettings()
    {
        var config = TestHelper.LoadTestConfig("config_rows.yml");

        config.Zugferd.Version.ShouldBe("2.3");
        config.Zugferd.Profile.ShouldBe("Extended");
    }

    [Test]
    public void Load_RowsConfig_InvoiceMapping()
    {
        var config = TestHelper.LoadTestConfig("config_rows.yml");

        config.Mapping.Invoice!.InvoiceNumber!.Column.ShouldBe("Rechnungsnummer");
    }

    [Test]
    public void Load_RowsConfig_BuyerMapping()
    {
        var config = TestHelper.LoadTestConfig("config_rows.yml");

        config.Mapping.Buyer!.Name!.Column.ShouldBe("Käufer_Name");
    }

    [Test]
    public void Load_RowsConfig_SellerMapping()
    {
        var config = TestHelper.LoadTestConfig("config_rows.yml");

        config.Mapping.Seller!.TaxRegistrations!.Count.ShouldBe(2);
    }

    [Test]
    public void Load_RowsConfig_LineItemFields()
    {
        var config = TestHelper.LoadTestConfig("config_rows.yml");

        config.Mapping.LineItems!.Fields!.Name!.Column.ShouldBe("Artikel_Name");
    }

    [Test]
    public void Load_RowsConfig_AllowanceCharge()
    {
        var config = TestHelper.LoadTestConfig("config_rows.yml");

        config.Mapping.LineItems!.AllowanceCharge!.Enabled.ShouldBeTrue();
    }

    [Test]
    public void Load_ColumnsConfig_FieldFragments()
    {
        var config = TestHelper.LoadTestConfig("config_columns_suffix.yml");

        config.Mapping.LineItems!.Fields!.Name!.Column.ShouldBe("Artikel");
    }

    [Test]
    public void Load_ColumnsConfig_FixedFields()
    {
        var config = TestHelper.LoadTestConfig("config_columns_suffix.yml");

        config.Mapping.LineItems!.FixedFields!["unitCode"].Value.ShouldBe("C62");
    }

    [Test]
    public void Load_SellerTaxRegistrations_SupportsFixedValues()
    {
        var yamlPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "seller_tax_values.yml");
        try
        {
            File.WriteAllText(yamlPath, """
                mapping:
                  seller:
                    taxRegistrations:
                      - value: "201/113/40209"
                        scheme: "FC"
                      - value: "DE123456789"
                        scheme: "VA"
                """);

            var config = YamlConfigLoader.Load(yamlPath);

            config.Mapping.Seller!.TaxRegistrations!.Count.ShouldBe(2);
            config.Mapping.Seller.TaxRegistrations[0].Value.ShouldBe("201/113/40209");
            config.Mapping.Seller.TaxRegistrations[0].Scheme.ShouldBe("FC");
            config.Mapping.Seller.TaxRegistrations[1].Value.ShouldBe("DE123456789");
            config.Mapping.Seller.TaxRegistrations[1].Scheme.ShouldBe("VA");
        }
        finally
        {
            File.Delete(yamlPath);
        }
    }

    [Test]
    public void CreateDefault_ReturnsValidConfig()
    {
        var config = YamlConfigLoader.CreateDefault();

        config.Csv.Separator.ShouldBe(";");
        config.Mapping.LineItems!.Mode.ShouldBe("rows");
    }

    [Test]
    public void Load_NonExistentFile_ThrowsException()
    {
        Should.Throw<Exception>(() => YamlConfigLoader.Load("nonexistent_file_xyz.yml"));
    }
}
