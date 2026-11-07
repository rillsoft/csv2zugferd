namespace Csv2Zugferd;

/// <summary>
/// Stabile englische Codes für strukturierte Fachbefunde (Warnungen, Rückfälle), getrennt vom Vorrat der
/// Fehlercodes in <see cref="ErrorCodes"/>. Einzige Stelle des Vorrats.
/// </summary>
public static class DiagnosticCodes
{
    /// <summary>Konfiguriertes Feld hat in dieser Betriebsart oder grundsätzlich keine Wirkung.</summary>
    public const string ConfigFieldIgnored = "config_field_ignored";

    /// <summary>Ein in dieser Betriebsart erforderlicher Abschnitt fehlt (z. B. columnPattern im columns-Modus); es entstehen keine Positionen.</summary>
    public const string ConfigFieldMissing = "config_field_missing";

    /// <summary>Unbekannter Konfigurationswert, der Mapper oder Exporter fällt auf den dokumentierten Rückfall zurück.</summary>
    public const string ConfigValueFallback = "config_value_fallback";

    /// <summary>Datumswert nicht lesbar; der Mapper verwendet DateTime.MinValue.</summary>
    public const string InvalidDate = "invalid_date";

    /// <summary>Dezimalwert nicht lesbar; der Mapper verwendet 0.</summary>
    public const string InvalidDecimal = "invalid_decimal";

    /// <summary>Ungültiger Regex in einer Regel; die Regel wird übersprungen.</summary>
    public const string InvalidRegex = "invalid_regex";

    /// <summary>IBAN mit ungültiger Prüfsumme.</summary>
    public const string InvalidIbanChecksum = "invalid_iban_checksum";

    /// <summary>Keine Lieferdaten konfiguriert; das Rechnungsdatum wird als Leistungsdatum (BT-72) verwendet.</summary>
    public const string DeliveryDateDefaulted = "delivery_date_defaulted";

    /// <summary>XRechnung erlaubt den Rechnungstyp (BT-3) nicht (BR-DE-17).</summary>
    public const string XrechnungTypeCodeNotAllowed = "xrechnung_type_code_not_allowed";

    /// <summary>Gutschrift oder Korrektur ohne Vorgängerrechnung (BT-25).</summary>
    public const string PrecedingInvoiceMissing = "preceding_invoice_missing";

    /// <summary>Vorgängerrechnung konfiguriert, Profil Minimum schreibt sie nicht.</summary>
    public const string PrecedingInvoiceNotInMinimum = "preceding_invoice_not_in_minimum";

    /// <summary>XRechnung verlangt Zahlungsanweisungen (BR-DE-1).</summary>
    public const string PaymentMeansMissingXrechnung = "payment_means_missing_xrechnung";

    /// <summary>paymentMeans ohne typeCode wird ignoriert.</summary>
    public const string PaymentMeansWithoutTypeCode = "payment_means_without_type_code";

    /// <summary>directDebit konfiguriert, aber typeCode ist keine Lastschrift.</summary>
    public const string DirectDebitIgnored = "direct_debit_ignored";

    /// <summary>Überweisung ohne IBAN des Verkäufers (BR-61).</summary>
    public const string CreditTransferWithoutIban = "credit_transfer_without_iban";

    /// <summary>SEPA-Lastschrift ohne Gläubiger-ID (BT-90).</summary>
    public const string DirectDebitWithoutCreditorId = "direct_debit_without_creditor_id";

    /// <summary>SEPA-Lastschrift ohne Mandatsreferenz (BT-89).</summary>
    public const string DirectDebitWithoutMandate = "direct_debit_without_mandate";

    /// <summary>SEPA-Lastschrift ohne IBAN des Käufers (BT-91).</summary>
    public const string DirectDebitWithoutBuyerIban = "direct_debit_without_buyer_iban";

    public static readonly IReadOnlyList<string> All =
    [
        ConfigFieldIgnored,
        ConfigFieldMissing,
        ConfigValueFallback,
        InvalidDate,
        InvalidDecimal,
        InvalidRegex,
        InvalidIbanChecksum,
        DeliveryDateDefaulted,
        XrechnungTypeCodeNotAllowed,
        PrecedingInvoiceMissing,
        PrecedingInvoiceNotInMinimum,
        PaymentMeansMissingXrechnung,
        PaymentMeansWithoutTypeCode,
        DirectDebitIgnored,
        CreditTransferWithoutIban,
        DirectDebitWithoutCreditorId,
        DirectDebitWithoutMandate,
        DirectDebitWithoutBuyerIban,
    ];
}
