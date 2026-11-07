using Csv2Zugferd.Models;
using s2industries.ZUGFeRD;
using s2industries.ZUGFeRD.PDF;
using Serilog;

namespace Csv2Zugferd;

public static class InvoiceExporter
{
    public static void SaveXml(InvoiceDescriptor desc, string outputPath, ZugferdConfig zugferdConfig)
        => SaveXml(desc, outputPath, zugferdConfig, null);

    public static void SaveXml(InvoiceDescriptor desc, string outputPath, ZugferdConfig zugferdConfig, ICollection<MappingDiagnostic>? diagnostics)
    {
        Log.Debug("XML-Export: Pfad={Path}", outputPath);

        using (var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
        {
            WriteXml(desc, stream, zugferdConfig, diagnostics);
        }

        Log.Debug("XML-Export abgeschlossen: {Bytes} Bytes geschrieben", new FileInfo(outputPath).Length);
    }

    /// <summary>
    /// Gemeinsamer Stream-Kern von CLI und MCP-Probelauf: derselbe Codepfad bis einschließlich <c>desc.Save</c>;
    /// nur das Ziel (Datei oder Speicherstrom) unterscheidet sich.
    /// </summary>
    public static void WriteXml(InvoiceDescriptor desc, Stream stream, ZugferdConfig zugferdConfig, ICollection<MappingDiagnostic>? diagnostics)
    {
        var version = ResolveVersion(zugferdConfig, diagnostics);
        var profile = ResolveProfile(zugferdConfig, diagnostics);

        Log.Debug("XML-Export: Version={Version}, Profile={Profile}", version, profile);

        desc.Save(stream, version, profile);
        stream.Flush();
    }

    public static Task SavePdfAsync(InvoiceDescriptor desc, string outputPath, string pdfTemplatePath, ZugferdConfig zugferdConfig)
        => SavePdfAsync(desc, outputPath, pdfTemplatePath, zugferdConfig, null);

    public static async Task SavePdfAsync(InvoiceDescriptor desc, string outputPath, string pdfTemplatePath, ZugferdConfig zugferdConfig, ICollection<MappingDiagnostic>? diagnostics)
    {
        var version = ResolveVersion(zugferdConfig, diagnostics);
        var profile = ResolveProfile(zugferdConfig, diagnostics);
        var format = ResolveFormat(zugferdConfig, diagnostics);

        Log.Debug("PDF-Export: Version={Version}, Profile={Profile}, Format={Format}", version, profile, format);
        Log.Debug("PDF-Vorlage: {TemplatePath}", pdfTemplatePath);

        await InvoicePdfProcessor.SaveToPdfAsync(outputPath, version, profile, format, pdfTemplatePath, desc);

        Log.Debug("PDF-Export abgeschlossen: {Bytes} Bytes geschrieben", new FileInfo(outputPath).Length);
    }

    /// <summary>Effektive Version; unbekannte Angabe → 2.3 mit Befund <see cref="DiagnosticCodes.ConfigValueFallback"/>.</summary>
    public static ZUGFeRDVersion ResolveVersion(ZugferdConfig zugferdConfig, ICollection<MappingDiagnostic>? diagnostics)
    {
        if (ConfigValues.TryParseVersion(zugferdConfig.Version, out var version))
            return version;

        ReportFallback(diagnostics, "zugferd.version", zugferdConfig.Version, ConfigValues.Version);
        return version;
    }

    /// <summary>Effektives Profil; unbekannte Angabe → Extended mit Befund <see cref="DiagnosticCodes.ConfigValueFallback"/>.</summary>
    public static Profile ResolveProfile(ZugferdConfig zugferdConfig, ICollection<MappingDiagnostic>? diagnostics)
    {
        if (ConfigValues.TryParseProfile(zugferdConfig.Profile, out var profile))
            return profile;

        ReportFallback(diagnostics, "zugferd.profile", zugferdConfig.Profile, ConfigValues.ProfileSet);
        return profile;
    }

    /// <summary>Effektives Format (nur PDF); unbekannte Angabe → CII mit Befund <see cref="DiagnosticCodes.ConfigValueFallback"/>.</summary>
    public static ZUGFeRDFormats ResolveFormat(ZugferdConfig zugferdConfig, ICollection<MappingDiagnostic>? diagnostics)
    {
        if (ConfigValues.TryParseFormat(zugferdConfig.Format, out var format))
            return format;

        ReportFallback(diagnostics, "zugferd.format", zugferdConfig.Format, ConfigValues.Format);
        return format;
    }

    private static void ReportFallback(ICollection<MappingDiagnostic>? diagnostics, string path, string value, ValueSet valueSet)
    {
        var message = $"Unbekannter Wert '{value}' in {path}, verwendet wird {valueSet.InvalidFallback}; zulässig sind {string.Join(", ", valueSet.Canonical)}";
        Log.Warning("{Warning}", message);
        diagnostics?.Add(new MappingDiagnostic(DiagnosticCodes.ConfigValueFallback, path, message));
    }
}
