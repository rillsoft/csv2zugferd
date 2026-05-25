namespace Csv2Zugferd.Models;

public class AppConfig
{
    public CsvConfig Csv { get; set; } = new();
    public ZugferdConfig Zugferd { get; set; } = new();
    public MappingConfig Mapping { get; set; } = new();
}
