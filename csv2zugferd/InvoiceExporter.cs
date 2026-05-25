using Csv2Zugferd.Models;
using s2industries.ZUGFeRD;
using s2industries.ZUGFeRD.PDF;
using Serilog;

namespace Csv2Zugferd;

public static class InvoiceExporter
{
    public static void SaveXml(InvoiceDescriptor desc, string outputPath, ZugferdConfig zugferdConfig)
    {
        var version = ParseVersion(zugferdConfig.Version);
        var profile = ParseProfile(zugferdConfig.Profile);

        Log.Debug("XML-Export: Version={Version}, Profile={Profile}, Pfad={Path}", version, profile, outputPath);

        using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        desc.Save(stream, version, profile);
        stream.Flush();

        Log.Debug("XML-Export abgeschlossen: {Bytes} Bytes geschrieben", new FileInfo(outputPath).Length);
    }

    public static async Task SavePdfAsync(InvoiceDescriptor desc, string outputPath, string pdfTemplatePath, ZugferdConfig zugferdConfig)
    {
        var version = ParseVersion(zugferdConfig.Version);
        var profile = ParseProfile(zugferdConfig.Profile);
        var format = ParseFormat(zugferdConfig.Format);

        Log.Debug("PDF-Export: Version={Version}, Profile={Profile}, Format={Format}", version, profile, format);
        Log.Debug("PDF-Vorlage: {TemplatePath}", pdfTemplatePath);

        await InvoicePdfProcessor.SaveToPdfAsync(outputPath, version, profile, format, pdfTemplatePath, desc);

        Log.Debug("PDF-Export abgeschlossen: {Bytes} Bytes geschrieben", new FileInfo(outputPath).Length);
    }

    private static ZUGFeRDVersion ParseVersion(string version) =>
        version switch
        {
            "1" or "1.0" => ZUGFeRDVersion.Version1,
            "2.0" or "20" => ZUGFeRDVersion.Version20,
            "2.3" or "23" => ZUGFeRDVersion.Version23,
            _ => ZUGFeRDVersion.Version23,
        };

    private static Profile ParseProfile(string profile) =>
        profile.ToLowerInvariant() switch
        {
            "minimum" => Profile.Minimum,
            "basicwl" => Profile.BasicWL,
            "basic" => Profile.Basic,
            "comfort" or "en16931" => Profile.Comfort,
            "extended" => Profile.Extended,
            "xrechnung" => Profile.XRechnung,
            _ => Profile.Extended,
        };

    private static ZUGFeRDFormats ParseFormat(string format) =>
        format.ToUpperInvariant() switch
        {
            "CII" => ZUGFeRDFormats.CII,
            "UBL" => ZUGFeRDFormats.UBL,
            _ => ZUGFeRDFormats.CII,
        };
}
