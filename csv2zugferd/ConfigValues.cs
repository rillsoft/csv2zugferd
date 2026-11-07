using System.Globalization;
using System.Reflection;
using s2industries.ZUGFeRD;

namespace Csv2Zugferd;

/// <summary>
/// Ein fachlicher Wertevorrat der Konfiguration: kanonische Werte, akzeptierte Schreibweisen und der Rückfall
/// bei unbekanntem Wert. Mapper, Exporter, MCP-Prüfung und Schema lesen dieselbe Quelle.
/// </summary>
public sealed class ValueSet
{
    public required string Name { get; init; }

    /// <summary>Kanonische Werte in empfohlener Schreibweise (Vertrags-<c>enum</c>).</summary>
    public required IReadOnlyList<string> Canonical { get; init; }

    /// <summary>Englische Beschreibung der weiteren akzeptierten Schreibweisen und der Parse-Semantik.</summary>
    public required string Description { get; init; }

    /// <summary>Groß-/Kleinschreibung der kanonischen Werte unerheblich?</summary>
    public required bool CaseInsensitive { get; init; }

    /// <summary>Weitere akzeptierte Schreibweisen außer den kanonischen Werten (z. B. Enum-Namen zu Codes), für das Schema.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>Akzeptiert der Parser zusätzlich ganzzahlige Werte (Enum-Ordinal oder Code)?</summary>
    public bool AcceptsInteger { get; init; }

    /// <summary>Rückfall bei unbekanntem Wert, oder <c>null</c>, wenn ein unbekannter Wert ein Fehler ist.</summary>
    public required string? InvalidFallback { get; init; }

    /// <summary>Prüft, ob der Parser den Wert kennt (ohne Rückfall).</summary>
    public required Func<string, bool> IsKnown { get; init; }
}

/// <summary>
/// Zulässige Werte und Parse-Regeln der YAML-Konfiguration an genau einer Stelle. Die Parse-Methoden
/// übernehmen das bisherige Verhalten des Mappers und Exporters unverändert (Rückfälle, Schreibvarianten).
/// </summary>
public static class ConfigValues
{
    public const string LineItemsModeRows = "rows";
    public const string LineItemsModeColumns = "columns";
    public const string ColumnStylePrefix = "prefix";
    public const string ColumnStyleSuffix = "suffix";
    public const string TotalsModeAuto = "auto";
    public const string TotalsModeManual = "manual";

    // --- Modi ---

    public static readonly ValueSet LineItemsMode = new()
    {
        Name = "lineItemsMode",
        Canonical = [LineItemsModeRows, LineItemsModeColumns],
        Description = "Case-insensitive. Any value other than columns (including empty or unknown) selects rows.",
        CaseInsensitive = true,
        InvalidFallback = LineItemsModeRows,
        IsKnown = value => EqualsAny(value, LineItemsModeRows, LineItemsModeColumns),
    };

    public static readonly ValueSet ColumnStyle = new()
    {
        Name = "columnStyle",
        Canonical = [ColumnStylePrefix, ColumnStyleSuffix],
        Description = "Case-insensitive. Any value other than prefix (including empty or unknown) selects suffix; the initial value when the key is absent is prefix.",
        CaseInsensitive = true,
        InvalidFallback = ColumnStyleSuffix,
        IsKnown = value => EqualsAny(value, ColumnStylePrefix, ColumnStyleSuffix),
    };

    public static readonly ValueSet TotalsMode = new()
    {
        Name = "totalsMode",
        Canonical = [TotalsModeAuto, TotalsModeManual],
        Description = "Case-insensitive. Any value other than auto (including empty or unknown) selects manual; the initial value when the key is absent is auto.",
        CaseInsensitive = true,
        InvalidFallback = TotalsModeManual,
        IsKnown = value => EqualsAny(value, TotalsModeAuto, TotalsModeManual),
    };

    public static string ParseLineItemsMode(string? value) =>
        string.Equals(value, LineItemsModeColumns, StringComparison.OrdinalIgnoreCase) ? LineItemsModeColumns : LineItemsModeRows;

    public static string ParseColumnStyle(string? value) =>
        string.Equals(value, ColumnStylePrefix, StringComparison.OrdinalIgnoreCase) ? ColumnStylePrefix : ColumnStyleSuffix;

    public static string ParseTotalsMode(string? value) =>
        string.Equals(value, TotalsModeAuto, StringComparison.OrdinalIgnoreCase) ? TotalsModeAuto : TotalsModeManual;

    // --- zugferd ---

    private static readonly Dictionary<string, ZUGFeRDVersion> s_versions = new(StringComparer.Ordinal)
    {
        ["1"] = ZUGFeRDVersion.Version1,
        ["1.0"] = ZUGFeRDVersion.Version1,
        ["2.0"] = ZUGFeRDVersion.Version20,
        ["20"] = ZUGFeRDVersion.Version20,
        ["2.3"] = ZUGFeRDVersion.Version23,
        ["23"] = ZUGFeRDVersion.Version23,
    };

    public static readonly ValueSet Version = new()
    {
        Name = "version",
        Canonical = ["2.3", "2.0", "1.0"],
        Aliases = ["23", "20", "1"],
        Description = "Exact spellings 2.3, 23, 2.0, 20, 1.0 and 1 are accepted. Unknown values fall back to 2.3.",
        CaseInsensitive = false,
        InvalidFallback = "2.3",
        IsKnown = value => s_versions.ContainsKey(value),
    };

    private static readonly Dictionary<string, Profile> s_profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["minimum"] = Profile.Minimum,
        ["basicwl"] = Profile.BasicWL,
        ["basic"] = Profile.Basic,
        ["comfort"] = Profile.Comfort,
        ["en16931"] = Profile.Comfort,
        ["extended"] = Profile.Extended,
        ["xrechnung"] = Profile.XRechnung,
    };

    public static readonly ValueSet ProfileSet = new()
    {
        Name = "profile",
        Canonical = ["Minimum", "BasicWL", "Basic", "Comfort", "EN16931", "Extended", "XRechnung"],
        Description = "Case-insensitive. Comfort and EN16931 are the same profile. Unknown values fall back to Extended.",
        CaseInsensitive = true,
        InvalidFallback = "Extended",
        IsKnown = value => s_profiles.ContainsKey(value),
    };

    public static readonly ValueSet Format = new()
    {
        Name = "format",
        Canonical = ["CII", "UBL"],
        Description = "Case-insensitive. Unknown values fall back to CII. Only used by the PDF export.",
        CaseInsensitive = true,
        InvalidFallback = "CII",
        IsKnown = value => EqualsAny(value, "CII", "UBL"),
    };

    public static bool TryParseVersion(string value, out ZUGFeRDVersion version)
    {
        if (s_versions.TryGetValue(value, out version))
            return true;
        version = ZUGFeRDVersion.Version23;
        return false;
    }

    public static bool TryParseProfile(string value, out Profile profile)
    {
        if (s_profiles.TryGetValue(value, out profile))
            return true;
        profile = Profile.Extended;
        return false;
    }

    public static bool TryParseFormat(string value, out ZUGFeRDFormats format)
    {
        switch (value.ToUpperInvariant())
        {
            case "CII":
                format = ZUGFeRDFormats.CII;
                return true;
            case "UBL":
                format = ZUGFeRDFormats.UBL;
                return true;
            default:
                format = ZUGFeRDFormats.CII;
                return false;
        }
    }

    // --- Fachliche Codes aus der ZUGFeRD-Bibliothek ---

    public static readonly ValueSet Currency = EnumSet<CurrencyCodes>("currency",
        "ISO 4217 currency code. Names are case-insensitive. Unknown values fall back to EUR.", "EUR");

    public static readonly ValueSet Country = EnumSet<CountryCodes>("country",
        "ISO 3166-1 alpha-2 country code. Names are case-insensitive. Unknown values fall back to DE.", "DE");

    public static readonly ValueSet UnitCode = EnumSet<QuantityCodes>("unitCode",
        "UN/ECE Recommendation 20 unit code as named in the ZUGFeRD library (C62 piece, DAY, HUR, KGM, MTR, ...). Names are case-insensitive; codes starting with a digit carry a leading underscore (_3B). Unknown values fall back to C62.", "C62");

    public static readonly ValueSet TaxType = EnumSet<TaxTypes>("taxType",
        "UNTDID 5153 tax type code, normally VAT. Names are case-insensitive. Unknown values fall back to VAT.", "VAT");

    public static readonly ValueSet TaxCategory = EnumSet<TaxCategoryCodes>("taxCategoryCode",
        "UNTDID 5305 VAT category code: S standard, Z zero rated, E exempt, AE reverse charge, K intra-community supply, G export, O not subject to VAT, L Canary Islands, M Ceuta and Melilla. Names are case-insensitive. Unknown values fall back to S.", "S");

    public static readonly ValueSet TaxScheme = new()
    {
        Name = "taxScheme",
        Canonical = ["VA", "FC"],
        Description = "VA = VAT identifier, FC = tax number. Case-insensitive. Unknown or missing values fall back to VA.",
        CaseInsensitive = true,
        InvalidFallback = "VA",
        IsKnown = value => EqualsAny(value, "VA", "FC"),
    };

    public static readonly ValueSet ElectronicAddressScheme = new()
    {
        Name = "electronicAddressScheme",
        Canonical = ["GermanyVatNumber", "LuxemburgVatNumber", "ElectronicMailSmtp", "EM"],
        Description = "Case-sensitive. GermanyVatNumber = EAS 9930, LuxemburgVatNumber = EAS 9938, ElectronicMailSmtp or EM = e-mail address (EAS EM). Unknown or missing values fall back to GermanyVatNumber.",
        CaseInsensitive = false,
        InvalidFallback = "GermanyVatNumber",
        IsKnown = value => value is "GermanyVatNumber" or "LuxemburgVatNumber" or "ElectronicMailSmtp" or "EM",
    };

    public static readonly ValueSet BooleanText = new()
    {
        Name = "booleanText",
        Canonical = ["true", "false"],
        Description = "Case-insensitive true selects the first meaning; any other text selects the second. An empty value selects the default stated at the field.",
        CaseInsensitive = true,
        InvalidFallback = "false",
        IsKnown = value => EqualsAny(value, "true", "false"),
    };

    // Rechnungstypen: UNTDID-1001-Codes hängen als EnumStringValueAttribute an den Enum-Feldern.
    private static readonly (Dictionary<string, InvoiceType> ByCode, Dictionary<InvoiceType, string> ByType) s_invoiceTypes = BuildInvoiceTypeTable();

    public static readonly ValueSet InvoiceTypeSet = new()
    {
        Name = "invoiceType",
        Canonical = s_invoiceTypes.ByType.Values.Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList(),
        Aliases = Enum.GetNames<InvoiceType>(),
        Description = "UNTDID 1001 code (380 invoice, 381 credit note, 384 correction, 389 self-billed invoice, 326 partial invoice, ...) or the library name (Invoice, CreditNote, Correction, ...; case-insensitive). Leading and trailing whitespace is ignored. Unknown values are an error (invoice_type_unknown), there is no fallback.",
        CaseInsensitive = false,
        InvalidFallback = null,
        IsKnown = value => TryParseInvoiceType(value, out _),
    };

    public static readonly ValueSet PaymentMeansType = new()
    {
        Name = "paymentMeansType",
        Canonical = Enum.GetValues<PaymentMeansTypeCodes>().Select(v => ((int)v).ToString(CultureInfo.InvariantCulture)).Distinct().ToList(),
        Aliases = Enum.GetNames<PaymentMeansTypeCodes>(),
        Description = "UNTDID 4461 code as number (58 SEPA credit transfer, 59 SEPA direct debit, 30 credit transfer, 49 direct debit, 10 cash, 48 bank card, ...) or the library name (SEPACreditTransfer, SEPADirectDebit, ...; case-insensitive). Leading and trailing whitespace is ignored. Unknown values are an error (payment_means_type_unknown), there is no fallback.",
        CaseInsensitive = false,
        InvalidFallback = null,
        IsKnown = value => TryParsePaymentMeansTypeCode(value, out _),
    };

    public static readonly IReadOnlyList<ValueSet> All =
    [
        LineItemsMode, ColumnStyle, TotalsMode, Version, ProfileSet, Format, Currency, Country, UnitCode, TaxType,
        TaxCategory, TaxScheme, ElectronicAddressScheme, BooleanText, InvoiceTypeSet, PaymentMeansType,
    ];

    public static ValueSet? FindSet(string name) =>
        All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));

    // --- Parse-Methoden (Verhalten des bisherigen Mappers) ---

    public static bool TryParseCurrency(string value, out CurrencyCodes code) =>
        TryParseEnum(value, CurrencyCodes.EUR, out code);

    public static bool TryParseCountry(string value, out CountryCodes code) =>
        TryParseEnum(value, CountryCodes.DE, out code);

    public static bool TryParseUnitCode(string value, out QuantityCodes code) =>
        TryParseEnum(value, QuantityCodes.C62, out code);

    public static bool TryParseTaxType(string value, out TaxTypes code) =>
        TryParseEnum(value, TaxTypes.VAT, out code);

    public static bool TryParseTaxCategory(string value, out TaxCategoryCodes code) =>
        TryParseEnum(value, TaxCategoryCodes.S, out code);

    public static bool TryParseTaxScheme(string? value, out TaxRegistrationSchemeID scheme)
    {
        switch (value?.ToUpperInvariant())
        {
            case "FC":
                scheme = TaxRegistrationSchemeID.FC;
                return true;
            case "VA":
                scheme = TaxRegistrationSchemeID.VA;
                return true;
            default:
                scheme = TaxRegistrationSchemeID.VA;
                return false;
        }
    }

    public static bool TryParseElectronicAddressScheme(string? value, out ElectronicAddressSchemeIdentifiers scheme)
    {
        switch (value)
        {
            case "GermanyVatNumber":
                scheme = ElectronicAddressSchemeIdentifiers.GermanyVatNumber;
                return true;
            case "LuxemburgVatNumber":
                scheme = ElectronicAddressSchemeIdentifiers.LuxemburgVatNumber;
                return true;
            case "EM":
            case "ElectronicMailSmtp":
                scheme = ElectronicAddressSchemeIdentifiers.ElectronicMailSmtp;
                return true;
            default:
                scheme = ElectronicAddressSchemeIdentifiers.GermanyVatNumber;
                return false;
        }
    }

    public static bool TryParseInvoiceType(string value, out InvoiceType type)
    {
        var trimmed = value.Trim();
        if (s_invoiceTypes.ByCode.TryGetValue(trimmed, out type))
            return true;
        if (!int.TryParse(trimmed, out _) && Enum.TryParse(trimmed, true, out type))
            return true;
        type = InvoiceType.Invoice;
        return false;
    }

    public static string GetInvoiceTypeCode(InvoiceType type) =>
        s_invoiceTypes.ByType.GetValueOrDefault(type) ?? ((int)type).ToString(CultureInfo.InvariantCulture);

    public static bool TryParsePaymentMeansTypeCode(string value, out PaymentMeansTypeCodes code)
    {
        var trimmed = value.Trim();
        if (int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && Enum.IsDefined(typeof(PaymentMeansTypeCodes), number))
        {
            code = (PaymentMeansTypeCodes)number;
            return true;
        }
        if (!int.TryParse(trimmed, out _) && Enum.TryParse(trimmed, true, out code))
            return true;
        code = default;
        return false;
    }

    // --- Helfer ---

    private static bool EqualsAny(string value, params string[] candidates) =>
        candidates.Any(c => string.Equals(value, c, StringComparison.OrdinalIgnoreCase));

    /// <summary>Enum.TryParse mit ignoreCase, wie bisher im Mapper: Namen case-insensitiv, ganzzahlige Werte werden ebenfalls akzeptiert.</summary>
    private static bool TryParseEnum<TEnum>(string value, TEnum fallback, out TEnum result) where TEnum : struct, Enum
    {
        if (Enum.TryParse(value, true, out result))
            return true;
        result = fallback;
        return false;
    }

    private static ValueSet EnumSet<TEnum>(string name, string description, string fallback) where TEnum : struct, Enum => new()
    {
        Name = name,
        Canonical = Enum.GetNames<TEnum>(),
        Description = description,
        CaseInsensitive = true,
        AcceptsInteger = true,
        InvalidFallback = fallback,
        IsKnown = value => Enum.TryParse<TEnum>(value, true, out var parsed) && (!int.TryParse(value, out _) || Enum.IsDefined(parsed)),
    };

    private static (Dictionary<string, InvoiceType> ByCode, Dictionary<InvoiceType, string> ByType) BuildInvoiceTypeTable()
    {
        var byCode = new Dictionary<string, InvoiceType>();
        var byType = new Dictionary<InvoiceType, string>();

        foreach (var field in typeof(InvoiceType).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var attribute = field.GetCustomAttribute<EnumStringValueAttribute>();
            if (attribute == null) continue;

            var type = (InvoiceType)field.GetValue(null)!;
            byCode.TryAdd(attribute.Value, type);
            byType.TryAdd(type, attribute.Value);
            foreach (var legacyValue in attribute.LegacyValues ?? [])
                byCode.TryAdd(legacyValue, type);
        }

        return (byCode, byType);
    }
}
