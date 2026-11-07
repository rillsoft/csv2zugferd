namespace Csv2Zugferd;

/// <summary>
/// Stabile englische Fehlercodes für fachliche Ablehnungen. Einzige Stelle des Vorrats:
/// Ein Code, der hier nicht steht, existiert nicht. Der deutsche Text steht daneben in der Ausnahme.
/// </summary>
public static class ErrorCodes
{
    public const string CsvFileNotFound = "csv_file_not_found";
    public const string CsvEmpty = "csv_empty";
    public const string CsvReadFailed = "csv_read_failed";
    public const string ConfigNotFound = "config_not_found";
    public const string ConfigInvalidYaml = "config_invalid_yaml";
    public const string ConfigSourceMissing = "config_source_missing";
    public const string ConfigSourceAmbiguous = "config_source_ambiguous";
    public const string InvoiceTypeUnknown = "invoice_type_unknown";
    public const string PaymentMeansTypeUnknown = "payment_means_type_unknown";
    public const string PdfTemplateNotFound = "pdf_template_not_found";
    public const string PdfCreateFailed = "pdf_create_failed";
    public const string MappingFailed = "mapping_failed";
    public const string FileExists = "file_exists";
    public const string OutputPathInvalid = "output_path_invalid";
    public const string ResponseTooLarge = "response_too_large";
    public const string InternalError = "internal_error";
    public const string OutputSchemaInvalid = "output_schema_invalid";

    /// <summary>Alle Codes des Vorrats, für Vertrags- und Vollständigkeitstests.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        CsvFileNotFound,
        CsvEmpty,
        CsvReadFailed,
        ConfigNotFound,
        ConfigInvalidYaml,
        ConfigSourceMissing,
        ConfigSourceAmbiguous,
        InvoiceTypeUnknown,
        PaymentMeansTypeUnknown,
        PdfTemplateNotFound,
        PdfCreateFailed,
        MappingFailed,
        FileExists,
        OutputPathInvalid,
        ResponseTooLarge,
        InternalError,
        OutputSchemaInvalid,
    ];
}
