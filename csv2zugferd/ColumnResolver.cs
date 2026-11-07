using Csv2Zugferd.Models;

namespace Csv2Zugferd;

/// <summary>
/// Spaltenauflösung des columns-Modus, gemeinsam von <see cref="InvoiceMapper"/> und dem MCP-Werkzeug
/// <c>csv2zugferd_config_get</c> verwendet. Reine Namensbildung ohne Datenzugriff; ob ein Zellwert leer ist,
/// entscheidet weiterhin der Mapper.
/// </summary>
public static class ColumnResolver
{
    /// <summary>Standardfragmente, die der Mapper im columns-Modus verwendet, wenn kein Fragment konfiguriert ist.</summary>
    public const string DefaultNameFragment = "Artikel";
    public const string DefaultDescriptionFragment = "Beschreibung";
    public const string DefaultPriceFragment = "Einzelpreis";
    public const string DefaultQuantityFragment = "Menge";

    public static bool IsPrefixStyle(ColumnPatternConfig pattern) =>
        ConfigValues.ParseColumnStyle(pattern.Style) == ConfigValues.ColumnStylePrefix;

    /// <summary>Index als Text, bei <c>zeroPadding</c> &gt; 0 links mit Nullen aufgefüllt.</summary>
    public static string FormatIndex(ColumnPatternConfig pattern, int index) =>
        pattern.ZeroPadding > 0
            ? index.ToString().PadLeft(pattern.ZeroPadding, '0')
            : index.ToString();

    /// <summary>Spaltenname für ein Fragment an einer Position: prefix <c>Pos1_Artikel</c>, suffix <c>Artikel_001</c>.</summary>
    public static string BuildColumnName(ColumnPatternConfig pattern, int index, string fragment)
    {
        var idx = FormatIndex(pattern, index);
        return IsPrefixStyle(pattern)
            ? $"{pattern.Prefix}{idx}{pattern.Separator}{fragment}"
            : $"{fragment}{pattern.Separator}{idx}";
    }

    /// <summary>
    /// Alle konkret möglichen Spaltennamen für ein Fragment im columns-Modus: zuerst der direkte Name
    /// (globale Spalte), dann die indexierten Namen ab <c>startIndex</c> für <paramref name="positions"/> Positionen.
    /// </summary>
    public static IEnumerable<string> EnumerateCandidates(ColumnPatternConfig pattern, string fragment, int positions)
    {
        yield return fragment;
        for (var i = 0; i < positions; i++)
            yield return BuildColumnName(pattern, pattern.StartIndex + i, fragment);
    }
}
