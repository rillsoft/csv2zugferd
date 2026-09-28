namespace Csv2Zugferd.Models;

public class MappingConfig
{
    public InvoiceMapping? Invoice { get; set; }
    public BuyerMapping? Buyer { get; set; }
    public SellerMapping? Seller { get; set; }
    public DeliveryMapping? Delivery { get; set; }
    public OrderReferenceMapping? OrderReference { get; set; }
    public LineItemsMapping? LineItems { get; set; }
    public TotalsMapping? Totals { get; set; }
    public PaymentTermsMapping? PaymentTerms { get; set; }
    public PaymentMeansMapping? PaymentMeans { get; set; }
}

public class FieldMapping
{
    public string? Column { get; set; }
    public string? Value { get; set; }
    public string? Default { get; set; }
    public string? Scheme { get; set; }
    public List<FieldRule>? Rules { get; set; }
}

public class FieldRule
{
    public FieldRuleCondition? When { get; set; }
    public string? Value { get; set; }
}

public class FieldRuleCondition
{
    public string? Column { get; set; }
    public string? Regex { get; set; }
}

public class InvoiceMapping
{
    public FieldMapping? InvoiceNumber { get; set; }
    public FieldMapping? InvoiceDate { get; set; }
    public FieldMapping? Currency { get; set; }
    public FieldMapping? OrderReferenceId { get; set; }
    public FieldMapping? Name { get; set; }
    public FieldMapping? ReferenceOrderNo { get; set; }
    public FieldMapping? Note { get; set; }
}

public class BuyerMapping
{
    public FieldMapping? Name { get; set; }
    public FieldMapping? PostalCode { get; set; }
    public FieldMapping? City { get; set; }
    public FieldMapping? Street { get; set; }
    public FieldMapping? Country { get; set; }
    public FieldMapping? TaxId { get; set; }
    public FieldMapping? TaxScheme { get; set; }
    public FieldMapping? Contact { get; set; }
    public FieldMapping? ElectronicAddress { get; set; }
}

public class SellerMapping
{
    public FieldMapping? Name { get; set; }
    public FieldMapping? PostalCode { get; set; }
    public FieldMapping? City { get; set; }
    public FieldMapping? Street { get; set; }
    public FieldMapping? Country { get; set; }
    public List<TaxRegistrationMapping>? TaxRegistrations { get; set; }
    public FieldMapping? ElectronicAddress { get; set; }
}

public class TaxRegistrationMapping
{
    public string? Column { get; set; }
    public string? Value { get; set; }
    public string? Scheme { get; set; }
}

public class DeliveryMapping
{
    public FieldMapping? DeliveryNoteNumber { get; set; }
    public FieldMapping? DeliveryNoteDate { get; set; }
    public FieldMapping? ActualDeliveryDate { get; set; }
}

public class OrderReferenceMapping
{
    public FieldMapping? OrderNumber { get; set; }
    public FieldMapping? OrderDate { get; set; }
}

public class LineItemsMapping
{
    public string Mode { get; set; } = "rows";
    public LineItemFieldsMapping? Fields { get; set; }
    public ColumnPatternConfig? ColumnPattern { get; set; }
    public Dictionary<string, FieldMapping>? FixedFields { get; set; }
    public AllowanceChargeMapping? AllowanceCharge { get; set; }

    // For columns mode: simple field name fragments parsed from YAML
    public Dictionary<string, string>? ColumnsFields { get; set; }
}

public class LineItemFieldsMapping
{
    public FieldMapping? Name { get; set; }
    public FieldMapping? Description { get; set; }
    public FieldMapping? SellerAssignedId { get; set; }
    public FieldMapping? BuyerAssignedId { get; set; }
    public FieldMapping? NetUnitPrice { get; set; }
    public FieldMapping? BilledQuantity { get; set; }
    public FieldMapping? UnitCode { get; set; }
    public FieldMapping? TaxType { get; set; }
    public FieldMapping? TaxCategoryCode { get; set; }
    public FieldMapping? TaxPercent { get; set; }
    public FieldMapping? DiscountPercent { get; set; }
    public FieldMapping? DiscountReason { get; set; }
}

public class ColumnPatternConfig
{
    public string Style { get; set; } = "prefix";
    public string? Prefix { get; set; }
    public string Separator { get; set; } = "_";
    public int StartIndex { get; set; } = 1;
    public int ZeroPadding { get; set; } = 0;
}

public class AllowanceChargeMapping
{
    public bool Enabled { get; set; }
    public FieldMapping? IsDiscount { get; set; }
    public FieldMapping? ChargePercentage { get; set; }
    public FieldMapping? Reason { get; set; }
}

public class TotalsMapping
{
    public string Mode { get; set; } = "auto";
    public FieldMapping? LineTotalAmount { get; set; }
    public FieldMapping? AllowanceTotalAmount { get; set; }
    public FieldMapping? TaxBasisAmount { get; set; }
    public FieldMapping? TaxTotalAmount { get; set; }
    public FieldMapping? GrandTotalAmount { get; set; }
    public FieldMapping? TotalPrepaidAmount { get; set; }
}

public class PaymentTermsMapping
{
    public FieldMapping? Description { get; set; }
    public FieldMapping? DueDate { get; set; }
}

public class PaymentMeansMapping
{
    public FieldMapping? TypeCode { get; set; }
    public FieldMapping? Information { get; set; }
    public FieldMapping? PaymentReference { get; set; }
    public List<FinancialAccountMapping>? SellerAccounts { get; set; }
    public DirectDebitMapping? DirectDebit { get; set; }
}

public class FinancialAccountMapping
{
    public FieldMapping? Iban { get; set; }
    public FieldMapping? Bic { get; set; }
    public FieldMapping? Name { get; set; }
}

public class DirectDebitMapping
{
    public FieldMapping? CreditorId { get; set; }
    public FieldMapping? MandateReference { get; set; }
    public FieldMapping? BuyerIban { get; set; }
    public FieldMapping? BuyerBic { get; set; }
}
