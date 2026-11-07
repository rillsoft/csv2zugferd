namespace Csv2Zugferd;

/// <summary>Wirkungsstatus eines Konfigurationsfeldes.</summary>
public enum FieldStatus
{
    /// <summary>Wirkt in jeder Betriebsart.</summary>
    Active,

    /// <summary>Wirkt nur unter einer Bedingung (Modus, Zahlungsart, Profil); sonst ohne Wirkung mit Befund.</summary>
    Conditional,

    /// <summary>Wird akzeptiert, aber nie ausgewertet.</summary>
    Ignored,
}

/// <summary>
/// Wirkung, Auflösung und Wertebereich eines Konfigurationsfeldes. <paramref name="Sources"/> enthält nur die an
/// diesem Feld wirksamen Quellen in Reihenfolge (<c>value</c>, <c>column</c>, <c>rules</c>, <c>default</c>);
/// strukturelle Felder haben eine leere Liste. <paramref name="Modes"/> ist leer bei modusunabhängigen Feldern.
/// </summary>
public sealed record FieldSemantic(
    string Path,
    FieldStatus Status,
    IReadOnlyList<string> Modes,
    IReadOnlyList<string> Sources,
    string? ValueSet,
    string? DefaultValue,
    string? InvalidFallback,
    string Description);

/// <summary>
/// Wirkungsmetadaten je Feldpfad an genau einer Stelle. Listenpfade verwenden <c>[]</c>. Generische Teilschlüssel
/// eines Feld-Mappings stehen unter dem Präfix <c>fieldMapping.</c>. Reflection- und Verhaltenstests sichern,
/// dass jede öffentliche Modelleigenschaft einen Eintrag hat und die Wertebereiche dem Parser entsprechen.
/// </summary>
public static class ConfigSemantics
{
    public const string ModeRows = ConfigValues.LineItemsModeRows;
    public const string ModeColumns = ConfigValues.LineItemsModeColumns;
    public const string ModeManual = ConfigValues.TotalsModeManual;
    public const string ModeAuto = ConfigValues.TotalsModeAuto;

    private static readonly string[] s_none = [];
    private static readonly string[] s_std = ["value", "column", "rules", "default"];
    private static readonly string[] s_valueColumn = ["value", "column"];
    private static readonly string[] s_columnOnly = ["column"];

    private const string RowsOnly = "Evaluated in rows mode only; in columns mode the key is accepted but ignored (config_field_ignored).";
    private const string ColumnsOnly = "Evaluated in columns mode only; in rows mode the key is accepted but ignored (config_field_ignored).";
    private const string ColumnsResolution = "In columns mode column is a fragment: a direct column with this exact name is tried first, then the indexed column built from columnPattern; resolution order value, column, rules, default.";
    private const string ManualOnly = "Read in totals.mode manual only; in auto mode the key is accepted but ignored (config_field_ignored).";
    private const string DateNote = "Parsed with csv.dateFormat; a non-empty value that cannot be parsed yields DateTime.MinValue and invalid_date.";
    private const string DecimalNote = "Parsed with csv.decimalSeparator; a non-empty value that cannot be parsed yields 0 and invalid_decimal.";

    public static readonly IReadOnlyList<FieldSemantic> All =
    [
        // --- Wurzel ---
        Structural("csv", "CSV reading settings; all keys optional with the stated defaults."),
        Structural("zugferd", "Output settings; all keys optional with the stated defaults."),
        Structural("mapping", "Field mapping; every section is optional, missing sections write nothing."),

        // --- csv ---
        Scalar("csv.separator", ";", "Exact string passed to the CSV reader; the two characters \\t mean tab."),
        Scalar("csv.encoding", "UTF-8", ".NET encoding name; an unknown name fails the CSV read (csv_read_failed)."),
        Scalar("csv.hasHeader", "true", "Boolean. Column mapping needs a header row; false is accepted but leaves no column names to map."),
        Scalar("csv.decimalSeparator", ",", "With \",\" each comma in a numeric cell is replaced by a dot before parsing; any other value leaves the cell unchanged."),
        Scalar("csv.dateFormat", "dd.MM.yyyy", "Exact .NET format tried first; then a culture-invariant general parse; then DateTime.MinValue with invalid_date."),

        // --- zugferd ---
        Set("zugferd.version", "version", "2.3", "Applied in XML and PDF export. Unknown values fall back to 2.3 with config_value_fallback."),
        Set("zugferd.profile", "profile", "Extended", "Applied in XML and PDF export and in the document checks (XRechnung, Minimum). Unknown values fall back to Extended with config_value_fallback."),
        Set("zugferd.format", "format", "CII", "Applied in the PDF export only; the XML-only export always writes CII. Unknown values fall back to CII with config_value_fallback."),

        // --- mapping.invoice ---
        Structural("mapping.invoice", "Invoice header; values come from the first CSV row."),
        Field("mapping.invoice.invoiceNumber", "Invoice number (BT-1); also the output file name."),
        Field("mapping.invoice.invoiceDate", "Invoice date (BT-2). " + DateNote),
        FieldSet("mapping.invoice.typeCode", "invoiceType", null, "Not configured or empty means 380 (library default). Any non-empty value must be a known code or name, otherwise the run fails with invoice_type_unknown."),
        FieldSet("mapping.invoice.currency", "currency", null, "Empty values fall back to EUR silently; unknown non-empty values fall back to EUR with config_value_fallback."),
        Field("mapping.invoice.orderReferenceId", "Purchase order reference passed on descriptor creation."),
        Field("mapping.invoice.name", "Document name; the library writes it in profile Extended only."),
        Field("mapping.invoice.referenceOrderNo", "Written as ReferenceOrderNo (BT-13) when non-empty."),
        Field("mapping.invoice.note", "Written as document note (BT-22) when non-empty."),
        ListField("mapping.invoice.precedingInvoices", "List of preceding invoice references; entries with an empty number are skipped. Not written in profile Minimum (preceding_invoice_not_in_minimum)."),
        Field("mapping.invoice.precedingInvoices[].number", "Preceding invoice number (BT-25); required for the entry to be written."),
        Field("mapping.invoice.precedingInvoices[].date", "Preceding invoice date (BT-26), optional. " + DateNote),

        // --- mapping.buyer ---
        Structural("mapping.buyer", "Buyer party; written only when name resolves to a non-empty value."),
        Field("mapping.buyer.id", "Buyer identifier (BT-46); empty writes no element."),
        Field("mapping.buyer.name", "Buyer name (BT-44); empty skips the whole buyer party (address, id, country), tax id and contact are still written."),
        Field("mapping.buyer.postalCode", "Buyer postal code (BT-53)."),
        Field("mapping.buyer.city", "Buyer city (BT-52)."),
        Field("mapping.buyer.street", "Buyer street (BT-50)."),
        FieldSet("mapping.buyer.country", "country", null, "Empty values fall back to DE silently; unknown non-empty values fall back to DE with config_value_fallback."),
        Field("mapping.buyer.taxId", "Buyer tax identifier (BT-48); written when non-empty together with taxScheme."),
        FieldSet("mapping.buyer.taxScheme", "taxScheme", null, "Scheme of buyer.taxId; empty or unknown falls back to VA (config_value_fallback for unknown non-empty values)."),
        Field("mapping.buyer.contact", "Buyer contact name (BT-56); written when non-empty."),
        Field("mapping.buyer.electronicAddress", "Buyer electronic address (BT-49); written when non-empty. The scheme key of this field is parsed with the electronicAddressScheme value set (fallback GermanyVatNumber)."),

        // --- mapping.seller ---
        Structural("mapping.seller", "Seller party; written only when name resolves to a non-empty value."),
        Field("mapping.seller.id", "Seller identifier (BT-29); empty writes no element."),
        Field("mapping.seller.name", "Seller name (BT-27); empty skips the whole seller party, tax registrations are still written."),
        Field("mapping.seller.postalCode", "Seller postal code (BT-38)."),
        Field("mapping.seller.city", "Seller city (BT-37)."),
        Field("mapping.seller.street", "Seller street (BT-35)."),
        FieldSet("mapping.seller.country", "country", null, "Empty values fall back to DE silently; unknown non-empty values fall back to DE with config_value_fallback."),
        ListField("mapping.seller.taxRegistrations", "List of seller tax registrations (BT-31/BT-32); entries that resolve to an empty value are skipped."),
        new("mapping.seller.taxRegistrations[].column", FieldStatus.Active, s_none, s_valueColumn, null, null, null,
            "CSV column of the registration number, used only when value is empty. This entry is not a field mapping: no rules, no default."),
        new("mapping.seller.taxRegistrations[].value", FieldStatus.Active, s_none, s_valueColumn, null, null, null,
            "Fixed registration number; takes precedence over column."),
        new("mapping.seller.taxRegistrations[].scheme", FieldStatus.Active, s_none, s_none, "taxScheme", "VA", "VA",
            "VA or FC (case-insensitive); empty or unknown falls back to VA (config_value_fallback for unknown non-empty values)."),
        Field("mapping.seller.electronicAddress", "Seller electronic address (BT-34); written when non-empty. The scheme key of this field is parsed with the electronicAddressScheme value set (fallback GermanyVatNumber)."),

        // --- mapping.delivery / orderReference ---
        Structural("mapping.delivery", "Delivery data. Without delivery note and delivery date the invoice date is written as BT-72 (delivery_date_defaulted)."),
        Field("mapping.delivery.deliveryNoteNumber", "Delivery note number (BT-16); written when non-empty together with deliveryNoteDate."),
        Field("mapping.delivery.deliveryNoteDate", "Delivery note date, optional; only used when deliveryNoteNumber is non-empty. " + DateNote),
        Field("mapping.delivery.actualDeliveryDate", "Actual delivery date (BT-72). " + DateNote),
        Structural("mapping.orderReference", "Buyer order reference (BT-13)."),
        Field("mapping.orderReference.orderNumber", "Order number; written when non-empty."),
        Field("mapping.orderReference.orderDate", "Order date, optional; only used when orderNumber is non-empty. " + DateNote),

        // --- mapping.lineItems ---
        Structural("mapping.lineItems", "Line items. Without this section no line items are written."),
        Set("mapping.lineItems.mode", "lineItemsMode", "rows", "Selects the CSV layout. Empty or unknown values select rows (config_value_fallback)."),
        Structural("mapping.lineItems.fields", "Field mappings of a line item. In rows mode required (without fields no line items); in columns mode optional with default fragments Artikel, Beschreibung, Einzelpreis, Menge."),
        new("mapping.lineItems.fields.name", FieldStatus.Active, s_none, s_std, null, null, null,
            "Rows mode: resolution value, column, rules, default; rows with an empty name are skipped. Columns mode: only column is used as fragment (default Artikel); the iteration stops at the first position whose name column is empty."),
        Field("mapping.lineItems.fields.description", "Item description (BT-154). " + ColumnsResolution + " Default fragment in columns mode: Beschreibung."),
        Field("mapping.lineItems.fields.sellerAssignedId", "Seller item id (BT-155). " + ColumnsResolution),
        Field("mapping.lineItems.fields.buyerAssignedId", "Buyer item id (BT-156). " + ColumnsResolution),
        Field("mapping.lineItems.fields.netUnitPrice", "Net unit price (BT-146). " + DecimalNote + " " + ColumnsResolution + " Default fragment in columns mode: Einzelpreis."),
        Field("mapping.lineItems.fields.billedQuantity", "Billed quantity (BT-129). " + DecimalNote + " " + ColumnsResolution + " Default fragment in columns mode: Menge."),
        new("mapping.lineItems.fields.unitCode", FieldStatus.Conditional, [ModeRows], s_std, "unitCode", null, "C62",
            "Unit code (BT-130). " + RowsOnly + " Columns mode uses fixedFields.unitCode. Empty falls back to C62 silently, unknown values with config_value_fallback."),
        new("mapping.lineItems.fields.taxType", FieldStatus.Conditional, [ModeRows], s_std, "taxType", null, "VAT",
            "Tax type (BT-151 scheme). " + RowsOnly + " Columns mode uses fixedFields.taxType. Empty falls back to VAT silently, unknown values with config_value_fallback."),
        new("mapping.lineItems.fields.taxCategoryCode", FieldStatus.Conditional, [ModeRows], s_std, "taxCategoryCode", null, "S",
            "VAT category (BT-151). " + RowsOnly + " Columns mode uses fixedFields.taxCategoryCode. Empty falls back to S silently, unknown values with config_value_fallback."),
        Field("mapping.lineItems.fields.taxPercent", "VAT rate (BT-152). " + DecimalNote + " A trailing % sign is removed. " + ColumnsResolution),
        Field("mapping.lineItems.fields.discountPercent", "Item discount in percent, applied as discounted net unit price. " + DecimalNote + " A trailing % sign is removed. " + ColumnsResolution),
        new("mapping.lineItems.fields.discountReason", FieldStatus.Ignored, s_none, s_none, null, null, null,
            "Accepted but never written: in columns mode the fragment is read and discarded, in rows mode the key is not evaluated. Reported as config_field_ignored when present. Use lineItems.allowanceCharge.reason (rows mode) for a reason text."),
        new("mapping.lineItems.columnPattern", FieldStatus.Conditional, [ModeColumns], s_none, null, null, null,
            "Column naming pattern. Required in columns mode (without it no line items are written); " + ColumnsOnly),
        new("mapping.lineItems.columnPattern.style", FieldStatus.Conditional, [ModeColumns], s_none, "columnStyle", "prefix", "suffix",
            "prefix or suffix (case-insensitive). Empty or unknown values select suffix (config_value_fallback); the initial value when the key is absent is prefix."),
        new("mapping.lineItems.columnPattern.prefix", FieldStatus.Conditional, [ModeColumns], s_none, null, null, null,
            "Text before the index in prefix style (Pos -> Pos1_Artikel). Not used in suffix style."),
        new("mapping.lineItems.columnPattern.separator", FieldStatus.Conditional, [ModeColumns], s_none, null, "_", null,
            "Separator between fragment and index in both styles."),
        new("mapping.lineItems.columnPattern.startIndex", FieldStatus.Conditional, [ModeColumns], s_none, null, "1", null,
            "Index of the first position; the iteration counts up from here until the name column is empty."),
        new("mapping.lineItems.columnPattern.zeroPadding", FieldStatus.Conditional, [ModeColumns], s_none, null, "0", null,
            "Minimum digits of the index, left-padded with zeros; 0 disables padding."),
        new("mapping.lineItems.fixedFields", FieldStatus.Conditional, [ModeColumns], s_none, null, null, null,
            "Dictionary with exactly three effective keys: unitCode, taxType, taxCategoryCode. Other keys are reported as unknown by csv2zugferd_config_get. " + ColumnsOnly),
        new("mapping.lineItems.fixedFields.unitCode", FieldStatus.Conditional, [ModeColumns], s_std, "unitCode", null, "C62",
            "Unit code per position in columns mode. " + ColumnsResolution + " Empty falls back to C62 silently, unknown values with config_value_fallback."),
        new("mapping.lineItems.fixedFields.taxType", FieldStatus.Conditional, [ModeColumns], s_std, "taxType", null, "VAT",
            "Tax type per position in columns mode. " + ColumnsResolution + " Empty falls back to VAT silently, unknown values with config_value_fallback."),
        new("mapping.lineItems.fixedFields.taxCategoryCode", FieldStatus.Conditional, [ModeColumns], s_std, "taxCategoryCode", null, "S",
            "VAT category per position in columns mode. " + ColumnsResolution + " Empty falls back to S silently, unknown values with config_value_fallback."),
        new("mapping.lineItems.allowanceCharge", FieldStatus.Conditional, [ModeRows], s_none, null, null, null,
            "Line allowance or charge (BG-27/BG-28) per row. " + RowsOnly),
        new("mapping.lineItems.allowanceCharge.enabled", FieldStatus.Conditional, [ModeRows], s_none, null, "false", null,
            "Boolean switch; false disables the whole block."),
        new("mapping.lineItems.allowanceCharge.isDiscount", FieldStatus.Conditional, [ModeRows], s_std, "booleanText", "true", "false",
            "true (case-insensitive) writes an allowance, any other non-empty text writes a charge; empty means allowance."),
        new("mapping.lineItems.allowanceCharge.chargePercentage", FieldStatus.Conditional, [ModeRows], s_std, null, null, null,
            "Percentage of the line net amount. " + DecimalNote + " Empty or non-positive values write nothing."),
        new("mapping.lineItems.allowanceCharge.reason", FieldStatus.Conditional, [ModeRows], s_std, null, "Rabatt", null,
            "Reason text; empty resolves to Rabatt."),
        new("mapping.lineItems.columnsFields", FieldStatus.Ignored, s_none, s_none, null, null, null,
            "Deprecated string dictionary, never read by the mapper. Reported as config_field_ignored when present. Use fields with column fragments instead."),

        // --- mapping.totals ---
        Structural("mapping.totals", "Totals; absent means auto."),
        Set("mapping.totals.mode", "totalsMode", "auto", "Empty or unknown values select manual (config_value_fallback); the initial value when the key is absent is auto."),
        new("mapping.totals.lineTotalAmount", FieldStatus.Conditional, [ModeManual], s_std, null, null, null, "Sum of line net amounts (BT-106). " + ManualOnly + " " + DecimalNote),
        new("mapping.totals.allowanceTotalAmount", FieldStatus.Conditional, [ModeManual], s_std, null, null, null, "Document allowances (BT-107). " + ManualOnly + " " + DecimalNote),
        new("mapping.totals.taxBasisAmount", FieldStatus.Conditional, [ModeManual], s_std, null, null, null, "Total without VAT (BT-109). " + ManualOnly + " " + DecimalNote),
        new("mapping.totals.taxTotalAmount", FieldStatus.Conditional, [ModeManual], s_std, null, null, null, "Total VAT (BT-110); the tax breakdown is derived from the line items and adjusted to this total. " + ManualOnly + " " + DecimalNote),
        new("mapping.totals.grandTotalAmount", FieldStatus.Conditional, [ModeManual], s_std, null, null, null, "Total with VAT (BT-112). " + ManualOnly + " " + DecimalNote),
        new("mapping.totals.totalPrepaidAmount", FieldStatus.Conditional, [ModeManual], s_std, null, null, null, "Prepaid amount (BT-113); due payable is grand total minus prepaid. " + ManualOnly + " " + DecimalNote),

        // --- mapping.paymentTerms ---
        Structural("mapping.paymentTerms", "Payment terms; written only when description is non-empty."),
        Field("mapping.paymentTerms.description", "Payment terms text (BT-20); required for the block."),
        Field("mapping.paymentTerms.dueDate", "Due date (BT-9), optional. " + DateNote),

        // --- mapping.paymentMeans ---
        Structural("mapping.paymentMeans", "Payment means (BG-16); without a non-empty typeCode the whole block is ignored (payment_means_without_type_code)."),
        FieldSet("mapping.paymentMeans.typeCode", "paymentMeansType", null, "Empty ignores the block with payment_means_without_type_code; unknown non-empty values fail the run with payment_means_type_unknown."),
        Field("mapping.paymentMeans.information", "Payment means text (BT-82)."),
        Field("mapping.paymentMeans.paymentReference", "Payment reference (BT-83); written when non-empty."),
        ListField("mapping.paymentMeans.sellerAccounts", "Seller bank accounts; entries with an empty IBAN are skipped. Credit transfer (30, 58) without any account yields credit_transfer_without_iban."),
        Field("mapping.paymentMeans.sellerAccounts[].iban", "Seller IBAN (BT-84); whitespace removed, upper-cased; invalid checksum yields invalid_iban_checksum."),
        Field("mapping.paymentMeans.sellerAccounts[].bic", "Seller BIC (BT-86), optional; whitespace removed, upper-cased."),
        Field("mapping.paymentMeans.sellerAccounts[].name", "Account holder (BT-85), optional."),
        new("mapping.paymentMeans.directDebit", FieldStatus.Conditional, s_none, s_none, null, null, null,
            "Evaluated only when typeCode resolves to 59 (SEPA direct debit) or 49 (direct debit); otherwise ignored with direct_debit_ignored."),
        new("mapping.paymentMeans.directDebit.creditorId", FieldStatus.Conditional, s_none, s_std, null, null, null,
            "SEPA creditor id (BT-90); whitespace removed, upper-cased. Missing with 59 yields direct_debit_without_creditor_id."),
        new("mapping.paymentMeans.directDebit.mandateReference", FieldStatus.Conditional, s_none, s_std, null, null, null,
            "Mandate reference (BT-89). Missing with 59 yields direct_debit_without_mandate."),
        new("mapping.paymentMeans.directDebit.buyerIban", FieldStatus.Conditional, s_none, s_std, null, null, null,
            "Debited buyer IBAN (BT-91); normalized like the seller IBAN; invalid checksum yields invalid_iban_checksum; missing with 59 yields direct_debit_without_buyer_iban."),
        new("mapping.paymentMeans.directDebit.buyerBic", FieldStatus.Conditional, s_none, s_std, null, null, null,
            "Buyer BIC, optional; whitespace removed, upper-cased."),

        // --- Generische Teilschlüssel eines Feld-Mappings ---
        new("fieldMapping.value", FieldStatus.Active, s_none, s_none, null, null, null,
            "Fixed value; non-empty value wins over column, rules and default."),
        new("fieldMapping.column", FieldStatus.Active, s_none, s_none, null, null, null,
            "CSV column (header name, case-insensitive match against the CSV header). Used when value is empty and the cell is non-empty."),
        new("fieldMapping.default", FieldStatus.Active, s_none, s_none, null, null, null,
            "Fallback when value, column and rules yield nothing."),
        new("fieldMapping.scheme", FieldStatus.Conditional, s_none, s_none, "electronicAddressScheme", null, "GermanyVatNumber",
            "Only read on buyer.electronicAddress and seller.electronicAddress; on every other field accepted but ignored."),
        new("fieldMapping.rules", FieldStatus.Active, s_none, s_none, null, null, null,
            "Ordered list; the first matching rule wins. Evaluated after value and column, before default."),
        new("fieldMapping.rules[].when", FieldStatus.Active, s_none, s_none, null, null, null,
            "Condition; rules without column or regex are skipped."),
        new("fieldMapping.rules[].when.column", FieldStatus.Active, s_none, s_none, null, null, null,
            "CSV column tested by the regex. In columns mode resolved per position (direct column first, then indexed)."),
        new("fieldMapping.rules[].when.regex", FieldStatus.Active, s_none, s_none, null, null, null,
            ".NET regex, culture-invariant; an invalid pattern is skipped with invalid_regex."),
        new("fieldMapping.rules[].value", FieldStatus.Active, s_none, s_none, null, null, null,
            "Value returned when the rule matches; its allowed values are those of the target field."),
    ];

    public static FieldSemantic? Find(string path) =>
        All.FirstOrDefault(s => string.Equals(s.Path, path, StringComparison.Ordinal));

    public static string StatusName(FieldStatus status) =>
        status switch
        {
            FieldStatus.Active => "active",
            FieldStatus.Conditional => "conditional",
            FieldStatus.Ignored => "ignored",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

    /// <summary>Pfade, die konfigurierbare Feld-Mappings (<c>FieldMapping</c>) sind.</summary>
    public static bool IsFieldMappingPath(string path) =>
        Find(path) is { Sources.Count: > 0 } semantic && semantic.Sources.Contains("rules");

    private static FieldSemantic Structural(string path, string description) =>
        new(path, FieldStatus.Active, s_none, s_none, null, null, null, description);

    private static FieldSemantic Scalar(string path, string? defaultValue, string description) =>
        new(path, FieldStatus.Active, s_none, s_none, null, defaultValue, null, description);

    private static FieldSemantic Set(string path, string valueSet, string defaultValue, string description) =>
        new(path, FieldStatus.Active, s_none, s_none, valueSet, defaultValue, ConfigValues.FindSet(valueSet)!.InvalidFallback, description);

    private static FieldSemantic Field(string path, string description) =>
        new(path, FieldStatus.Active, s_none, s_std, null, null, null, description);

    private static FieldSemantic FieldSet(string path, string valueSet, string? defaultValue, string description) =>
        new(path, FieldStatus.Active, s_none, s_std, valueSet, defaultValue, ConfigValues.FindSet(valueSet)!.InvalidFallback, description);

    private static FieldSemantic ListField(string path, string description) =>
        new(path, FieldStatus.Active, s_none, s_none, null, null, null, description);
}
