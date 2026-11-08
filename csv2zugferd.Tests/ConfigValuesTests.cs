using Csv2Zugferd.Models;
using NUnit.Framework;
using s2industries.ZUGFeRD;
using Shouldly;

namespace Csv2Zugferd.Tests;

[TestFixture]
public class ConfigValuesTests
{
    [TestCase("Minimum", Profile.Minimum)]
    [TestCase("minimum", Profile.Minimum)]
    [TestCase("BasicWL", Profile.BasicWL)]
    [TestCase("Basic", Profile.Basic)]
    [TestCase("Comfort", Profile.Comfort)]
    [TestCase("EN16931", Profile.Comfort)]
    [TestCase("en16931", Profile.Comfort)]
    [TestCase("Extended", Profile.Extended)]
    [TestCase("XRECHNUNG", Profile.XRechnung)]
    public void ConfigValues_ProfileTable_MatchesExporterParsing(string value, Profile expected)
    {
        ConfigValues.TryParseProfile(value, out var profile).ShouldBeTrue();
        profile.ShouldBe(expected);
        ConfigValues.ProfileSet.IsKnown(value).ShouldBeTrue();

        var diagnostics = new List<MappingDiagnostic>();
        InvoiceExporter.ResolveProfile(new ZugferdConfig { Profile = value }, diagnostics).ShouldBe(expected);
        diagnostics.ShouldBeEmpty();
    }

    [Test]
    public void ParseProfile_UnknownValue_FallsBackToExtendedAndWarns()
    {
        var diagnostics = new List<MappingDiagnostic>();

        var profile = InvoiceExporter.ResolveProfile(new ZugferdConfig { Profile = "Komfort" }, diagnostics);

        profile.ShouldBe(Profile.Extended);
        var diagnostic = diagnostics.ShouldHaveSingleItem();
        diagnostic.Code.ShouldBe(DiagnosticCodes.ConfigValueFallback);
        diagnostic.Path.ShouldBe("zugferd.profile");
        diagnostic.Message.ShouldContain("Komfort");
        diagnostic.Message.ShouldContain("Extended");
    }

    [TestCase("", ZUGFeRDVersion.Version23, false)]
    [TestCase("2.3", ZUGFeRDVersion.Version23, true)]
    [TestCase("23", ZUGFeRDVersion.Version23, true)]
    [TestCase("2.0", ZUGFeRDVersion.Version20, true)]
    [TestCase("20", ZUGFeRDVersion.Version20, true)]
    [TestCase("1", ZUGFeRDVersion.Version1, true)]
    [TestCase("1.0", ZUGFeRDVersion.Version1, true)]
    [TestCase("3.0", ZUGFeRDVersion.Version23, false)]
    public void ConfigValues_VersionSpellings_PreserveExporterParsing(string value, ZUGFeRDVersion expected, bool known)
    {
        ConfigValues.TryParseVersion(value, out var version).ShouldBe(known);
        version.ShouldBe(expected);
        ConfigValues.Version.IsKnown(value).ShouldBe(known);
    }

    [TestCase("CII", ZUGFeRDFormats.CII, true)]
    [TestCase("cii", ZUGFeRDFormats.CII, true)]
    [TestCase("UBL", ZUGFeRDFormats.UBL, true)]
    [TestCase("ubl", ZUGFeRDFormats.UBL, true)]
    [TestCase("XML", ZUGFeRDFormats.CII, false)]
    public void ConfigValues_FormatSpellings_PreserveExporterParsing(string value, ZUGFeRDFormats expected, bool known)
    {
        ConfigValues.TryParseFormat(value, out var format).ShouldBe(known);
        format.ShouldBe(expected);
    }

    [Test]
    public void ConfigValues_AcceptedSpellings_PreserveExistingParsing()
    {
        // Enum-basierte Werte: Namen case-insensitiv, Rückfall bei Unbekanntem
        ConfigValues.TryParseCurrency("eur", out var currency).ShouldBeTrue();
        currency.ShouldBe(CurrencyCodes.EUR);
        ConfigValues.TryParseCurrency("Euro", out currency).ShouldBeFalse();
        currency.ShouldBe(CurrencyCodes.EUR);
        ConfigValues.TryParseCountry("at", out var country).ShouldBeTrue();
        country.ShouldBe(CountryCodes.AT);
        ConfigValues.TryParseUnitCode("day", out var unit).ShouldBeTrue();
        unit.ShouldBe(QuantityCodes.DAY);
        ConfigValues.TryParseUnitCode("Stück", out unit).ShouldBeFalse();
        unit.ShouldBe(QuantityCodes.C62);
        ConfigValues.TryParseTaxType("vat", out var taxType).ShouldBeTrue();
        taxType.ShouldBe(TaxTypes.VAT);
        ConfigValues.TryParseTaxCategory("ae", out var category).ShouldBeTrue();
        category.ShouldBe(TaxCategoryCodes.AE);

        // Steuernummernschema: case-insensitiv, Rückfall VA
        ConfigValues.TryParseTaxScheme("fc", out var scheme).ShouldBeTrue();
        scheme.ShouldBe(TaxRegistrationSchemeID.FC);
        ConfigValues.TryParseTaxScheme("XX", out scheme).ShouldBeFalse();
        scheme.ShouldBe(TaxRegistrationSchemeID.VA);

        // Elektronische Adresse: case-sensitiv
        ConfigValues.TryParseElectronicAddressScheme("EM", out var eas).ShouldBeTrue();
        eas.ShouldBe(ElectronicAddressSchemeIdentifiers.ElectronicMailSmtp);
        ConfigValues.TryParseElectronicAddressScheme("em", out eas).ShouldBeFalse();
        eas.ShouldBe(ElectronicAddressSchemeIdentifiers.GermanyVatNumber);

        // Rechnungstyp: Code, Name (case-insensitiv), Leerraum
        ConfigValues.TryParseInvoiceType(" 381 ", out var type).ShouldBeTrue();
        type.ShouldBe(InvoiceType.CreditNote);
        ConfigValues.TryParseInvoiceType("creditnote", out type).ShouldBeTrue();
        type.ShouldBe(InvoiceType.CreditNote);
        ConfigValues.TryParseInvoiceType("999", out _).ShouldBeFalse();
        ConfigValues.GetInvoiceTypeCode(InvoiceType.Correction).ShouldBe("384");

        // Zahlungsart: nur definierte Zahlen, Namen case-insensitiv
        ConfigValues.TryParsePaymentMeansTypeCode("58", out var pm).ShouldBeTrue();
        pm.ShouldBe(PaymentMeansTypeCodes.SEPACreditTransfer);
        ConfigValues.TryParsePaymentMeansTypeCode("sepadirectdebit", out pm).ShouldBeTrue();
        pm.ShouldBe(PaymentMeansTypeCodes.SEPADirectDebit);
        ConfigValues.TryParsePaymentMeansTypeCode("9999", out _).ShouldBeFalse();
        ConfigValues.TryParsePaymentMeansTypeCode("ZZZ", out _).ShouldBeFalse();

        // Modi
        ConfigValues.ParseLineItemsMode("COLUMNS").ShouldBe("columns");
        ConfigValues.ParseLineItemsMode("").ShouldBe("rows");
        ConfigValues.ParseColumnStyle("").ShouldBe("suffix");
        ConfigValues.ParseColumnStyle("Prefix").ShouldBe("prefix");
        ConfigValues.ParseTotalsMode("").ShouldBe("manual");
        ConfigValues.ParseTotalsMode("Auto").ShouldBe("auto");
    }

    [Test]
    public void ConfigValues_CanonicalValues_AreAllKnown()
    {
        foreach (var set in ConfigValues.All)
        {
            set.Canonical.ShouldNotBeEmpty(set.Name);
            foreach (var value in set.Canonical)
                set.IsKnown(value).ShouldBeTrue($"{set.Name}: {value}");
            foreach (var alias in set.Aliases)
                set.IsKnown(alias).ShouldBeTrue($"{set.Name} alias: {alias}");
            if (set.InvalidFallback != null)
                set.IsKnown(set.InvalidFallback).ShouldBeTrue($"{set.Name} fallback: {set.InvalidFallback}");
        }
    }

    [Test]
    public void ConfigValues_SetNames_AreUnique()
    {
        ConfigValues.All.Select(s => s.Name).ShouldBeUnique();
    }

    [Test]
    public void ConfigSemantics_FieldValueSets_MatchParserBehavior()
    {
        foreach (var semantic in ConfigSemantics.All.Where(s => s.ValueSet != null))
        {
            var set = ConfigValues.FindSet(semantic.ValueSet!);
            set.ShouldNotBeNull($"{semantic.Path}: Wertevorrat {semantic.ValueSet} fehlt");
            semantic.InvalidFallback.ShouldBe(set.InvalidFallback, semantic.Path);
            if (semantic.DefaultValue != null && semantic.ValueSet != "booleanText")
                set.IsKnown(semantic.DefaultValue).ShouldBeTrue($"{semantic.Path}: Default {semantic.DefaultValue} unbekannt");
        }
    }

    [Test]
    public void Map_UnknownTotalsMode_PreservesManualBranch()
    {
        var (config, rows) = TestHelper.CreateRoundingTestCase("irgendwas");
        var diagnostics = new List<MappingDiagnostic>();

        var desc = InvoiceMapper.Map(rows, config, diagnostics);

        desc.GrandTotalAmount.ShouldBe(448.62m); // manuelle Werte aus dem Testfall
        diagnostics.ShouldContain(d => d.Code == DiagnosticCodes.ConfigValueFallback && d.Path == "mapping.totals.mode");
    }

    [Test]
    public void Map_UnknownColumnStyle_PreservesSuffixBranch()
    {
        var (config, rows) = TestHelper.CreateRoundingTestCase();
        config.Mapping.LineItems!.ColumnPattern!.Style = "mittig";
        var diagnostics = new List<MappingDiagnostic>();

        var desc = InvoiceMapper.Map(rows, config, diagnostics);

        desc.TradeLineItems.Count.ShouldBe(8);
        diagnostics.ShouldContain(d => d.Code == DiagnosticCodes.ConfigValueFallback && d.Path == "mapping.lineItems.columnPattern.style");
    }
}
