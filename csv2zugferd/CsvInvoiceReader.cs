using System.Globalization;
using System.Text;
using Csv2Zugferd.Models;
using CsvHelper;
using CsvHelper.Configuration;
using Serilog;

namespace Csv2Zugferd;

public static class CsvInvoiceReader
{
    public static List<Dictionary<string, string>> Read(string path, CsvConfig csvConfig)
    {
        var encoding = Encoding.GetEncoding(csvConfig.Encoding);
        var separator = csvConfig.Separator switch
        {
            "\\t" => "\t",
            _ => csvConfig.Separator
        };

        Log.Debug("CSV-Reader: Delimiter={Delimiter}, Encoding={Encoding}, HasHeader={HasHeader}", separator, csvConfig.Encoding, csvConfig.HasHeader);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = separator,
            HasHeaderRecord = csvConfig.HasHeader,
            MissingFieldFound = null,
            BadDataFound = null,
        };

        using var reader = new StreamReader(path, encoding);
        using var csv = new CsvReader(reader, config);

        var records = new List<Dictionary<string, string>>();

        csv.Read();
        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? [];
        Log.Debug("CSV-Header: {HeaderCount} Spalten erkannt: {Headers}", headers.Length, string.Join(", ", headers));

        while (csv.Read())
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers)
            {
                row[header] = csv.GetField(header) ?? string.Empty;
            }
            records.Add(row);
        }

        Log.Debug("CSV-Reader: {RecordCount} Datensätze gelesen", records.Count);
        return records;
    }
}
