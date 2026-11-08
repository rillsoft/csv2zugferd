using System.Globalization;
using System.Text;
using Csv2Zugferd.Models;
using CsvHelper;
using CsvHelper.Configuration;
using Serilog;

namespace Csv2Zugferd;

/// <summary>Kopfzeile und Datenzeilen einer CSV-Datei in Dateireihenfolge.</summary>
public sealed record CsvTable(IReadOnlyList<string> Headers, List<Dictionary<string, string>> Rows);

public static class CsvInvoiceReader
{
    public static List<Dictionary<string, string>> Read(string path, CsvConfig csvConfig)
        => ReadTable(path, csvConfig).Rows;

    /// <summary>
    /// Liest Kopfzeile und Datenzeilen. Einziger Lesepfad für CLI und MCP-Server.
    /// Fehlende Datei → <see cref="ErrorCodes.CsvFileNotFound"/>, fehlende Kopfzeile → <see cref="ErrorCodes.CsvEmpty"/>,
    /// andere Lesefehler (Encoding, Format) → <see cref="ErrorCodes.CsvReadFailed"/>.
    /// </summary>
    public static CsvTable ReadTable(string path, CsvConfig csvConfig)
    {
        if (!File.Exists(path))
            throw new Csv2ZugferdException(ErrorCodes.CsvFileNotFound, $"CSV-Datei nicht gefunden: {path}");

        try
        {
            return ReadTableCore(path, csvConfig);
        }
        catch (Csv2ZugferdException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new Csv2ZugferdException(ErrorCodes.CsvReadFailed, $"CSV-Datei konnte nicht gelesen werden: {ex.Message}", ex);
        }
    }

    private static CsvTable ReadTableCore(string path, CsvConfig csvConfig)
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

        if (!csv.Read())
            throw new Csv2ZugferdException(ErrorCodes.CsvEmpty, $"CSV-Datei enthält keine Kopfzeile: {path}");

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
        return new CsvTable(headers, records);
    }
}
