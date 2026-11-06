using Csv2Zugferd.Models;
using NUnit.Framework;
using s2industries.ZUGFeRD;
using Shouldly;

namespace Csv2Zugferd.Tests;

[TestFixture]
public class InvoiceMapperTests
{
    // ── 4.3.1 Grundfunktionen ──────────────────────────────────────────────

    [Test]
    public void Map_RowsMode_InvoiceNumber()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.InvoiceNo.ShouldBe("471102");
    }

    [Test]
    public void Map_RowsMode_InvoiceDate()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.InvoiceDate.ShouldBe(new DateTime(2013, 6, 5));
    }

    [Test]
    public void Map_RowsMode_Currency()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.Currency.ShouldBe(CurrencyCodes.EUR);
    }

    [Test]
    public void Map_RowsMode_InvoiceName()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.Name.ShouldBe("WARENRECHNUNG");
    }

    // ── 4.3.1a Rechnungstyp (BT-3) ────────────────────────────────────────

    [Test]
    public void Map_InvoiceTypeCode_NotConfigured_DefaultsToInvoice()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.Type.ShouldBe(InvoiceType.Invoice);
    }

    [Test]
    public void Map_InvoiceTypeCode_Value381_SetsCreditNote()
    {
        var desc = MapWithInvoiceTypeCode(new FieldMapping { Value = "381" });

        desc.Type.ShouldBe(InvoiceType.CreditNote);
    }

    [Test]
    public void Map_InvoiceTypeCode_Name_SetsCorrection()
    {
        var desc = MapWithInvoiceTypeCode(new FieldMapping { Value = "correction" });

        desc.Type.ShouldBe(InvoiceType.Correction);
    }

    [Test]
    public void Map_InvoiceTypeCode_ViaRule_SetsCreditNote()
    {
        var config = TestHelper.LoadTestConfig("config_creditnote.yml");
        var rows = TestHelper.ReadTestCsv("test_creditnote.csv", config.Csv);

        InvoiceMapper.Map(rows, config).Type.ShouldBe(InvoiceType.CreditNote);

        foreach (var row in rows)
            row["Belegart"] = "Rechnung";
        InvoiceMapper.Map(rows, config).Type.ShouldBe(InvoiceType.Invoice);
    }

    [Test]
    public void Map_InvoiceTypeCode_Unknown_Throws()
    {
        var exception = Should.Throw<InvalidOperationException>(() => MapWithInvoiceTypeCode(new FieldMapping { Value = "999" }));

        exception.Message.ShouldContain("BT-3");
        exception.Message.ShouldContain("999");
    }

    [Test]
    public void CheckDocument_XRechnungWithDisallowedTypeCode_Warns()
    {
        var debitNote = MapWithInvoiceTypeCode(new FieldMapping { Value = "383" });
        var creditNote = MapWithInvoiceTypeCode(new FieldMapping { Value = "381" });

        InvoiceMapper.CheckDocument(debitNote, "XRechnung").ShouldContain(w => w.Contains("BR-DE-17") && w.Contains("383"));
        InvoiceMapper.CheckDocument(debitNote, "Extended").ShouldBeEmpty();
        InvoiceMapper.CheckDocument(creditNote, "XRechnung").ShouldNotContain(w => w.Contains("BR-DE-17"));
    }

    private static InvoiceDescriptor MapWithInvoiceTypeCode(FieldMapping? typeCode, List<PrecedingInvoiceMapping>? precedingInvoices = null)
    {
        var config = new AppConfig
        {
            Csv = new CsvConfig(),
            Mapping = new MappingConfig
            {
                Invoice = new InvoiceMapping
                {
                    InvoiceNumber = new FieldMapping { Value = "TEST" },
                    InvoiceDate = new FieldMapping { Value = "01.01.2023" },
                    Currency = new FieldMapping { Value = "EUR" },
                    TypeCode = typeCode,
                    PrecedingInvoices = precedingInvoices,
                },
            }
        };
        var rows = new List<Dictionary<string, string>> { new(StringComparer.OrdinalIgnoreCase) };
        return InvoiceMapper.Map(rows, config);
    }

    // ── 4.3.1b Vorgängerrechnung (BT-25/BT-26) ────────────────────────────

    [Test]
    public void Map_PrecedingInvoices_NumberAndDate()
    {
        var desc = TestHelper.MapFromTestData("config_creditnote.yml", "test_creditnote.csv");

        var references = desc.GetInvoiceReferencedDocuments();
        references.Count.ShouldBe(1);
        references[0].ID.ShouldBe("471102");
        references[0].IssueDateTime.ShouldBe(new DateTime(2013, 6, 5));
    }

    [Test]
    public void Map_PrecedingInvoices_TwoEntries_TwoReferences()
    {
        var desc = MapWithInvoiceTypeCode(new FieldMapping { Value = "381" },
        [
            new PrecedingInvoiceMapping { Number = new FieldMapping { Value = "R-1" }, Date = new FieldMapping { Value = "01.03.2023" } },
            new PrecedingInvoiceMapping { Number = new FieldMapping { Value = "R-2" } },
        ]);

        var references = desc.GetInvoiceReferencedDocuments();
        references.Select(r => r.ID).ShouldBe(["R-1", "R-2"]);
        references[0].IssueDateTime.ShouldBe(new DateTime(2023, 3, 1));
        references[1].IssueDateTime.ShouldBeNull();
    }

    [Test]
    public void Map_PrecedingInvoices_EmptyNumber_Skipped()
    {
        var desc = MapWithInvoiceTypeCode(new FieldMapping { Value = "381" },
        [
            new PrecedingInvoiceMapping { Number = new FieldMapping { Column = "Leer" } },
            new PrecedingInvoiceMapping { Number = new FieldMapping { Value = "R-1" } },
        ]);

        desc.GetInvoiceReferencedDocuments().Select(r => r.ID).ShouldBe(["R-1"]);
    }

    [Test]
    public void CheckDocument_CreditNoteWithoutPrecedingInvoice_Warns()
    {
        var creditNote = MapWithInvoiceTypeCode(new FieldMapping { Value = "381" });
        var correction = MapWithInvoiceTypeCode(new FieldMapping { Value = "384" });
        var invoice = MapWithInvoiceTypeCode(new FieldMapping { Value = "380" });
        var withReference = MapWithInvoiceTypeCode(new FieldMapping { Value = "384" },
            [new PrecedingInvoiceMapping { Number = new FieldMapping { Value = "R-1" } }]);

        InvoiceMapper.CheckDocument(creditNote, "Extended").ShouldContain(w => w.Contains("BT-25") && !w.Contains("BR-DE-26"));
        InvoiceMapper.CheckDocument(correction, "XRechnung").ShouldContain(w => w.Contains("BT-25") && w.Contains("BR-DE-26"));
        InvoiceMapper.CheckDocument(invoice, "XRechnung").ShouldBeEmpty();
        InvoiceMapper.CheckDocument(withReference, "XRechnung").ShouldBeEmpty();
    }

    [Test]
    public void CheckDocument_MinimumWithPrecedingInvoice_Warns()
    {
        var desc = MapWithInvoiceTypeCode(new FieldMapping { Value = "381" },
            [new PrecedingInvoiceMapping { Number = new FieldMapping { Value = "R-1" } }]);

        InvoiceMapper.CheckDocument(desc, "Minimum").ShouldContain(w => w.Contains("MINIMUM") && w.Contains("BT-25"));
        InvoiceMapper.CheckDocument(desc, "BasicWL").ShouldBeEmpty();
    }

    // ── 4.3.2 Käufer / Verkäufer ──────────────────────────────────────────

    [Test]
    public void Map_RowsMode_BuyerName()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.Buyer.Name.ShouldBe("Kunden Mitte AG");
    }

    [Test]
    public void Map_RowsMode_BuyerId()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.Buyer.ID.ID.ShouldBe("012345");
    }


    [Test]
    public void Map_RowsMode_BuyerAddress()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.Buyer.City.ShouldBe("Frankfurt");
    }

    [Test]
    public void Map_RowsMode_SellerName()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.Seller.Name.ShouldBe("Lieferant GmbH");
    }

    [Test]
    public void Map_RowsMode_SellerId()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.Seller.ID.ID.ShouldBe("1234567890");
    }

    [Test]
    public void Map_RowsMode_SellerTaxRegistrations()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.SellerTaxRegistration.Count.ShouldBe(2);
    }

    [Test]
    public void Map_SellerValues_UsesFixedSellerData()
    {
        var config = new AppConfig
        {
            Csv = new CsvConfig(),
            Mapping = new MappingConfig
            {
                Invoice = new InvoiceMapping
                {
                    InvoiceNumber = new FieldMapping { Value = "TEST" },
                    InvoiceDate = new FieldMapping { Value = "01.01.2026" },
                    Currency = new FieldMapping { Value = "EUR" },
                },
                Seller = new SellerMapping
                {
                    Name = new FieldMapping { Value = "Fester Lieferant GmbH" },
                    Id = new FieldMapping { Value = "10000000001" },
                    PostalCode = new FieldMapping { Value = "12345" },
                    City = new FieldMapping { Value = "Musterstadt" },
                    Street = new FieldMapping { Value = "Musterstrasse 1" },
                    Country = new FieldMapping { Value = "DE" },
                }
            }
        };
        var rows = new List<Dictionary<string, string>>
        {
            new(StringComparer.OrdinalIgnoreCase)
        };

        var desc = InvoiceMapper.Map(rows, config);

        desc.Seller.Name.ShouldBe("Fester Lieferant GmbH");
        desc.Seller.ID.ID.ShouldBe("10000000001");
        desc.Seller.Postcode.ShouldBe("12345");
        desc.Seller.City.ShouldBe("Musterstadt");
        desc.Seller.Street.ShouldBe("Musterstrasse 1");
        desc.Seller.Country.ShouldBe(CountryCodes.DE);
    }

    [Test]
    public void Map_SellerTaxRegistrations_UsesFixedValues()
    {
        var config = new AppConfig
        {
            Csv = new CsvConfig(),
            Mapping = new MappingConfig
            {
                Invoice = new InvoiceMapping
                {
                    InvoiceNumber = new FieldMapping { Value = "TEST" },
                    InvoiceDate = new FieldMapping { Value = "01.01.2026" },
                    Currency = new FieldMapping { Value = "EUR" },
                },
                Seller = new SellerMapping
                {
                    Name = new FieldMapping { Value = "Fester Lieferant GmbH" },
                    Country = new FieldMapping { Value = "DE" },
                    TaxRegistrations =
                    [
                        new TaxRegistrationMapping { Value = "12/345/67890", Scheme = "FC" },
                        new TaxRegistrationMapping { Value = "DE123456789", Scheme = "VA" },
                    ],
                }
            }
        };
        var rows = new List<Dictionary<string, string>>
        {
            new(StringComparer.OrdinalIgnoreCase)
        };

        var desc = InvoiceMapper.Map(rows, config);

        desc.SellerTaxRegistration.Count.ShouldBe(2);
    }

    // ── 4.3.3 Positionen (rows) ───────────────────────────────────────────

    [Test]
    public void Map_RowsMode_TwoLineItems()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.TradeLineItems.Count.ShouldBe(2);
    }

    [Test]
    public void Map_RowsMode_FirstItemName()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.TradeLineItems[0].Name.ShouldBe("Test1");
    }

    [Test]
    public void Map_RowsMode_FirstItemPrice()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.TradeLineItems[0].NetUnitPrice.ShouldBe(14.95m);
    }

    [Test]
    public void Map_RowsMode_FirstItemQuantity()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.TradeLineItems[0].BilledQuantity.ShouldBe(6m);
    }

    [Test]
    public void Map_RowsMode_FirstItemTaxPercent()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.TradeLineItems[0].TaxPercent.ShouldBe(19m);
    }

    [Test]
    public void Map_RowsMode_FirstItemDiscount()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");
        var charges = desc.TradeLineItems[0].GetTradeAllowanceCharges();

        charges.Count.ShouldBe(1);
        charges[0].ActualAmount.ShouldBe(17.94m);
    }

    [Test]
    public void Map_RowsMode_SecondItemNoDiscount()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.TradeLineItems[1].GetTradeAllowanceCharges().Count.ShouldBe(0);
    }

    // ── 4.3.4 Positionen (columns) ────────────────────────────────────────

    [Test]
    public void Map_ColumnsSuffix_TwoLineItems()
    {
        var desc = TestHelper.MapFromTestData("config_columns_suffix.yml", "test_columns_suffix.csv");

        desc.TradeLineItems.Count.ShouldBe(2);
    }

    [Test]
    public void Map_ColumnsSuffix_FirstItemName()
    {
        var desc = TestHelper.MapFromTestData("config_columns_suffix.yml", "test_columns_suffix.csv");

        desc.TradeLineItems[0].Name.ShouldBe("Test1");
    }

    [Test]
    public void Map_ColumnsSuffix_Discount()
    {
        var desc = TestHelper.MapFromTestData("config_columns_suffix.yml", "test_columns_suffix.csv");

        desc.TradeLineItems[0].GrossUnitPrice.ShouldBe(14.95m);
        desc.TradeLineItems[0].NetUnitPrice.ShouldBe(11.96m);
        desc.TradeLineItems[0].GetTradeAllowanceCharges().Count.ShouldBe(0);
    }

    [Test]
    public void Map_ColumnsPrefix_TwoLineItems()
    {
        var desc = TestHelper.MapFromTestData("config_columns_prefix.yml", "test_columns_prefix.csv");

        desc.TradeLineItems.Count.ShouldBe(2);
    }

    [Test]
    public void Map_ColumnsPrefix_FirstItemName()
    {
        var desc = TestHelper.MapFromTestData("config_columns_prefix.yml", "test_columns_prefix.csv");

        desc.TradeLineItems[0].Name.ShouldBe("Test1");
    }

    [Test]
    public void Map_ColumnsPrefix_StopsAtEmptyName()
    {
        var desc = TestHelper.MapFromTestData("config_columns_prefix.yml", "test_columns_prefix.csv");

        desc.TradeLineItems.Count.ShouldBe(2);
    }

    [Test]
    public void Map_ColumnsMode_UsesDirectColumnForGlobalTaxPercent()
    {
        var config = new AppConfig
        {
            Csv = new CsvConfig
            {
                DecimalSeparator = "."
            },
            Mapping = new MappingConfig
            {
                Invoice = new InvoiceMapping
                {
                    InvoiceNumber = new FieldMapping { Value = "TEST" },
                    InvoiceDate = new FieldMapping { Value = "01.01.2026" },
                    Currency = new FieldMapping { Value = "EUR" },
                },
                LineItems = new LineItemsMapping
                {
                    Mode = "columns",
                    ColumnPattern = new ColumnPatternConfig
                    {
                        Style = "suffix",
                        Separator = "_",
                        StartIndex = 0,
                        ZeroPadding = 0,
                    },
                    Fields = new LineItemFieldsMapping
                    {
                        Name = new FieldMapping { Column = "PRODUCT_NAME" },
                        NetUnitPrice = new FieldMapping { Column = "PRODUCT_PRICE" },
                        BilledQuantity = new FieldMapping { Column = "PRODUCT_QUANTITY" },
                        TaxPercent = new FieldMapping { Column = "INVOICE_TAX" },
                    },
                },
            }
        };
        var rows = new List<Dictionary<string, string>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["PRODUCT_NAME_0"] = "Rillsoft Cloud Standard",
                ["PRODUCT_PRICE_0"] = "1440.00",
                ["PRODUCT_QUANTITY_0"] = "1",
                ["INVOICE_TAX"] = "19.00",
            }
        };

        var desc = InvoiceMapper.Map(rows, config);

        desc.TradeLineItems.Count.ShouldBe(1);
        desc.TradeLineItems[0].TaxPercent.ShouldBe(19m);
    }

    [Test]
    public void Map_ColumnsMode_MapsSellerAssignedId()
    {
        var config = new AppConfig
        {
            Csv = new CsvConfig
            {
                DecimalSeparator = "."
            },
            Mapping = new MappingConfig
            {
                Invoice = new InvoiceMapping
                {
                    InvoiceNumber = new FieldMapping { Value = "TEST" },
                    InvoiceDate = new FieldMapping { Value = "01.01.2026" },
                    Currency = new FieldMapping { Value = "EUR" },
                },
                LineItems = new LineItemsMapping
                {
                    Mode = "columns",
                    ColumnPattern = new ColumnPatternConfig
                    {
                        Style = "suffix",
                        Separator = "_",
                        StartIndex = 0,
                        ZeroPadding = 0,
                    },
                    Fields = new LineItemFieldsMapping
                    {
                        Name = new FieldMapping { Column = "PRODUCT_NAME" },
                        SellerAssignedId = new FieldMapping { Column = "PRODUCT_CODE" },
                        NetUnitPrice = new FieldMapping { Column = "PRODUCT_PRICE" },
                        BilledQuantity = new FieldMapping { Column = "PRODUCT_QUANTITY" },
                        TaxPercent = new FieldMapping { Value = "19.00" },
                    },
                },
            }
        };
        var rows = new List<Dictionary<string, string>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["PRODUCT_CODE_0"] = "00105",
                ["PRODUCT_NAME_0"] = "Rillsoft Project 9 Light Einzelplatz-Lizenz",
                ["PRODUCT_PRICE_0"] = "357.00",
                ["PRODUCT_QUANTITY_0"] = "2",
            }
        };

        var desc = InvoiceMapper.Map(rows, config);

        desc.TradeLineItems[0].SellerAssignedID.ShouldBe("00105");
        desc.TradeLineItems[0].Description.ShouldNotBe("00105");
    }

    [Test]
    public void Map_ColumnsMode_ParsesDiscountWithPercentSign()
    {
        var config = new AppConfig
        {
            Csv = new CsvConfig
            {
                DecimalSeparator = "."
            },
            Mapping = new MappingConfig
            {
                Invoice = new InvoiceMapping
                {
                    InvoiceNumber = new FieldMapping { Value = "TEST" },
                    InvoiceDate = new FieldMapping { Value = "01.01.2026" },
                    Currency = new FieldMapping { Value = "EUR" },
                },
                LineItems = new LineItemsMapping
                {
                    Mode = "columns",
                    ColumnPattern = new ColumnPatternConfig
                    {
                        Style = "suffix",
                        Separator = "_",
                        StartIndex = 0,
                        ZeroPadding = 0,
                    },
                    Fields = new LineItemFieldsMapping
                    {
                        Name = new FieldMapping { Column = "PRODUCT_NAME" },
                        NetUnitPrice = new FieldMapping { Column = "PRODUCT_PRICE" },
                        BilledQuantity = new FieldMapping { Column = "PRODUCT_QUANTITY" },
                        TaxPercent = new FieldMapping { Value = "19.00" },
                        DiscountPercent = new FieldMapping { Column = "PRODUCT_DISCOUNT" },
                    },
                },
            }
        };
        var rows = new List<Dictionary<string, string>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["PRODUCT_NAME_0"] = "Rillsoft Project 9 Light Einzelplatz-Lizenz",
                ["PRODUCT_PRICE_0"] = "357.00",
                ["PRODUCT_QUANTITY_0"] = "2",
                ["PRODUCT_DISCOUNT_0"] = "10%",
            }
        };

        var desc = InvoiceMapper.Map(rows, config);

        desc.TradeLineItems[0].GrossUnitPrice.ShouldBe(357.00m);
        desc.TradeLineItems[0].NetUnitPrice.ShouldBe(321.30m);
        desc.TradeLineItems[0].GetTradeAllowanceCharges().Count.ShouldBe(0);
    }

    [Test]
    public void Map_ColumnsMode_ResolvesFixedFieldRulesPerLineItem()
    {
        var config = new AppConfig
        {
            Csv = new CsvConfig
            {
                DecimalSeparator = "."
            },
            Mapping = new MappingConfig
            {
                Invoice = new InvoiceMapping
                {
                    InvoiceNumber = new FieldMapping { Value = "TEST" },
                    InvoiceDate = new FieldMapping { Value = "01.01.2026" },
                    Currency = new FieldMapping { Value = "EUR" },
                },
                LineItems = new LineItemsMapping
                {
                    Mode = "columns",
                    ColumnPattern = new ColumnPatternConfig
                    {
                        Style = "suffix",
                        Separator = "_",
                        StartIndex = 0,
                        ZeroPadding = 0,
                    },
                    Fields = new LineItemFieldsMapping
                    {
                        Name = new FieldMapping { Column = "PRODUCT_NAME" },
                        NetUnitPrice = new FieldMapping { Column = "PRODUCT_PRICE" },
                        BilledQuantity = new FieldMapping { Column = "PRODUCT_QUANTITY" },
                        TaxPercent = new FieldMapping { Value = "19.00" },
                    },
                    FixedFields = new Dictionary<string, FieldMapping>
                    {
                        ["unitCode"] = new()
                        {
                            Default = "C62",
                            Rules =
                            [
                                new FieldRule
                                {
                                    When = new FieldRuleCondition { Column = "PRODUCT_CODE", Regex = "^002" },
                                    Value = "ANN",
                                },
                                new FieldRule
                                {
                                    When = new FieldRuleCondition { Column = "PRODUCT_CODE", Regex = "^003" },
                                    Value = "DAY",
                                },
                            ],
                        },
                        ["taxType"] = new() { Value = "VAT" },
                        ["taxCategoryCode"] = new() { Value = "S" },
                    },
                },
            }
        };
        var rows = new List<Dictionary<string, string>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["PRODUCT_CODE_0"] = "00105",
                ["PRODUCT_NAME_0"] = "Lizenz",
                ["PRODUCT_PRICE_0"] = "100.00",
                ["PRODUCT_QUANTITY_0"] = "1",
                ["PRODUCT_CODE_1"] = "00201",
                ["PRODUCT_NAME_1"] = "Wartung",
                ["PRODUCT_PRICE_1"] = "120.00",
                ["PRODUCT_QUANTITY_1"] = "1",
                ["PRODUCT_CODE_2"] = "00301",
                ["PRODUCT_NAME_2"] = "Vor-Ort-Schulung",
                ["PRODUCT_PRICE_2"] = "900.00",
                ["PRODUCT_QUANTITY_2"] = "1",
            }
        };

        var desc = InvoiceMapper.Map(rows, config);

        desc.TradeLineItems[0].UnitCode.ShouldBe(QuantityCodes.C62);
        desc.TradeLineItems[1].UnitCode.ShouldBe(QuantityCodes.ANN);
        desc.TradeLineItems[2].UnitCode.ShouldBe(QuantityCodes.DAY);
    }

    // ── 4.3.5 Summen ──────────────────────────────────────────────────────

    [Test]
    public void Map_AutoTotals_LineTotalAmount()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.LineTotalAmount!.Value.ShouldBe(103.20m);
    }

    [Test]
    public void Map_AutoTotals_AllowanceTotalAmount()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.AllowanceTotalAmount!.Value.ShouldBe(17.94m);
    }

    [Test]
    public void Map_AutoTotals_TaxBasisAmount()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.TaxBasisAmount!.Value.ShouldBe(85.26m);
    }

    [Test]
    public void Map_AutoTotals_GrandTotalAmount()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.GrandTotalAmount!.Value.ShouldBe(101.46m);
    }

    [Test]
    public void Map_AutoTotals_TaxAmount()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.TaxTotalAmount!.Value.ShouldBe(16.20m);
    }

    [Test]
    public void Map_ManualTotals_AddsTaxBreakdownAndDuePayableAmount()
    {
        var config = new AppConfig
        {
            Csv = new CsvConfig
            {
                DecimalSeparator = "."
            },
            Mapping = new MappingConfig
            {
                Invoice = new InvoiceMapping
                {
                    InvoiceNumber = new FieldMapping { Value = "TEST" },
                    InvoiceDate = new FieldMapping { Value = "01.01.2026" },
                    Currency = new FieldMapping { Value = "EUR" },
                },
                LineItems = new LineItemsMapping
                {
                    Mode = "rows",
                    Fields = new LineItemFieldsMapping
                    {
                        Name = new FieldMapping { Column = "Name" },
                        NetUnitPrice = new FieldMapping { Column = "Price" },
                        BilledQuantity = new FieldMapping { Column = "Quantity" },
                        TaxType = new FieldMapping { Value = "VAT" },
                        TaxCategoryCode = new FieldMapping { Value = "S" },
                        TaxPercent = new FieldMapping { Column = "TaxPercent" },
                    },
                },
                Totals = new TotalsMapping
                {
                    Mode = "manual",
                    LineTotalAmount = new FieldMapping { Value = "100.00" },
                    AllowanceTotalAmount = new FieldMapping { Value = "0.00" },
                    TaxBasisAmount = new FieldMapping { Value = "100.00" },
                    TaxTotalAmount = new FieldMapping { Value = "19.00" },
                    GrandTotalAmount = new FieldMapping { Value = "119.00" },
                    TotalPrepaidAmount = new FieldMapping { Value = "0.00" },
                },
            }
        };
        var rows = new List<Dictionary<string, string>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = "TestItem",
                ["Price"] = "100.00",
                ["Quantity"] = "1",
                ["TaxPercent"] = "19.00",
            }
        };

        var desc = InvoiceMapper.Map(rows, config);

        desc.AnyApplicableTradeTaxes().ShouldBeTrue();
        desc.DuePayableAmount!.Value.ShouldBe(119.00m);
    }

    [Test]
    public void Map_AutoTotals_RoundsLineTotalsBeforeSumming()
    {
        var (config, rows) = TestHelper.CreateRoundingTestCase();

        var desc = InvoiceMapper.Map(rows, config);

        desc.TradeLineItems.Select(item => item.LineTotalAmount)
            .ShouldBe([88.47m, 14.41m, 88.47m, 19.55m, 67.06m, 67.06m, 2.00m, 47.81m]);
        desc.LineTotalAmount!.Value.ShouldBe(394.83m);
        desc.TaxBasisAmount!.Value.ShouldBe(394.83m);
        var taxes = desc.GetApplicableTradeTaxes();
        taxes.Single(tax => tax.Percent == 19m).BasisAmount.ShouldBe(217.89m);
        taxes.Single(tax => tax.Percent == 19m).TaxAmount.ShouldBe(41.40m);
        taxes.Single(tax => tax.Percent == 7m).BasisAmount.ShouldBe(176.94m);
        taxes.Single(tax => tax.Percent == 7m).TaxAmount.ShouldBe(12.39m);
        desc.TaxTotalAmount!.Value.ShouldBe(53.79m);
        desc.GrandTotalAmount!.Value.ShouldBe(448.62m);
    }

    [Test]
    public void Map_ManualTotals_TaxBasisUsesRoundedLineTotals()
    {
        var (config, rows) = TestHelper.CreateRoundingTestCase("manual");

        var desc = InvoiceMapper.Map(rows, config);

        desc.LineTotalAmount!.Value.ShouldBe(394.83m);
        var taxes = desc.GetApplicableTradeTaxes();
        taxes.Single(tax => tax.Percent == 19m).BasisAmount.ShouldBe(217.89m);
        taxes.Single(tax => tax.Percent == 7m).BasisAmount.ShouldBe(176.94m);
        taxes.Sum(tax => tax.TaxAmount).ShouldBe(53.79m);
    }

    [Test]
    public void Map_AutoTotals_NegativeQuantities_RoundsConsistently()
    {
        var config = new AppConfig
        {
            Csv = new CsvConfig { DecimalSeparator = "." },
            Mapping = new MappingConfig
            {
                Invoice = new InvoiceMapping
                {
                    InvoiceNumber = new FieldMapping { Value = "K-2026-001" },
                    InvoiceDate = new FieldMapping { Value = "2026-01-15" },
                    Currency = new FieldMapping { Value = "EUR" },
                },
                LineItems = new LineItemsMapping
                {
                    Mode = "rows",
                    Fields = new LineItemFieldsMapping
                    {
                        Name = new FieldMapping { Column = "Name" },
                        NetUnitPrice = new FieldMapping { Column = "Price" },
                        BilledQuantity = new FieldMapping { Column = "Quantity" },
                        TaxPercent = new FieldMapping { Value = "19" },
                    },
                },
            }
        };
        var rows = new List<Dictionary<string, string>>
        {
            new(StringComparer.OrdinalIgnoreCase) { ["Name"] = "Rücknahme A", ["Price"] = "51.45", ["Quantity"] = "-0.28" },
            new(StringComparer.OrdinalIgnoreCase) { ["Name"] = "Rücknahme B", ["Price"] = "1.115", ["Quantity"] = "-3" },
        };

        var desc = InvoiceMapper.Map(rows, config);

        desc.TradeLineItems.Select(item => item.LineTotalAmount).ShouldBe([-14.41m, -3.35m]);
        desc.LineTotalAmount!.Value.ShouldBe(-17.76m);
        desc.GetApplicableTradeTaxes().Single().BasisAmount.ShouldBe(-17.76m);
        desc.GetApplicableTradeTaxes().Single().TaxAmount.ShouldBe(-3.37m);
        desc.GrandTotalAmount!.Value.ShouldBe(-21.13m);
    }

    // ── 4.3.6 Zahlungsbedingungen ─────────────────────────────────────────

    [Test]
    public void Map_PaymentTerms_Description()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.PaymentTerms.Count.ShouldBeGreaterThan(0);
        desc.PaymentTerms[0].Description.ShouldContain("Zahlbar innerhalb 30 Tagen");
    }

    [Test]
    public void Map_PaymentTerms_DueDate()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.PaymentTerms[0].DueDate!.Value.ShouldBe(new DateTime(2018, 4, 4));
    }

    // ── 4.3.6a Zahlungsmittel / Bankverbindung ────────────────────────────

    [Test]
    public void Map_PaymentMeans_CreditTransfer_TypeCodeAndInformation()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.PaymentMeans!.TypeCode.ShouldBe(PaymentMeansTypeCodes.SEPACreditTransfer);
        desc.PaymentMeans.Information.ShouldBe("SEPA-Überweisung");
        desc.PaymentMeans.SEPACreditorIdentifier.ShouldBeNullOrEmpty();
    }

    [Test]
    public void Map_PaymentMeans_CreditTransfer_SellerAccountNormalized()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.CreditorBankAccounts.Count.ShouldBe(1);
        desc.CreditorBankAccounts[0].IBAN.ShouldBe("DE02120300000000202051");
        desc.CreditorBankAccounts[0].BIC.ShouldBe("BYLADEM1001");
        desc.CreditorBankAccounts[0].Name.ShouldBe("Lieferant GmbH");
        desc.DebitorBankAccounts.Count.ShouldBe(0);
    }

    [Test]
    public void Map_PaymentMeans_PaymentReferenceFromColumn()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.PaymentReference.ShouldBe("471102");
    }

    [Test]
    public void Map_PaymentMeans_DirectDebit_ViaRule()
    {
        var desc = TestHelper.MapFromTestData("config_directdebit.yml", "test_directdebit.csv");

        desc.PaymentMeans!.TypeCode.ShouldBe(PaymentMeansTypeCodes.SEPADirectDebit);
        desc.PaymentMeans.SEPACreditorIdentifier.ShouldBe("DE98ZZZ09999999999");
        desc.PaymentMeans.SEPAMandateReference.ShouldBe("MANDAT-2013-001");
    }

    [Test]
    public void Map_PaymentMeans_DirectDebit_BuyerAccount()
    {
        var desc = TestHelper.MapFromTestData("config_directdebit.yml", "test_directdebit.csv");

        desc.DebitorBankAccounts.Count.ShouldBe(1);
        desc.DebitorBankAccounts[0].IBAN.ShouldBe("DE89370400440532013000");
        desc.DebitorBankAccounts[0].BIC.ShouldBe("COBADEFFXXX");
        InvoiceMapper.CheckPaymentMeans(desc, "XRechnung").ShouldBeEmpty();
    }

    [Test]
    public void Map_PaymentMeans_RuleNotMatching_FallsBackToCreditTransfer()
    {
        var config = TestHelper.LoadTestConfig("config_directdebit.yml");
        var rows = TestHelper.ReadTestCsv("test_directdebit.csv", config.Csv);
        rows[0]["Zahlart"] = "Überweisung";

        var desc = InvoiceMapper.Map(rows, config);

        desc.PaymentMeans!.TypeCode.ShouldBe(PaymentMeansTypeCodes.SEPACreditTransfer);
        desc.PaymentMeans.SEPAMandateReference.ShouldBeNullOrEmpty();
        desc.DebitorBankAccounts.Count.ShouldBe(0);
        desc.CreditorBankAccounts.Count.ShouldBe(1);
    }

    [Test]
    public void Map_PaymentMeans_MultipleSellerAccounts()
    {
        var desc = MapWithPaymentMeans(new PaymentMeansMapping
        {
            TypeCode = new FieldMapping { Value = "SEPACreditTransfer" },
            SellerAccounts =
            [
                new FinancialAccountMapping { Iban = new FieldMapping { Value = "DE02120300000000202051" } },
                new FinancialAccountMapping { Iban = new FieldMapping { Value = "DE89370400440532013000" } },
                new FinancialAccountMapping { Iban = new FieldMapping { Column = "Leer" } },
            ],
        });

        desc.CreditorBankAccounts.Select(a => a.IBAN).ShouldBe(["DE02120300000000202051", "DE89370400440532013000"]);
    }

    [Test]
    public void Map_PaymentMeans_UnknownTypeCode_Throws()
    {
        Should.Throw<InvalidOperationException>(() => MapWithPaymentMeans(new PaymentMeansMapping
        {
            TypeCode = new FieldMapping { Value = "Scheck" },
        }));
    }

    [Test]
    public void Map_PaymentMeans_NotConfigured_NoPaymentMeans()
    {
        var desc = MapWithPaymentMeans(null);

        desc.PaymentMeans.ShouldBeNull();
        desc.CreditorBankAccounts.Count.ShouldBe(0);
    }

    [Test]
    public void CheckPaymentMeans_CreditTransferWithoutIban_Warns()
    {
        var desc = MapWithPaymentMeans(new PaymentMeansMapping { TypeCode = new FieldMapping { Value = "58" } });

        InvoiceMapper.CheckPaymentMeans(desc, "Extended").ShouldContain(w => w.Contains("BR-61"));
    }

    [Test]
    public void CheckPaymentMeans_DirectDebitIncomplete_Warns()
    {
        var desc = MapWithPaymentMeans(new PaymentMeansMapping { TypeCode = new FieldMapping { Value = "59" } });

        var warnings = InvoiceMapper.CheckPaymentMeans(desc, "Extended");

        warnings.ShouldContain(w => w.Contains("BT-89"));
        warnings.ShouldContain(w => w.Contains("BT-90"));
        warnings.ShouldContain(w => w.Contains("BT-91"));
    }

    [Test]
    public void CheckPaymentMeans_XRechnungWithoutPaymentMeans_Warns()
    {
        var desc = MapWithPaymentMeans(null);

        InvoiceMapper.CheckPaymentMeans(desc, "XRechnung").ShouldContain(w => w.Contains("BR-DE-1"));
        InvoiceMapper.CheckPaymentMeans(desc, "Extended").ShouldBeEmpty();
    }

    [Test]
    public void CheckPaymentMeans_InvalidIbanChecksum_Warns()
    {
        var desc = MapWithPaymentMeans(new PaymentMeansMapping
        {
            TypeCode = new FieldMapping { Value = "58" },
            SellerAccounts = [new FinancialAccountMapping { Iban = new FieldMapping { Value = "DE00120300000000202051" } }],
        });

        InvoiceMapper.CheckPaymentMeans(desc, "Extended").ShouldContain(w => w.Contains("Prüfsumme"));
    }

    private static InvoiceDescriptor MapWithPaymentMeans(PaymentMeansMapping? paymentMeans)
    {
        var config = new AppConfig
        {
            Csv = new CsvConfig(),
            Mapping = new MappingConfig
            {
                Invoice = new InvoiceMapping
                {
                    InvoiceNumber = new FieldMapping { Value = "TEST" },
                    InvoiceDate = new FieldMapping { Value = "01.01.2023" },
                    Currency = new FieldMapping { Value = "EUR" },
                },
                PaymentMeans = paymentMeans,
            }
        };
        var rows = new List<Dictionary<string, string>> { new(StringComparer.OrdinalIgnoreCase) };
        return InvoiceMapper.Map(rows, config);
    }

    // ── 4.3.7 Edge Cases ──────────────────────────────────────────────────

    [Test]
    public void Map_EmptyNote_NoNoteAdded()
    {
        var config = new AppConfig
        {
            Csv = new CsvConfig(),
            Mapping = new MappingConfig
            {
                Invoice = new InvoiceMapping
                {
                    InvoiceNumber = new FieldMapping { Value = "TEST" },
                    InvoiceDate = new FieldMapping { Value = "01.01.2023" },
                    Currency = new FieldMapping { Value = "EUR" },
                    Note = new FieldMapping { Column = "Bemerkung" },
                }
            }
        };
        var rows = new List<Dictionary<string, string>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Bemerkung"] = ""
            }
        };

        var desc = InvoiceMapper.Map(rows, config);

        desc.Notes.Count.ShouldBe(0);
    }

    [Test]
    public void Map_NullMapping_NoException()
    {
        var config = new AppConfig
        {
            Csv = new CsvConfig(),
            Mapping = new MappingConfig
            {
                Invoice = new InvoiceMapping
                {
                    InvoiceNumber = new FieldMapping { Value = "TEST" },
                    InvoiceDate = new FieldMapping { Value = "01.01.2023" },
                    Currency = new FieldMapping { Value = "EUR" },
                }
                // Buyer = null, Seller = null
            }
        };
        var rows = new List<Dictionary<string, string>>
        {
            new(StringComparer.OrdinalIgnoreCase)
        };

        Should.NotThrow(() => InvoiceMapper.Map(rows, config));
    }

    [Test]
    public void Map_InvalidDecimal_ReturnsZero()
    {
        var config = new AppConfig
        {
            Csv = new CsvConfig(),
            Mapping = new MappingConfig
            {
                Invoice = new InvoiceMapping
                {
                    InvoiceNumber = new FieldMapping { Value = "TEST" },
                    InvoiceDate = new FieldMapping { Value = "01.01.2023" },
                    Currency = new FieldMapping { Value = "EUR" },
                },
                LineItems = new LineItemsMapping
                {
                    Mode = "rows",
                    Fields = new LineItemFieldsMapping
                    {
                        Name = new FieldMapping { Column = "Name" },
                        NetUnitPrice = new FieldMapping { Column = "Price" },
                        BilledQuantity = new FieldMapping { Value = "1" },
                        TaxPercent = new FieldMapping { Value = "19" },
                    }
                }
            }
        };
        var rows = new List<Dictionary<string, string>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = "TestItem",
                ["Price"] = "INVALID"
            }
        };

        var desc = InvoiceMapper.Map(rows, config);

        desc.TradeLineItems[0].NetUnitPrice.ShouldBe(0m);
    }

    [Test]
    public void Map_InvalidDate_ReturnsMinValue()
    {
        var config = new AppConfig
        {
            Csv = new CsvConfig(),
            Mapping = new MappingConfig
            {
                Invoice = new InvoiceMapping
                {
                    InvoiceNumber = new FieldMapping { Value = "TEST" },
                    InvoiceDate = new FieldMapping { Column = "Datum" },
                    Currency = new FieldMapping { Value = "EUR" },
                }
            }
        };
        var rows = new List<Dictionary<string, string>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Datum"] = "NOT_A_DATE"
            }
        };

        var desc = InvoiceMapper.Map(rows, config);

        desc.InvoiceDate.ShouldBe(DateTime.MinValue);
    }

    [Test]
    public void Map_UnknownCurrency_DefaultsToEUR()
    {
        var config = new AppConfig
        {
            Csv = new CsvConfig(),
            Mapping = new MappingConfig
            {
                Invoice = new InvoiceMapping
                {
                    InvoiceNumber = new FieldMapping { Value = "TEST" },
                    InvoiceDate = new FieldMapping { Value = "01.01.2023" },
                    Currency = new FieldMapping { Value = "UNKNOWN" },
                }
            }
        };
        var rows = new List<Dictionary<string, string>>
        {
            new(StringComparer.OrdinalIgnoreCase)
        };

        var desc = InvoiceMapper.Map(rows, config);

        desc.Currency.ShouldBe(CurrencyCodes.EUR);
    }

    [Test]
    public void Map_UnknownCountry_DefaultsToDE()
    {
        var config = new AppConfig
        {
            Csv = new CsvConfig(),
            Mapping = new MappingConfig
            {
                Invoice = new InvoiceMapping
                {
                    InvoiceNumber = new FieldMapping { Value = "TEST" },
                    InvoiceDate = new FieldMapping { Value = "01.01.2023" },
                    Currency = new FieldMapping { Value = "EUR" },
                },
                Buyer = new BuyerMapping
                {
                    Name = new FieldMapping { Column = "Name" },
                    Country = new FieldMapping { Column = "Land" },
                }
            }
        };
        var rows = new List<Dictionary<string, string>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = "Testkäufer",
                ["Land"] = "INVALID"
            }
        };

        var desc = InvoiceMapper.Map(rows, config);

        desc.Buyer.Country.ShouldBe(CountryCodes.DE);
    }
}
