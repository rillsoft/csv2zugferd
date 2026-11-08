using System.ComponentModel;

namespace Csv2Zugferd.Models;

public class CsvConfig
{
    [Description("Column separator of the CSV file, for example \";\" or \",\". Use \"\\t\" for tab.")]
    public string Separator { get; set; } = ";";

    [Description("Text encoding of the CSV file, for example UTF-8 or windows-1252 (.NET encoding name).")]
    public string Encoding { get; set; } = "UTF-8";

    [Description("True if the first row contains the column names. Column mapping requires a header row.")]
    public bool HasHeader { get; set; } = true;

    [Description("Decimal separator used in CSV amounts and quantities, \",\" or \".\". With \",\" every comma in a number is read as decimal point.")]
    public string DecimalSeparator { get; set; } = ",";

    [Description("Exact .NET date format of CSV date values, for example dd.MM.yyyy or yyyy-MM-dd. Values that do not match are also tried with a culture-invariant general parse before falling back.")]
    public string DateFormat { get; set; } = "dd.MM.yyyy";
}
