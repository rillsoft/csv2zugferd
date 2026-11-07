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
    public void Load_DirectDebitConfig_PaymentMeans()
    {
        var config = TestHelper.LoadTestConfig("config_directdebit.yml");

        var paymentMeans = config.Mapping.PaymentMeans!;
        paymentMeans.TypeCode!.Default.ShouldBe("58");
        paymentMeans.TypeCode.Rules!.Count.ShouldBe(1);
        paymentMeans.SellerAccounts!.Count.ShouldBe(1);
        paymentMeans.DirectDebit!.CreditorId!.Value.ShouldBe("DE98ZZZ09999999999");
        paymentMeans.DirectDebit.MandateReference!.Column.ShouldBe("Mandat");
        paymentMeans.DirectDebit.BuyerIban!.Column.ShouldBe("Kunden_IBAN");
    }

    [Test]
    public void Load_CreditNoteConfig_InvoiceTypeCodeRules()
    {
        var config = TestHelper.LoadTestConfig("config_creditnote.yml");

        var typeCode = config.Mapping.Invoice!.TypeCode!;
        typeCode.Default.ShouldBe("380");
        typeCode.Rules!.Count.ShouldBe(2);
        typeCode.Rules[0].When!.Column.ShouldBe("Belegart");
        typeCode.Rules[0].Value.ShouldBe("381");

        var precedingInvoices = config.Mapping.Invoice.PrecedingInvoices!;
        precedingInvoices.Count.ShouldBe(1);
        precedingInvoices[0].Number!.Column.ShouldBe("Ursprungsrechnung");
        precedingInvoices[0].Date!.Column.ShouldBe("Ursprungsdatum");
    }

    [Test]
    public void Load_MissingFile_ThrowsConfigNotFound()
    {
        var exception = Should.Throw<Csv2ZugferdException>(() => YamlConfigLoader.Load("nonexistent_file_xyz.yml"));

        exception.Code.ShouldBe(ErrorCodes.ConfigNotFound);
    }

    [Test]
    public void Load_InvalidYaml_ThrowsConfigInvalidYaml()
    {
        var yamlPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "invalid.yml");
        File.WriteAllText(yamlPath, "csv:" + Environment.NewLine + "  separator: [" + Environment.NewLine);
        try
        {
            var exception = Should.Throw<Csv2ZugferdException>(() => YamlConfigLoader.Load(yamlPath));

            exception.Code.ShouldBe(ErrorCodes.ConfigInvalidYaml);
            exception.Message.ShouldContain("Zeile");
        }
        finally
        {
            File.Delete(yamlPath);
        }
    }

    [Test]
    public void LoadFromString_ScalarInsteadOfMapping_ExplainsExpectedObject()
    {
        var exception = Should.Throw<Csv2ZugferdException>(() => YamlConfigLoader.LoadFromString("mapping:\n  lineItems:\n    fixedFields:\n      unitCode: C62\n"));

        exception.Code.ShouldBe(ErrorCodes.ConfigInvalidYaml);
        exception.Message.ShouldContain("Objekt");
        exception.Message.ShouldContain("value");
    }

    [Test]
    public void LoadFromString_Empty_ThrowsConfigInvalidYaml()
    {
        Should.Throw<Csv2ZugferdException>(() => YamlConfigLoader.LoadFromString(string.Empty))
            .Code.ShouldBe(ErrorCodes.ConfigInvalidYaml);
    }

    [Test]
    public void LoadFromString_RowsConfig_EqualsLoadFromFile()
    {
        var path = TestHelper.GetTestDataPath("config_rows.yml");

        var fromFile = YamlConfigLoader.Load(path);
        var fromString = YamlConfigLoader.LoadFromString(File.ReadAllText(path));

        System.Text.Json.JsonSerializer.Serialize(fromString).ShouldBe(System.Text.Json.JsonSerializer.Serialize(fromFile));
    }

    [Test]
    public void FindUnknownKeys_MisspelledKey_ReturnsDottedPath()
    {
        var unknown = YamlConfigLoader.FindUnknownKeys("""
            mapping:
              invoice:
                number:
                  column: "Rechnungsnummer"
                invoiceDate:
                  colum: "Datum"
            """);

        unknown.ShouldBe(["mapping.invoice.number", "mapping.invoice.invoiceDate.colum"]);
    }

    [TestCase("config_rows.yml")]
    [TestCase("config_columns_suffix.yml")]
    [TestCase("config_columns_prefix.yml")]
    [TestCase("config_creditnote.yml")]
    [TestCase("config_directdebit.yml")]
    public void FindUnknownKeys_AllValidTestdataConfigs_ReturnEmpty(string configFile)
    {
        var unknown = YamlConfigLoader.FindUnknownKeys(File.ReadAllText(TestHelper.GetTestDataPath(configFile)));

        unknown.ShouldBeEmpty();
    }

    [Test]
    public void FindUnknownKeys_FixedFieldsTypo_ReturnsFullPath()
    {
        var unknown = YamlConfigLoader.FindUnknownKeys("""
            mapping:
              lineItems:
                mode: "columns"
                fixedFields:
                  unitcode:
                    value: "C62"
                  taxType:
                    valeu: "VAT"
            """);

        unknown.ShouldBe(["mapping.lineItems.fixedFields.unitcode", "mapping.lineItems.fixedFields.taxType.valeu"]);
    }

    [Test]
    public void FindUnknownKeys_ListIndexAndColumnsFields_ReportOnlyRealUnknowns()
    {
        var unknown = YamlConfigLoader.FindUnknownKeys("""
            mapping:
              seller:
                taxRegistrations:
                  - value: "201/113/40209"
                    scheme: "FC"
                  - value: "DE123456789"
                    schema: "VA"
              lineItems:
                columnsFields:
                  irgendwas: "Artikel"
            """);

        unknown.ShouldBe(["mapping.seller.taxRegistrations[1].schema"]);
    }

    [Test]
    public void FindUnknownKeys_InvalidYaml_ThrowsConfigInvalidYaml()
    {
        Should.Throw<Csv2ZugferdException>(() => YamlConfigLoader.FindUnknownKeys("a: [")).Code.ShouldBe(ErrorCodes.ConfigInvalidYaml);
    }

    [Test]
    public void Load_NonExistentFile_ThrowsException()
    {
        Should.Throw<Exception>(() => YamlConfigLoader.Load("nonexistent_file_xyz.yml"));
    }
}
