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

    // ── 4.3.2 Käufer / Verkäufer ──────────────────────────────────────────

    [Test]
    public void Map_RowsMode_BuyerName()
    {
        var desc = TestHelper.MapFromTestData("config_rows.yml", "test_rows.csv");

        desc.Buyer.Name.ShouldBe("Kunden Mitte AG");
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
