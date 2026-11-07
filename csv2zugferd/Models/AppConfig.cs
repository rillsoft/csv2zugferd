using System.ComponentModel;

namespace Csv2Zugferd.Models;

public class AppConfig
{
    [Description("CSV reading settings: separator, encoding, header, decimal separator and date format.")]
    public CsvConfig Csv { get; set; } = new();

    [Description("ZUGFeRD output settings: version, profile and format of the generated XML/PDF.")]
    public ZugferdConfig Zugferd { get; set; } = new();

    [Description("Field mapping from CSV columns or fixed values to the invoice (header, parties, line items, totals, payment).")]
    public MappingConfig Mapping { get; set; } = new();
}
