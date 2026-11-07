using System.ComponentModel;

namespace Csv2Zugferd.Models;

public class ZugferdConfig
{
    [Description("ZUGFeRD/Factur-X version of the output: 2.3 (recommended), 2.0 or 1.0. Unknown values fall back to 2.3.")]
    public string Version { get; set; } = "2.3";

    [Description("ZUGFeRD profile: Minimum, BasicWL, Basic, Comfort (same as EN16931), Extended or XRechnung. Case-insensitive. Unknown values fall back to Extended.")]
    public string Profile { get; set; } = "Extended";

    [Description("XML syntax embedded in the PDF: CII (UN/CEFACT Cross Industry Invoice, standard for ZUGFeRD) or UBL. Only used by the PDF export; the XML-only export always writes CII. Unknown values fall back to CII.")]
    public string Format { get; set; } = "CII";
}
