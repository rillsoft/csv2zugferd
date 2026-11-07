using System.ComponentModel;

namespace Csv2Zugferd.Models;

public class MappingConfig
{
    [Description("Invoice header: number, date, type code, currency, order reference, name, note and preceding invoices.")]
    public InvoiceMapping? Invoice { get; set; }

    [Description("Buyer (customer) party: name, address, identifiers and electronic address.")]
    public BuyerMapping? Buyer { get; set; }

    [Description("Seller (supplier) party: name, address, tax registrations and electronic address. Usually fixed values.")]
    public SellerMapping? Seller { get; set; }

    [Description("Delivery note reference and actual delivery date (BT-72). Without delivery data the invoice date is used as delivery date.")]
    public DeliveryMapping? Delivery { get; set; }

    [Description("Buyer order reference (BT-13) with order number and date.")]
    public OrderReferenceMapping? OrderReference { get; set; }

    [Description("Line items: mode (rows or columns), field mappings, column pattern for columns mode, fixed fields and line allowances.")]
    public LineItemsMapping? LineItems { get; set; }

    [Description("Document totals: auto (calculated from line items) or manual (read from CSV).")]
    public TotalsMapping? Totals { get; set; }

    [Description("Payment terms: description text and due date (BT-9, BT-20).")]
    public PaymentTermsMapping? PaymentTerms { get; set; }

    [Description("Payment means (BG-16): type code, seller bank accounts and SEPA direct debit data.")]
    public PaymentMeansMapping? PaymentMeans { get; set; }
}

public class FieldMapping
{
    [Description("Name of the CSV column to read the value from. In columns mode a line item field name is a fragment that is combined with the position index (for example Artikel -> Artikel_001); a direct global column with this exact name is tried first.")]
    public string? Column { get; set; }

    [Description("Fixed value. Takes precedence over column, rules and default.")]
    public string? Value { get; set; }

    [Description("Fallback value used when value is not set, the column is empty or missing and no rule matches.")]
    public string? Default { get; set; }

    [Description("Scheme qualifier for electronicAddress (GermanyVatNumber, LuxemburgVatNumber, ElectronicMailSmtp or EM). Ignored on all other fields; use buyer.taxScheme for the buyer tax id.")]
    public string? Scheme { get; set; }

    [Description("Ordered regex rules evaluated after value and column: the first rule whose column value matches the regex sets the value. Invalid regex patterns are logged and skipped.")]
    public List<FieldRule>? Rules { get; set; }
}

public class FieldRule
{
    [Description("Condition of the rule: a CSV column and a .NET regex. Both are required for the rule to be evaluated.")]
    public FieldRuleCondition? When { get; set; }

    [Description("Value that is used when the condition matches.")]
    public string? Value { get; set; }
}

public class FieldRuleCondition
{
    [Description("CSV column whose value is tested. In columns mode the column is resolved per position (direct column first, then indexed column).")]
    public string? Column { get; set; }

    [Description(".NET regular expression tested against the column value, for example ^003 or (?i)^lastschrift$.")]
    public string? Regex { get; set; }
}

public class InvoiceMapping
{
    [Description("Invoice number (BT-1). Required for a valid invoice; also used as output file name.")]
    public FieldMapping? InvoiceNumber { get; set; }

    [Description("Invoice issue date (BT-2), parsed with csv.dateFormat. Required for a valid invoice.")]
    public FieldMapping? InvoiceDate { get; set; }

    [Description("Invoice type code (BT-3) as UNTDID 1001 number (380 invoice, 381 credit note, 384 correction, ...) or library name (Invoice, CreditNote, Correction, ...). Not set means 380. Unknown values are an error (invoice_type_unknown).")]
    public FieldMapping? TypeCode { get; set; }

    [Description("Invoice currency code (BT-5), ISO 4217 such as EUR. Unknown values fall back to EUR.")]
    public FieldMapping? Currency { get; set; }

    [Description("Order reference passed to the invoice descriptor on creation (purchase order reference).")]
    public FieldMapping? OrderReferenceId { get; set; }

    [Description("Document name shown as invoice title (for example WARENRECHNUNG, STORNORECHNUNG). Written only in profile Extended.")]
    public FieldMapping? Name { get; set; }

    [Description("Buyer order number written as ReferenceOrderNo of the descriptor (BT-13 alternative to orderReference.orderNumber).")]
    public FieldMapping? ReferenceOrderNo { get; set; }

    [Description("Free text note on document level (BT-22). Empty values add no note.")]
    public FieldMapping? Note { get; set; }

    [Description("Preceding invoices (BT-25/BT-26) for credit notes and corrections; one entry per referenced invoice. Written from profile BasicWL on, not in Minimum.")]
    public List<PrecedingInvoiceMapping>? PrecedingInvoices { get; set; }
}

public class PrecedingInvoiceMapping
{
    [Description("Number of the preceding invoice (BT-25). Entries with an empty number are skipped.")]
    public FieldMapping? Number { get; set; }

    [Description("Issue date of the preceding invoice (BT-26), optional, parsed with csv.dateFormat.")]
    public FieldMapping? Date { get; set; }
}

public class BuyerMapping
{
    [Description("Buyer identifier (BT-46), usually the customer number at the seller; written as ram:ID of the BuyerTradeParty. Empty values write no element.")]
    public FieldMapping? Id { get; set; }

    [Description("Buyer name (BT-44). Without a name no buyer party is written at all.")]
    public FieldMapping? Name { get; set; }

    [Description("Buyer postal code (BT-53).")]
    public FieldMapping? PostalCode { get; set; }

    [Description("Buyer city (BT-52).")]
    public FieldMapping? City { get; set; }

    [Description("Buyer street and house number (BT-50).")]
    public FieldMapping? Street { get; set; }

    [Description("Buyer country code (BT-55), ISO 3166-1 alpha-2 such as DE. Unknown values fall back to DE.")]
    public FieldMapping? Country { get; set; }

    [Description("Buyer tax identifier (BT-48), for example the VAT id. Written together with taxScheme.")]
    public FieldMapping? TaxId { get; set; }

    [Description("Scheme of the buyer tax identifier: VA (VAT id) or FC (tax number). Unknown values fall back to VA.")]
    public FieldMapping? TaxScheme { get; set; }

    [Description("Buyer contact person name (BT-56).")]
    public FieldMapping? Contact { get; set; }

    [Description("Buyer electronic address (BT-49) with scheme in the scheme key of this field (GermanyVatNumber, LuxemburgVatNumber, ElectronicMailSmtp/EM; unknown or missing falls back to GermanyVatNumber).")]
    public FieldMapping? ElectronicAddress { get; set; }
}

public class SellerMapping
{
    [Description("Seller identifier (BT-29), for example the supplier number at the buyer; written as ram:ID of the SellerTradeParty. Empty values write no element.")]
    public FieldMapping? Id { get; set; }

    [Description("Seller name (BT-27). Without a name no seller party is written at all.")]
    public FieldMapping? Name { get; set; }

    [Description("Seller postal code (BT-38).")]
    public FieldMapping? PostalCode { get; set; }

    [Description("Seller city (BT-37).")]
    public FieldMapping? City { get; set; }

    [Description("Seller street and house number (BT-35).")]
    public FieldMapping? Street { get; set; }

    [Description("Seller country code (BT-40), ISO 3166-1 alpha-2 such as DE. Unknown values fall back to DE.")]
    public FieldMapping? Country { get; set; }

    [Description("Seller tax registrations (BT-31 VAT id with scheme VA, BT-32 tax number with scheme FC); one entry per registration with value or column and scheme.")]
    public List<TaxRegistrationMapping>? TaxRegistrations { get; set; }

    [Description("Seller electronic address (BT-34) with scheme in the scheme key of this field (GermanyVatNumber, LuxemburgVatNumber, ElectronicMailSmtp/EM; unknown or missing falls back to GermanyVatNumber).")]
    public FieldMapping? ElectronicAddress { get; set; }
}

public class TaxRegistrationMapping
{
    [Description("CSV column with the registration number. Used only when value is not set.")]
    public string? Column { get; set; }

    [Description("Fixed registration number, for example DE123456789. Takes precedence over column.")]
    public string? Value { get; set; }

    [Description("Scheme of the registration: VA (VAT id) or FC (tax number). Unknown or missing falls back to VA.")]
    public string? Scheme { get; set; }
}

public class DeliveryMapping
{
    [Description("Delivery note number (BT-16). Written together with deliveryNoteDate as delivery note reference.")]
    public FieldMapping? DeliveryNoteNumber { get; set; }

    [Description("Delivery note date, optional, parsed with csv.dateFormat. Only written when deliveryNoteNumber is present.")]
    public FieldMapping? DeliveryNoteDate { get; set; }

    [Description("Actual delivery date (BT-72), parsed with csv.dateFormat. If neither delivery note nor delivery date is configured, the invoice date is used and a warning is reported.")]
    public FieldMapping? ActualDeliveryDate { get; set; }
}

public class OrderReferenceMapping
{
    [Description("Buyer order number (BT-13). Without a number no order reference is written.")]
    public FieldMapping? OrderNumber { get; set; }

    [Description("Buyer order date, optional, parsed with csv.dateFormat.")]
    public FieldMapping? OrderDate { get; set; }
}

public class LineItemsMapping
{
    [Description("Line item layout of the CSV: rows (one CSV row per line item, header data from the first row) or columns (one invoice per CSV row, line items in numbered column groups). Case-insensitive; any value other than columns selects rows.")]
    public string Mode { get; set; } = "rows";

    [Description("Line item field mappings. In rows mode each field reads from the row; in columns mode column names are fragments combined with the position index.")]
    public LineItemFieldsMapping? Fields { get; set; }

    [Description("Column naming pattern for columns mode: prefix (Pos1_Artikel) or suffix (Artikel_001) with separator, start index and zero padding. Required in columns mode; ignored in rows mode.")]
    public ColumnPatternConfig? ColumnPattern { get; set; }

    [Description("Fixed line item fields for columns mode: unitCode, taxType and taxCategoryCode, each a field mapping (value, column, rules, default). Only these three keys have an effect. Ignored in rows mode, where fields.unitCode/taxType/taxCategoryCode are used instead.")]
    public Dictionary<string, FieldMapping>? FixedFields { get; set; }

    [Description("Additional line allowance or charge (BG-27/BG-28) per line item in rows mode: enabled flag, isDiscount, percentage and reason. Ignored in columns mode.")]
    public AllowanceChargeMapping? AllowanceCharge { get; set; }

    [Description("Deprecated and ignored: accepted for compatibility but never read by the mapper. Use fields with column fragments instead.")]
    public Dictionary<string, string>? ColumnsFields { get; set; }
}

public class LineItemFieldsMapping
{
    [Description("Item name (BT-153). Required: rows without a name are skipped; in columns mode the iteration stops at the first position with an empty name column. In columns mode only column (fragment) is used.")]
    public FieldMapping? Name { get; set; }

    [Description("Item description (BT-154), a real description text, not a product code.")]
    public FieldMapping? Description { get; set; }

    [Description("Seller assigned item identifier (BT-155), for example the article number.")]
    public FieldMapping? SellerAssignedId { get; set; }

    [Description("Buyer assigned item identifier (BT-156).")]
    public FieldMapping? BuyerAssignedId { get; set; }

    [Description("Net unit price (BT-146) parsed with csv.decimalSeparator. Invalid values fall back to 0 with a warning.")]
    public FieldMapping? NetUnitPrice { get; set; }

    [Description("Billed quantity (BT-129) parsed with csv.decimalSeparator. Invalid values fall back to 0 with a warning.")]
    public FieldMapping? BilledQuantity { get; set; }

    [Description("Unit of measure code (BT-130), UN/ECE Recommendation 20 such as C62 (piece), DAY, HUR, KGM. Rows mode only (columns mode uses fixedFields.unitCode). Unknown values fall back to C62.")]
    public FieldMapping? UnitCode { get; set; }

    [Description("Tax type code (BT-151 scheme), normally VAT. Rows mode only (columns mode uses fixedFields.taxType). Unknown values fall back to VAT.")]
    public FieldMapping? TaxType { get; set; }

    [Description("VAT category code (BT-151): S standard, Z zero rated, E exempt, AE reverse charge, K intra-community, G export, O not subject, L/M Canary Islands/Ceuta. Rows mode only (columns mode uses fixedFields.taxCategoryCode). Unknown values fall back to S.")]
    public FieldMapping? TaxCategoryCode { get; set; }

    [Description("VAT rate in percent (BT-152), for example 19 or 7. Values like 19%, 19.00 and 19,00 are accepted. Invalid values fall back to 0 with a warning.")]
    public FieldMapping? TaxPercent { get; set; }

    [Description("Item discount in percent (10, 10.00 or 10%). Applied as discounted net unit price; the list price is kept as gross price. Works in rows and columns mode.")]
    public FieldMapping? DiscountPercent { get; set; }

    [Description("Deprecated and ignored: accepted for compatibility but has no effect on the output. For a reason text use lineItems.allowanceCharge.reason in rows mode.")]
    public FieldMapping? DiscountReason { get; set; }
}

public class ColumnPatternConfig
{
    [Description("prefix builds <prefix><index><separator><fragment> (Pos1_Artikel); suffix builds <fragment><separator><index> (Artikel_001). Case-insensitive; any value other than prefix selects suffix.")]
    public string Style { get; set; } = "prefix";

    [Description("Text before the index in prefix style, for example Pos. Not used in suffix style.")]
    public string? Prefix { get; set; }

    [Description("Separator between fragment and index, default _.")]
    public string Separator { get; set; } = "_";

    [Description("Index of the first line item column group, default 1 (use 0 for PRODUCT_NAME_0).")]
    public int StartIndex { get; set; } = 1;

    [Description("Minimum number of digits of the index, left-padded with zeros, default 0 (no padding). 3 gives 001, 002, ...")]
    public int ZeroPadding { get; set; } = 0;
}

public class AllowanceChargeMapping
{
    [Description("True to write a line allowance or charge per item. Default false. Rows mode only.")]
    public bool Enabled { get; set; }

    [Description("true (default when empty) writes an allowance (BG-27), any other value writes a charge (BG-28). Case-insensitive.")]
    public FieldMapping? IsDiscount { get; set; }

    [Description("Percentage of the line net amount (10, 10.00 or 10%). Empty or non-positive values write no allowance or charge.")]
    public FieldMapping? ChargePercentage { get; set; }

    [Description("Reason text of the allowance or charge (BT-139/BT-144), default Rabatt.")]
    public FieldMapping? Reason { get; set; }
}

public class TotalsMapping
{
    [Description("auto (default) calculates all totals and the tax breakdown from the line items; manual reads the totals from the CSV. Case-insensitive; any value other than auto selects manual.")]
    public string Mode { get; set; } = "auto";

    [Description("Sum of line net amounts (BT-106). Manual mode only; must equal the sum of the rounded line totals.")]
    public FieldMapping? LineTotalAmount { get; set; }

    [Description("Sum of document level allowances (BT-107). Manual mode only; line discounts are already contained in the line totals, so this is normally 0.")]
    public FieldMapping? AllowanceTotalAmount { get; set; }

    [Description("Invoice total amount without VAT (BT-109). Manual mode only.")]
    public FieldMapping? TaxBasisAmount { get; set; }

    [Description("Total VAT amount (BT-110). Manual mode only; the tax breakdown per rate is derived from the line items and a rounding difference is assigned to the last group.")]
    public FieldMapping? TaxTotalAmount { get; set; }

    [Description("Invoice total amount with VAT (BT-112). Manual mode only.")]
    public FieldMapping? GrandTotalAmount { get; set; }

    [Description("Paid amount (BT-113). Manual mode only; the amount due (BT-115) is grand total minus prepaid.")]
    public FieldMapping? TotalPrepaidAmount { get; set; }
}

public class PaymentTermsMapping
{
    [Description("Payment terms text (BT-20). Without a description no payment terms are written.")]
    public FieldMapping? Description { get; set; }

    [Description("Payment due date (BT-9), optional, parsed with csv.dateFormat. Only written together with description.")]
    public FieldMapping? DueDate { get; set; }
}

public class PaymentMeansMapping
{
    [Description("Payment means type code (BT-81) as UNTDID 4461 number (58 SEPA credit transfer, 59 SEPA direct debit, 30 credit transfer, 49 direct debit, ...) or library name (SEPACreditTransfer, SEPADirectDebit, ...). Without a type code the whole paymentMeans block is ignored with a warning; unknown values are an error (payment_means_type_unknown).")]
    public FieldMapping? TypeCode { get; set; }

    [Description("Free text on the payment means (BT-82).")]
    public FieldMapping? Information { get; set; }

    [Description("Remittance information / payment reference (BT-83), usually the invoice number.")]
    public FieldMapping? PaymentReference { get; set; }

    [Description("Seller bank accounts (BG-17), one entry per account with iban, bic and account name. Accounts with an empty IBAN are skipped.")]
    public List<FinancialAccountMapping>? SellerAccounts { get; set; }

    [Description("SEPA direct debit data (creditor id, mandate reference, buyer account). Evaluated only when typeCode is 59 or 49; otherwise ignored with a warning.")]
    public DirectDebitMapping? DirectDebit { get; set; }
}

public class FinancialAccountMapping
{
    [Description("Seller IBAN (BT-84). Whitespace is removed and letters are upper-cased; an invalid checksum is reported as warning.")]
    public FieldMapping? Iban { get; set; }

    [Description("Seller BIC (BT-86), optional.")]
    public FieldMapping? Bic { get; set; }

    [Description("Account holder name (BT-85), optional.")]
    public FieldMapping? Name { get; set; }
}

public class DirectDebitMapping
{
    [Description("SEPA creditor identifier (BT-90), whitespace removed and upper-cased.")]
    public FieldMapping? CreditorId { get; set; }

    [Description("SEPA mandate reference (BT-89).")]
    public FieldMapping? MandateReference { get; set; }

    [Description("Debited buyer IBAN (BT-91), normalized like the seller IBAN; an invalid checksum is reported as warning.")]
    public FieldMapping? BuyerIban { get; set; }

    [Description("Buyer BIC, optional.")]
    public FieldMapping? BuyerBic { get; set; }
}
