namespace Csv2Zugferd.Models;

public class CsvConfig
{
    public string Separator { get; set; } = ";";
    public string Encoding { get; set; } = "UTF-8";
    public bool HasHeader { get; set; } = true;
    public string DecimalSeparator { get; set; } = ",";
    public string DateFormat { get; set; } = "dd.MM.yyyy";
}
