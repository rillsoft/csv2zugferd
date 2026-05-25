using System.CommandLine;
using Csv2Zugferd;
using Csv2Zugferd.Models;
using Serilog;
using Serilog.Events;

var csvOption = new Option<FileInfo>("--csv", "-c")
{
    Description = "Pfad zur CSV-Datei mit Rechnungsdaten",
    Required = true,
};

var pdfOption = new Option<FileInfo>("--pdf", "-p")
{
    Description = "Pfad zur PDF-Vorlage (Rechnungs-PDF)",
    Required = true,
};

var configOption = new Option<FileInfo>("--config", "-g")
{
    Description = "Pfad zur YML-Konfigurationsdatei",
    Required = true,
};

var outputOption = new Option<DirectoryInfo?>("--output", "-o")
{
    Description = "Ausgabeverzeichnis (Standard: aktuelles Verzeichnis)",
};

var xmlOnlyOption = new Option<bool>("--xml-only")
{
    Description = "Nur XML erzeugen, kein PDF",
};

var verboseOption = new Option<bool>("--verbose", "-v")
{
    Description = "Erweiterte Konsolenausgabe (Debug-Level)",
};

var rootCommand = new RootCommand("csv2zugferd - CSV-to-ZUGFeRD Kommandozeilen-Tool");
rootCommand.Options.Add(csvOption);
rootCommand.Options.Add(pdfOption);
rootCommand.Options.Add(configOption);
rootCommand.Options.Add(outputOption);
rootCommand.Options.Add(xmlOnlyOption);
rootCommand.Options.Add(verboseOption);

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var csvFile = parseResult.GetValue(csvOption)!;
    var pdfFile = parseResult.GetValue(pdfOption)!;
    var configFile = parseResult.GetValue(configOption)!;
    var outputDir = parseResult.GetValue(outputOption);
    var xmlOnly = parseResult.GetValue(xmlOnlyOption);
    var verbose = parseResult.GetValue(verboseOption);

    var logLevel = verbose ? LogEventLevel.Debug : LogEventLevel.Information;

    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Is(logLevel)
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File("logs/zugferd-.log",
            rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();

    try
    {
        Log.Information("ZUGFeRD CSV-to-PDF gestartet");

        // 1. Config laden
        AppConfig config;
        Log.Debug("Konfiguration laden: {ConfigPath}", configFile.FullName);
        config = YamlConfigLoader.Load(configFile.FullName);

        Log.Debug("CSV-Einstellungen: Separator={Separator}, Encoding={Encoding}, DecimalSeparator={DecimalSeparator}, DateFormat={DateFormat}",
            config.Csv.Separator, config.Csv.Encoding, config.Csv.DecimalSeparator, config.Csv.DateFormat);
        Log.Debug("ZUGFeRD-Einstellungen: Version={Version}, Profile={Profile}, Format={Format}",
            config.Zugferd.Version, config.Zugferd.Profile, config.Zugferd.Format);

        // 2. CSV lesen
        Log.Debug("CSV einlesen: {CsvPath}", csvFile.FullName);
        var rows = CsvInvoiceReader.Read(csvFile.FullName, config.Csv);
        Log.Information("{RowCount} Zeile(n) aus CSV gelesen", rows.Count);

        if (rows.Count == 0)
        {
            Log.Error("CSV-Datei enthält keine Daten");
            return 1;
        }

        // 3. Mapping
        Log.Debug("Mapping auf InvoiceDescriptor (Modus: {Mode})", config.Mapping.LineItems?.Mode ?? "rows");
        var desc = InvoiceMapper.Map(rows, config);
        Log.Information("InvoiceDescriptor erstellt: Rechnung {InvoiceNo}, {LineItemCount} Position(en)",
            desc.InvoiceNo, desc.TradeLineItems.Count);

        // 4. Ausgabeverzeichnis bestimmen
        var outDir = outputDir?.FullName ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outDir);

        var invoiceNo = desc.InvoiceNo ?? "output";
        var safeInvoiceNo = string.Join("_", invoiceNo.Split(Path.GetInvalidFileNameChars()));

        // 5. XML speichern
        var xmlPath = Path.Combine(outDir, $"{safeInvoiceNo}.xml");
        Log.Debug("XML speichern: {XmlPath}", xmlPath);
        InvoiceExporter.SaveXml(desc, xmlPath, config.Zugferd);
        Log.Information("XML erzeugt: {XmlPath}", xmlPath);

        // 6. PDF speichern (optional)
        if (!xmlOnly)
        {
            var pdfPath = Path.Combine(outDir, $"{safeInvoiceNo}.pdf");
            Log.Debug("PDF erzeugen: {PdfPath} (Vorlage: {TemplatePath})", pdfPath, pdfFile.FullName);
            await InvoiceExporter.SavePdfAsync(desc, pdfPath, pdfFile.FullName, config.Zugferd);
            Log.Information("PDF erzeugt: {PdfPath}", pdfPath);
        }

        Log.Information("Verarbeitung abgeschlossen");
        return 0;
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Fehler bei der Verarbeitung");
        return 1;
    }
    finally
    {
        await Log.CloseAndFlushAsync();
    }
});

return rootCommand.Parse(args).Invoke();

