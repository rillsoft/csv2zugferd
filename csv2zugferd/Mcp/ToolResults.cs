using System.Text.Json;
using System.Text.Json.Serialization;

namespace Csv2Zugferd.Mcp;

/// <summary>Ergebnis-DTOs der Werkzeuge nach Vertragsentwurf; Felder <c>lowerCamelCase</c> über <see cref="ToolRegistry.ResultJsonOptions"/>.</summary>
public static class ToolResults
{
    public sealed record Diagnostic(string Code, string Path, string Message)
    {
        public static Diagnostic From(MappingDiagnostic d) => new(d.Code, d.Path, d.Message);
    }

    // --- csv2zugferd_csv_header_get ---

    public sealed record CsvHeader(
        string CsvPath,
        IReadOnlyList<string> Columns,
        int ColumnCount,
        int RowCount,
        string Separator,
        string Encoding,
        IReadOnlyList<IReadOnlyList<string>> SampleRows);

    // --- csv2zugferd_config_schema_get ---

    public sealed record FieldSemanticResult(
        string Path,
        string Status,
        IReadOnlyList<string> Modes,
        IReadOnlyList<string> Sources,
        string? ValueSet,
        string? DefaultValue,
        string? InvalidFallback,
        string Description);

    public sealed record ConfigSchema(
        string? Section,
        JsonElement Schema,
        IReadOnlyList<FieldSemanticResult> Semantics,
        string SchemaVersion);

    // --- csv2zugferd_config_get ---

    public sealed record CsvSettings(string Separator, string Encoding, bool HasHeader, string DecimalSeparator, string DateFormat);

    public sealed record ZugferdSettings(string Version, string Profile, string Format);

    public sealed record MappedField(string Field, IReadOnlyList<string> Sources);

    public sealed record InvalidValue(string Path, string Value, IReadOnlyList<string> Allowed, string? Fallback);

    public sealed record ConfigCheck(
        string Source,
        CsvSettings Csv,
        ZugferdSettings Zugferd,
        string LineItemsMode,
        string TotalsMode,
        IReadOnlyList<MappedField> MappedFields,
        IReadOnlyList<string> UnknownKeys,
        IReadOnlyList<InvalidValue> InvalidValues,
        IReadOnlyList<Diagnostic> Warnings,
        IReadOnlyList<string> ColumnsReferenced,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? ColumnsMissing,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? ColumnsUnused);

    // --- csv2zugferd_invoice_get ---

    public sealed record Totals(
        decimal LineTotal,
        decimal AllowanceTotal,
        decimal ChargeTotal,
        decimal TaxBasisTotal,
        decimal TaxTotal,
        decimal GrandTotal,
        decimal DuePayable);

    public sealed record TaxBreakdownEntry(decimal Percent, string CategoryCode, decimal BasisAmount, decimal TaxAmount);

    public sealed record LineItem(string Name, decimal Quantity, string UnitCode, decimal NetUnitPrice, decimal LineTotal, decimal TaxPercent);

    public sealed record Party(string Name, string City, string Country);

    public sealed record Parties(Party? Seller, Party? Buyer);

    public sealed record Invoice(
        string InvoiceNumber,
        string InvoiceDate,
        string TypeCode,
        string Currency,
        string Profile,
        string Version,
        string Format,
        int RowCount,
        int LineItemCount,
        Totals Totals,
        IReadOnlyList<TaxBreakdownEntry> TaxBreakdown,
        IReadOnlyList<Diagnostic> Warnings,
        long XmlBytes,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? PdfBytes,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<LineItem>? LineItems,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Parties? Parties);

    // --- csv2zugferd_config_file_create ---

    public sealed record ConfigFile(string ConfigPath, long Bytes, string CliCommand);
}
