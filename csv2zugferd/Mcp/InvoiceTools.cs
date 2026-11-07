using System.Text.Json;
using Csv2Zugferd.Models;
using static Csv2Zugferd.Mcp.SchemaBuilder;

namespace Csv2Zugferd.Mcp;

/// <summary>
/// Die Werkzeuge des Einrichtungsassistenten. Jedes Werkzeug ruft ausschließlich die vorhandenen Fachmethoden
/// (<see cref="CsvInvoiceReader"/>, <see cref="YamlConfigLoader"/>, <see cref="InvoiceMapper"/>, <see cref="InvoiceExporter"/>).
/// Vertragsprosa ist englisch; Fehlertexte sind deutsch.
/// </summary>
public static class InvoiceTools
{
    public const string CsvHeaderGet = "csv2zugferd_csv_header_get";

    public static IEnumerable<ToolDefinition> CreateDefinitions()
    {
        yield return CreateCsvHeaderGet();
    }

    // --- csv2zugferd_csv_header_get ---

    public sealed record CsvHeaderResult(
        string CsvPath,
        IReadOnlyList<string> Columns,
        int ColumnCount,
        int RowCount,
        string Separator,
        string Encoding,
        IReadOnlyList<IReadOnlyList<string>> SampleRows);

    private static ToolDefinition CreateCsvHeaderGet()
    {
        var input = Object("Arguments for csv2zugferd_csv_header_get.",
        [
            Prop("csvPath", String("Path to the CSV file on the local file system (absolute or relative to the server's working directory).", minLength: 1)),
            Prop("separator", String("Column separator used in the file. Use \"\\t\" for tab. Same meaning as csv.separator in the YAML configuration.", defaultValue: ";", minLength: 1)),
            Prop("encoding", String("Text encoding of the file, for example UTF-8 or windows-1252. Same meaning as csv.encoding in the YAML configuration.", defaultValue: "UTF-8", minLength: 1)),
            Prop("sampleRows", Integer("Number of data rows to return as samples, 0 to 3. Default 0 returns no cell values, only the header.", minimum: 0, maximum: 3, defaultValue: 0)),
        ], "csvPath");

        var output = Object("Header of the CSV file.",
        [
            Prop("csvPath", String("The csvPath argument as given.")),
            Prop("columns", Array("Column names from the header row in file order.", String("Column name exactly as written in the header."))),
            Prop("columnCount", Integer("Number of columns in the header row.", minimum: 0)),
            Prop("rowCount", Integer("Number of data rows after the header.", minimum: 0)),
            Prop("separator", String("The separator that was applied.")),
            Prop("encoding", String("The encoding that was applied.")),
            Prop("sampleRows", Array("Sample data rows, each as a list of cell values in column order. Empty unless sampleRows > 0.",
                Array("One data row.", String("Cell value as text.")))),
        ], "csvPath", "columns", "columnCount", "rowCount", "separator", "encoding", "sampleRows");

        return new ToolDefinition
        {
            Name = CsvHeaderGet,
            Title = "Read CSV header",
            Description =
                "Reads the header row of a CSV file and returns the column names in file order plus the number of data rows. " +
                "Optionally returns up to 3 sample rows (sampleRows, default 0) so the mapping can be drafted from real values. " +
                "Does not validate a configuration (use csv2zugferd_config_get) and does not map an invoice (use csv2zugferd_invoice_get). " +
                "Read-only, writes no files.",
            InputSchema = ToElement(input),
            OutputSchema = ToElement(output),
            IsReadOnly = true,
            IsDestructive = false,
            IsIdempotent = true,
            IsOpenWorld = true,
            LimitHints = ["sampleRows: 0 (keine Beispielzeilen)"],
            Handler = (args, _) => Task.FromResult<object>(ReadCsvHeader(args)),
        };
    }

    public static CsvHeaderResult ReadCsvHeader(JsonElement args)
    {
        var csvPath = args.GetProperty("csvPath").GetString()!;
        var separator = GetString(args, "separator") ?? ";";
        var encoding = GetString(args, "encoding") ?? "UTF-8";
        var sampleRows = args.TryGetProperty("sampleRows", out var sampleElement) ? sampleElement.GetInt32() : 0;

        var csvConfig = new CsvConfig { Separator = separator, Encoding = encoding, HasHeader = true };
        var table = CsvInvoiceReader.ReadTable(csvPath, csvConfig);

        var samples = table.Rows
            .Take(sampleRows)
            .Select(row => (IReadOnlyList<string>)table.Headers.Select(h => row.GetValueOrDefault(h) ?? string.Empty).ToList())
            .ToList();

        return new CsvHeaderResult(csvPath, table.Headers, table.Headers.Count, table.Rows.Count, separator, encoding, samples);
    }

    private static string? GetString(JsonElement args, string name) =>
        args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
