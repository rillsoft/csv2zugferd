namespace Csv2Zugferd;

/// <summary>
/// Strukturierter Fachbefund: stabiler Code aus <see cref="DiagnosticCodes"/>, vollständiger Konfigurationspfad
/// (z. B. <c>mapping.invoice.invoiceDate</c>, bei Listen mit Index) und deutscher Text.
/// </summary>
public sealed record MappingDiagnostic(string Code, string Path, string Message)
{
    public override string ToString() => $"[{Code}] {Path}: {Message}";
}
