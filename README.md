# csv2zugferd Dokumentation

<!-- mcp-name: de.zugferd-tools/csv2zugferd -->

**Website:** [zugferd-tools.de](https://zugferd-tools.de)

`csv2zugferd` ist ein .NET-CLI-Tool, das Rechnungsdaten aus CSV-Dateien liest und daraus ZUGFeRD-konforme XML-Dateien oder PDF-Dateien mit eingebettetem ZUGFeRD-XML erzeugt. Die Struktur der CSV-Datei und das Mapping auf ZUGFeRD-Felder werden über eine YAML-Konfiguration gesteuert.

## Demo-Paket

Das Demo-Paket ist der schnellste Einstieg. Es enthält eine fertige Windows-Exe, zwei Beispiel-Rechnungen und eine vollständige YAML-Konfiguration.

**Download:** [GitHub Releases → Demo-ZIP](https://github.com/rillsoft/csv2zugferd/releases)

| Datei | Beschreibung |
|---|---|
| `csv2zugferd-win-x64.exe` | Windows-64-bit-Exe, self-contained (kein .NET nötig) |
| `config_demo.yml` | YAML-Konfiguration für die Demo-CSV-Struktur |
| `demo-2026-0001.csv` | Beispiel-Rechnung 1 – Softwarelizenzen mit Rabatt |
| `demo-2026-0001.pdf` | PDF-Vorlage zu Rechnung 1 |
| `demo-2026-0002.csv` | Beispiel-Rechnung 2 – Lizenzen, Service und Schulung |
| `demo-2026-0002.pdf` | PDF-Vorlage zu Rechnung 2 |
| `create-zugferd-demo.cmd` | Startskript – per Doppelklick oder CMD ausführen |

**Schnellstart:**

```cmd
:: 1. ZIP entpacken, dann:
create-zugferd-demo.cmd
:: Ergebnis: output\DEMO-2026-0001.pdf und output\DEMO-2026-0002.pdf
```

Die erzeugten Dateien sind valide ZUGFeRD-2.3-Rechnungen (Profil Extended) und können mit dem [Mustang-Validator](https://www.mustangproject.org/validator/) geprüft werden.

Detaillierte Erklärung der `config_demo.yml`: [zugferd-tools.de/docs/schnellstart/](https://zugferd-tools.de/docs/schnellstart/)

## Eingaben und Ausgaben

| Typ | Format | Beschreibung |
|-----|--------|--------------|
| CSV-Datei | `.csv` | Rechnungsdaten mit Kopf-, Käufer-, Verkäufer-, Positions- und Summenfeldern |
| PDF-Vorlage | `.pdf` | Bestehende Rechnungs-PDF, in die das XML eingebettet wird |
| Konfiguration | `.yml` | YAML-Datei mit CSV-Einstellungen und Mapping-Regeln |
| XML-Ausgabe | `.xml` | ZUGFeRD XML nach konfiguriertem Profil |
| PDF-Ausgabe | `.pdf` | ZUGFeRD PDF mit eingebettetem XML |

## CLI-Nutzung

```powershell
dotnet run --project .\csv2zugferd\csv2zugferd.csproj -- --csv .\test\0002486.csv --pdf .\test\2483.pdf --config .\test\config_datasrc.yml --output .\test\output
```

| Option | Kurz | Pflicht | Beschreibung |
|--------|------|---------|--------------|
| `--csv` | `-c` | Ja | Pfad zur CSV-Datei |
| `--pdf` | `-p` | Ja | Pfad zur PDF-Vorlage |
| `--config` | `-g` | Ja | Pfad zur YAML-Konfiguration |
| `--output` | `-o` | Nein | Ausgabeverzeichnis, Standard ist das aktuelle Verzeichnis |
| `--xml-only` | | Nein | Nur XML erzeugen, kein PDF |
| `--verbose` | `-v` | Nein | Debug-Ausgabe aktivieren |

Die Ausgabedateien werden anhand der Rechnungsnummer benannt, zum Beispiel `0002483.xml` und `0002483.pdf`.

## MCP-Server (Einrichtungsassistent)

Seit Version 1.3.0 stellt `csv2zugferd mcp` das Tool als MCP-Server (Model Context Protocol) über stdio bereit. Zweck ist die **Einrichtung**: Ein KI-Assistent in Claude Desktop, Claude Code oder VS Code liest den Kopf einer CSV-Datei, entwirft die YAML-Konfiguration, prüft sie im Probelauf gegen den echten Mapping-Code und speichert sie als Datei. Der **Produktivlauf bleibt bei der CLI**: lokal, deterministisch, ohne Modell und ohne Cloud-Upload durch csv2zugferd. Die gespeicherte Konfiguration gilt für alle weiteren Rechnungen derselben CSV-Struktur.

Vorbereitung: Für die Einrichtung mit einem KI-Assistenten anonymisierte Beispieldaten verwenden. Die Werkzeuge liefern Zellwerte nur auf Anforderung (`sampleRows`, `includeLineItems`, `includeParties`).

### Start und Client-Konfiguration

Der Server wird vom MCP-Client gestartet und liest JSON-RPC von stdin; stdout trägt ausschließlich JSON-RPC, Logs gehen nach `logs/zugferd-*.log` im Arbeitsverzeichnis und Warnungen zusätzlich nach stderr. `--verbose` schaltet die Logdatei auf Debug.

Zwei Wege zum Start: die Windows-Exe aus dem [GitHub-Release](https://github.com/rillsoft/csv2zugferd/releases) (`csv2zugferd.exe mcp`, kein .NET nötig) oder das .NET-Tool-Paket `csv2zugferd` von NuGet, das MCP-Clients mit `dnx csv2zugferd@1.3.0 mcp` starten (benötigt das .NET-10-SDK; läuft unter Windows, Linux und macOS). Mit dem Release 1.3.0 erscheint der Server in der [Official MCP Registry](https://registry.modelcontextprotocol.io/) unter dem Namen `de.zugferd-tools/csv2zugferd`.

Claude Desktop (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "csv2zugferd": {
      "command": "C:\\Tools\\csv2zugferd.exe",
      "args": ["mcp"]
    }
  }
}
```

Claude Desktop mit dem NuGet-Paket statt der Exe:

```json
{
  "mcpServers": {
    "csv2zugferd": {
      "command": "dnx",
      "args": ["csv2zugferd@1.3.0", "mcp"]
    }
  }
}
```

Claude Code:

```powershell
claude mcp add csv2zugferd -- C:\Tools\csv2zugferd.exe mcp
# oder mit dem NuGet-Paket:
claude mcp add csv2zugferd -- dnx csv2zugferd@1.3.0 mcp
```

VS Code (`.vscode/mcp.json`):

```json
{
  "servers": {
    "csv2zugferd": {
      "type": "stdio",
      "command": "C:\\Tools\\csv2zugferd.exe",
      "args": ["mcp"]
    }
  }
}
```

### Werkzeuge

| Werkzeug | Zweck | Wirkung |
|---|---|---|
| `csv2zugferd_csv_header_get` | Spaltennamen und Zeilenzahl einer CSV lesen, optional bis zu 3 Beispielzeilen (`sampleRows`) | lesend |
| `csv2zugferd_config_schema_get` | **Vollständige Konfigurationsreferenz**: JSON-Schema aller YAML-Schlüssel mit Beschreibung, Typ, Default und zulässigen Werten plus Semantik je Schlüssel (Wirkung, Modusabhängigkeit, Auflösungsreihenfolge, Rückfall). `section` liefert einen Teilbaum, z. B. `mapping.lineItems` | lesend |
| `csv2zugferd_config_get` | Konfiguration (Pfad oder YAML-Text) strukturell prüfen: effektive Einstellungen, gemappte Felder mit Quellen, unbekannte Schlüssel, unzulässige Werte mit Rückfall, ignorierte oder fehlende Abschnitte; mit `csvPath` Spaltenabgleich (fehlende und ungenutzte Spalten) | lesend |
| `csv2zugferd_invoice_get` | Probelauf mit dem echten Mapping ohne Ausgabedatei: Rechnungsnummer, Typ, Summen, Steueraufschlüsselung, Warnungen mit Code und Pfad, XML-Größe; optional Positionen, Parteien und PDF-Probelauf in eine temporäre Datei (`pdfTemplatePath`) | lesend |
| `csv2zugferd_config_file_create` | Geprüfte YAML als Datei speichern (UTF-8 ohne BOM); vorhandene Datei nur mit `overwrite: true`; liefert den CLI-Befehl für den Produktivlauf | schreibend |

Die Werkzeuge nutzen denselben Code wie die CLI. `csv2zugferd_config_schema_get` ersetzt das Lesen dieser README: Alle Konfigurationsmöglichkeiten sind allein über die Werkzeuge abrufbar und prüfbar.

Ressourcen (`resources/list`): `csv2zugferd://docs/config-reference` (diese README), `csv2zugferd://examples/config-rows` und `csv2zugferd://examples/config-columns-suffix` (Beispielkonfigurationen aus `testdata/`).

### Ablauf der Inbetriebnahme

1. **CSV-Struktur erfassen:** `csv2zugferd_csv_header_get` mit dem Pfad der (anonymisierten) CSV, bei Bedarf `sampleRows: 2`.
2. **Mapping erarbeiten:** `csv2zugferd_config_schema_get` liefert Schlüssel, Werte und Wirkung; der Assistent entwirft die YAML.
3. **Prüfen und korrigieren:** `csv2zugferd_config_get` mit `configYaml` und `csvPath` meldet Tippfehler (`unknownKeys`), unzulässige Werte (`invalidValues`) und fehlende Spalten (`columnsMissing`); `csv2zugferd_invoice_get` zeigt Summen, Steuergruppen und Warnungen des echten Mappings. Wiederholen, bis das Ergebnis stimmt.
4. **Speichern:** `csv2zugferd_config_file_create` schreibt die YAML und gibt den CLI-Befehl zurück.
5. **Produktivlauf:** Den Befehl mit PDF-Vorlage und Ausgabeverzeichnis ausführen, z. B. `csv2zugferd --csv rechnung.csv --pdf vorlage.pdf --config config.yml --output out`. Dafür ist weder ein Modell noch ein laufender MCP-Server nötig.

### Antworten, Fehler und Grenzen

- Erfolgreiche Antworten kommen als `structuredContent` und als inhaltsgleicher JSON-Text; jedes Werkzeug veröffentlicht ein `outputSchema`, gegen das die Antwort vor dem Senden geprüft wird.
- Warnungen sind Objekte `{code, path, message}` mit stabilem englischem Code (z. B. `invalid_date`, `config_field_ignored`, `invalid_iban_checksum`), vollständigem Konfigurationspfad und deutschem Text.
- Fachliche Ablehnungen sind Tool-Ergebnisse mit `isError: true`; die erste Textzeile ist der Code (z. B. `csv_file_not_found`, `config_invalid_yaml`, `file_exists`), `_meta.errorCode` trägt ihn zusätzlich. Unbekannte Werkzeuge und Schemaverletzungen sind JSON-RPC-Fehler `-32602` mit Feldpfad.
- Lesende Werkzeuge liefern keine Antwort über 900 000 Byte; darüber kommt `response_too_large` mit den Eingrenzungsargumenten des Werkzeugs (`sampleRows: 0`, `includeLineItems: false`, `section`).

## YAML-Grundstruktur

```yaml
csv:
  separator: ","
  encoding: "UTF-8"
  hasHeader: true
  decimalSeparator: "."
  dateFormat: "dd.MM.yyyy"

zugferd:
  version: "2.3"
  profile: "Extended"
  format: "CII"

mapping:
  invoice: {}
  buyer: {}
  seller: {}
  delivery: {}
  orderReference: {}
  lineItems: {}
  totals: {}
  paymentTerms: {}
  paymentMeans: {}
```

Jedes Mapping-Feld kann entweder einen CSV-Spaltennamen oder einen festen Wert verwenden:

```yaml
invoiceNumber:
  column: "INVOICE_NO"

currency:
  value: "EUR"
```

`value` ist sinnvoll für Stammdaten oder Standards, die nicht in jeder CSV wiederholt werden sollen. `column` liest den Wert aus der CSV-Zeile.

Zusätzlich kann ein Mapping-Feld einen Fallback und Regex-Regeln definieren:

```yaml
unitCode:
  default: "C62"
  rules:
    - when:
        column: "PRODUCT_CODE"
        regex: "^002"
      value: "ANN"
    - when:
        column: "PRODUCT_CODE"
        regex: "^003"
      value: "DAY"
    - when:
        column: "PRODUCT_NAME"
        regex: "(?i)vor-ort-schulung|schulung"
      value: "DAY"
```

Die Reihenfolge ist: `value`, `column`, `rules`, `default`. Im `columns`-Modus werden Regelspalten positionsbezogen aufgelöst, also z. B. `PRODUCT_CODE_0`, `PRODUCT_CODE_1` usw.

## Feste Verkäuferdaten

Verkäuferdaten können vollständig in der YAML-Konfiguration festgelegt werden. Das ist der empfohlene Weg, wenn alle Rechnungen vom gleichen Absender erzeugt werden.

```yaml
seller:
  name:
    value: "Muster Lieferant GmbH"
  id:
    value: "1234567890"
  postalCode:
    value: "80333"
  city:
    value: "Muenchen"
  street:
    value: "Lieferantenstrasse 20"
  country:
    value: "DE"
  taxRegistrations:
    - value: "201/113/40209"
      scheme: "FC"
    - value: "DE123456789"
      scheme: "VA"
  electronicAddress:
    value: "DE123456789"
    scheme: "GermanyVatNumber"
```

`id` ist optional und trägt die Verkäuferkennung (BT-29), zum Beispiel die Lieferantennummer, unter der der Käufer den Verkäufer führt. Sie erscheint im XML als `ram:ID` der `SellerTradeParty`. Ohne `id` wird kein Element geschrieben.

Unterstützte Steuer-Schemata:

| Scheme | Bedeutung |
|--------|-----------|
| `FC` | Steuernummer |
| `VA` | Umsatzsteuer-ID |

`taxRegistrations` unterstützt sowohl feste Werte per `value` als auch CSV-Werte per `column`.

## Rechnungskopf

```yaml
invoice:
  invoiceNumber:
    column: "INVOICE_NO"
  invoiceDate:
    column: "INVOICE_DUEDATE"
  typeCode:                     # Rechnungstyp (BT-3), ohne Angabe 380
    value: "380"
  currency:
    value: "EUR"
  orderReferenceId:
    column: "SALESORDER_ID"
  referenceOrderNo:
    column: "SALESORDER_ID"
  name:
    value: "WARENRECHNUNG"
  note:
    column: "INVOICE_NOTE"
```

Pflichtnah sind Rechnungsnummer, Rechnungsdatum und Währung. Wenn die CSV kein echtes Rechnungsdatum enthält, muss eine passende Ersatzspalte bewusst gewählt werden.

### Rechnungstyp, Storno und Gutschrift

`typeCode` setzt den Rechnungstyp (BT-3, `rsm:ExchangedDocument/ram:TypeCode`). Ohne Angabe wird `380` geschrieben. Zulässig ist eine Zahl nach UNTDID 1001 oder der Name aus der ZUGFeRD-Bibliothek (`Invoice`, `CreditNote`, `Correction`, `SelfBilledInvoice`, …); ein unbekannter Wert führt zu einem Fehler.

| Code | Bedeutung | Einsatz |
|------|-----------|---------|
| `380` | Rechnung | Standard |
| `381` | Gutschrift (kaufmännisch), Stornorechnung | vollständiges Storno, Rückerstattung |
| `384` | Rechnungskorrektur | Teilkorrektur einer bestehenden Rechnung (Preis, Menge, Steuersatz) |
| `389` | Selbstfakturierung (Gutschriftsverfahren, § 14 Abs. 2 UStG) | der Leistungsempfänger rechnet ab; Aussteller und Verkäufer sind vertauscht |
| `326`, `386`, `875`–`877` | Teil-, Vorauszahlungs- und Bau-Abschlagsrechnungen | werden durchgereicht |

Ein Storno ist fachlich keine Gutschrift im Sinne des Gutschriftsverfahrens. Wer „Gutschrift“ meint und selbst der Leistende ist, braucht `381` oder `384`, nicht `389`.

Rechnung und Storno lassen sich aus einer CSV-Spalte steuern:

```yaml
invoice:
  typeCode:
    default: "380"
    rules:
      - when:
          column: "Belegart"
          regex: "(?i)^(gutschrift|storno)"
        value: "381"
      - when:
          column: "Belegart"
          regex: "(?i)^korrektur"
        value: "384"
  precedingInvoices:            # Vorgängerrechnung (BT-25/BT-26), eine oder mehrere
    - number:
        column: "Ursprungsrechnung"
      date:
        column: "Ursprungsdatum"
```

Liefert die CSV keine Belegart, kann die Regel auch auf das Vorzeichen einer Betragsspalte prüfen, zum Beispiel `regex: "^-"` auf `INVOICE_TOTAL`.

`precedingInvoices` erzeugt je Eintrag ein `ram:InvoiceReferencedDocument` mit Nummer und Datum der Ursprungsrechnung. Einträge ohne Nummer werden übersprungen; das Datum ist optional. Mehrere Einträge sind für Sammelgutschriften oder Schlussrechnungen nach mehreren Abschlägen gedacht. Das Element wird ab Profil `BasicWL` geschrieben, nicht in `Minimum`.

Vorzeichen der Beträge:

| Typ | Beträge | Hinweis |
|-----|---------|---------|
| `381` | positiv | Das Minus steckt im Code. Negative Beträge bei `381` werden von Validatoren und Empfängern abgelehnt. |
| `380` mit negativen Beträgen | negativ | „Negativrechnung“, laut Factur-X zulässig; manche Empfänger buchen sie als normale Rechnung. |
| `384` | positiv oder negativ je Position | Teilkorrektur, Mengen dürfen negativ sein. |

Das Tool schreibt die Beträge so, wie die CSV sie liefert. Empfohlen ist `381` mit positiven Beträgen und `precedingInvoices`.

Weitere Hinweise:

- `name` sollte bei Storno und Korrektur auf `STORNORECHNUNG` bzw. `RECHNUNGSKORREKTUR` statt `GUTSCHRIFT` stehen. Das Element wird nur im Profil `Extended` geschrieben.
- Ein Storno bekommt eine eigene Rechnungsnummer; das Original wird nie überschrieben (GoBD).
- Der sichtbare PDF-Teil muss zum XML-Typ passen (kein PDF „Gutschrift“ zu einem XML mit `380`).
- Chorus Pro (Frankreich) akzeptiert nur `380`.

Unvollständige Angaben erzeugen eine Warnung im Log, keinen Abbruch:

- `381` oder `384` ohne `precedingInvoices` (bei `XRechnung` und `384`: BR-DE-26)
- Profil `XRechnung` mit einem Code außerhalb von `326`, `380`, `381`, `384`, `389`, `875`, `876`, `877` (BR-DE-17)
- Profil `Minimum` mit `precedingInvoices`

Eine lauffähige Beispielkonfiguration ist `testdata/config_creditnote.yml` mit `testdata/test_creditnote.csv`.

## Käuferdaten

```yaml
buyer:
  name:
    column: "ACCOUNT_NAME"
  id:
    column: "ACCOUNT_ID"
  postalCode:
    column: "INVOICE_BILL_CODE"
  city:
    column: "INVOICE_BILL_CITY"
  street:
    column: "INVOICE_BILL_STREET"
  country:
    value: "DE"
  contact:
    column: "CONTACT_LASTNAME"
  taxId:
    column: "BUYER_VAT_ID"
  taxScheme:
    value: "VA"
  electronicAddress:
    column: "BUYER_VAT_ID"
    scheme: "GermanyVatNumber"
```

`id` ist optional und trägt die Käuferkennung (BT-46), in der Regel die Kundennummer des Käufers beim Verkäufer. Sie erscheint im XML als `ram:ID` der `BuyerTradeParty`. Ohne `id` wird kein Element geschrieben. Wie alle Felder unterstützt `id` auch `value`, `default` und `rules`.

Für Länder sollte ein ISO-3166-Alpha-2-Code wie `DE` verwendet werden. Freitext wie `Deutschland` wird nicht zuverlässig als ZUGFeRD-Ländercode erkannt.

`taxScheme` akzeptiert `FC` (Steuernummer) oder `VA` (Umsatzsteuer-ID).

Unterstützte Werte für `scheme` bei `electronicAddress`:

| Scheme | Bedeutung |
|--------|-----------|
| `GermanyVatNumber` | Deutsche Umsatzsteuer-ID (EAS 9930) |
| `LuxemburgVatNumber` | Luxemburgische Umsatzsteuer-ID (EAS 9938) |
| `ElectronicMailSmtp` oder `EM` | E-Mail-Adresse (EAS `EM`) |

Andere Werte fallen auf `GermanyVatNumber` zurück. Die vollständige Liste der Kennungen steht in [ZUGFeRD-csharp](https://github.com/stephanstapel/ZUGFeRD-csharp/blob/master/ZUGFeRD/ElectronicAddressSchemeIdentifiers.cs); weitere Werte müssen erst im Mapper ergänzt werden.

## Lieferdaten

```yaml
delivery:
  deliveryNoteNumber:
    column: "DELIVERY_NOTE_NO"
  deliveryNoteDate:
    column: "DELIVERY_NOTE_DATE"
  actualDeliveryDate:
    column: "DELIVERY_DATE"
```

Alle drei Felder sind optional. `deliveryNoteNumber` und `deliveryNoteDate` werden gemeinsam als Lieferscheinreferenz geschrieben. `actualDeliveryDate` setzt das tatsächliche Lieferdatum (BT-72).

Sind weder Lieferschein noch Lieferdatum konfiguriert, wird das Rechnungsdatum als Leistungsdatum geschrieben und eine Warnung protokolliert. Der Block `ApplicableHeaderTradeDelivery` ist im ZUGFeRD-Schema Pflicht; ohne Inhalt würde die XML-Datei die Validierung nicht bestehen.

## Bestellreferenz

```yaml
orderReference:
  orderNumber:
    column: "ORDER_NO"
  orderDate:
    column: "ORDER_DATE"
```

`orderNumber` entspricht `BT-13` (Bestellnummer des Käufers). `orderDate` ist optional.

## Positionsdaten

Das Tool unterstützt zwei CSV-Strukturen. Alle Positionsfelder sind `FieldMapping`-Einträge; neben den Beispielen unten gibt es `sellerAssignedId` (Artikelnummer des Verkäufers, BT-155) und `buyerAssignedId` (Artikelnummer des Käufers, BT-156).

### Modus `rows`

Im `rows`-Modus entspricht jede CSV-Zeile einer Rechnungsposition. Kopfdaten werden aus der ersten Zeile gelesen.

```yaml
lineItems:
  mode: "rows"
  fields:
    name:
      column: "Artikel_Name"
    description:
      column: "Artikel_Beschreibung"
    netUnitPrice:
      column: "Einzelpreis_Netto"
    billedQuantity:
      column: "Menge"
    taxPercent:
      column: "MwSt_Prozent"
    unitCode:
      value: "C62"
    taxType:
      value: "VAT"
    taxCategoryCode:
      value: "S"
  allowanceCharge:
    enabled: true
    isDiscount:
      value: true
    chargePercentage:
      column: "Rabatt_Prozent"
    reason:
      column: "Rabatt_Grund"
```

`allowanceCharge` ist optional. `isDiscount: true` erzeugt einen Positionsrabatt (BG-27), `false` einen Positionszuschlag (BG-28), jeweils als `SpecifiedTradeAllowanceCharge` in der Positionsabrechnung mit Basisbetrag, Prozentsatz und Grund. Der Zeilennettobetrag (BT-131) ist bereits um den Rabatt gemindert bzw. um den Zuschlag erhöht; die Summen auf Dokumentebene folgen daraus. `chargePercentage` akzeptiert Werte wie `10`, `10.00` oder `10%`. Im `columns`-Modus wird Rabatt stattdessen über `discountPercent` in `fields` konfiguriert.

### Modus `columns`

Im `columns`-Modus steht eine ganze Rechnung in einer CSV-Zeile. Positionen werden über nummerierte Spalten erkannt.

Suffix-Beispiel:

```csv
PRODUCT_NAME_0,PRODUCT_QUANTITY_0,PRODUCT_PRICE_0,PRODUCT_NAME_1,PRODUCT_QUANTITY_1,PRODUCT_PRICE_1
Rillsoft Cloud Standard,1,1440.00,,,
```

Passende YAML:

```yaml
lineItems:
  mode: "columns"
  columnPattern:
    style: "suffix"
    separator: "_"
    startIndex: 0
    zeroPadding: 0
  fields:
    name:
      column: "PRODUCT_NAME"
    sellerAssignedId:
      column: "PRODUCT_CODE"
    netUnitPrice:
      column: "PRODUCT_PRICE"
    billedQuantity:
      column: "PRODUCT_QUANTITY"
    taxPercent:
      column: "INVOICE_TAX"
    discountPercent:
      column: "PRODUCT_DISCOUNT"
  fixedFields:
    unitCode:
      default: "C62"
      rules:
        - when:
            column: "PRODUCT_CODE"
            regex: "^002"
          value: "ANN"
        - when:
            column: "PRODUCT_CODE"
            regex: "^003"
          value: "DAY"
        - when:
            column: "PRODUCT_NAME"
            regex: "(?i)vor-ort-schulung|schulung"
          value: "DAY"
    taxType:
      value: "VAT"
    taxCategoryCode:
      value: "S"
```

Im `columns`-Modus kann ein Feld entweder als nummeriertes Positionsfeld gelesen werden, zum Beispiel `PRODUCT_NAME_0`, oder als globale Einzelspalte, zum Beispiel `INVOICE_TAX`.

Bei `style: "prefix"` steht der Index vor dem Feldnamen; `prefix` ist der Text vor dem Index, `separator` trennt Index und Feldname: `prefix: "Pos"`, `separator: "_"` und `startIndex: 1` ergeben `Pos1_Artikel`, `Pos2_Artikel` usw. (Beispiel: `testdata/config_columns_prefix.yml`). Bei `style: "suffix"` wird `prefix` nicht verwendet; `zeroPadding: 3` erzeugt `Artikel_001`.

`PRODUCT_CODE` ist hier eine positionsbezogene Verkäufer-Artikelnummer und wird deshalb als `sellerAssignedId` gemappt. Es sollte nicht als `description` verwendet werden.

`discountPercent` akzeptiert Werte wie `10`, `10.00` oder `10%`. Der Rabatt wird als rabattierter Netto-Einzelpreis geschrieben. Ein Listenpreis bleibt optional als `GrossPriceProductTradePrice` erhalten; es wird kein `AppliedTradeAllowanceCharge` für Positionsrabatte erzeugt.

### Wirkung je Modus, Auflösung und Rückfälle

Jedes Mapping-Feld wird in der Reihenfolge `value`, `column`, `rules`, `default` aufgelöst; der erste nicht leere Treffer gewinnt. Einige Schlüssel wirken nur in einer Betriebsart:

| Schlüssel | Wirkt nur bei |
|---|---|
| `lineItems.columnPattern`, `lineItems.fixedFields` | `lineItems.mode: columns` |
| `lineItems.fields.unitCode`, `taxType`, `taxCategoryCode`, `lineItems.allowanceCharge` | `lineItems.mode: rows` (im columns-Modus gelten `fixedFields`) |
| `totals.lineTotalAmount` … `totals.totalPrepaidAmount` | `totals.mode: manual` |
| `paymentMeans.directDebit` | `typeCode` 59 oder 49 |

Zwei Schlüssel werden aus Kompatibilitätsgründen akzeptiert, haben aber **keine Wirkung**: `lineItems.columnsFields` (Positionsfelder gehören nach `lineItems.fields`) und `lineItems.fields.discountReason` (ein Rabattgrund wird nur im rows-Modus über `lineItems.allowanceCharge.reason` geschrieben). Die CLI protokolliert ihre Verwendung als Warnung, `csv2zugferd_config_get` meldet sie als `config_field_ignored`.

Unbekannte Werte brechen die Verarbeitung nicht ab, sondern fallen mit Warnung zurück: `zugferd.version` → `2.3`, `zugferd.profile` → `Extended`, `zugferd.format` → `CII`, `lineItems.mode` → `rows`, `columnPattern.style` → `suffix`, `totals.mode` → `manual`, Währung → `EUR`, Land → `DE`, Einheit → `C62`, Steuerart → `VAT`, Steuerkategorie → `S`, Steuernummernschema → `VA`, `electronicAddress.scheme` → `GermanyVatNumber`. Ein unbekannter `invoice.typeCode` oder `paymentMeans.typeCode` ist dagegen ein Fehler. Unbekannte YAML-Schlüssel (Tippfehler) werden beim Laden ignoriert; `csv2zugferd_config_get` listet sie unter `unknownKeys`. Der Spaltenabgleich von `csv2zugferd_config_get` ist strukturell: Er vergleicht konfigurierte Spaltennamen (im columns-Modus einschließlich der indexierten Namen) mit dem CSV-Kopf, liest aber keine Zellwerte; ob ein Wert leer ist oder eine Regel greift, zeigt erst `csv2zugferd_invoice_get`.

## Summen

Summen können automatisch berechnet oder manuell aus der CSV gelesen werden.

Automatisch:

```yaml
totals:
  mode: "auto"
```

Manuell:

```yaml
totals:
  mode: "manual"
  lineTotalAmount:
    column: "INVOICE_SUBTOTAL"
  allowanceTotalAmount:
    value: "0"
  taxBasisAmount:
    column: "INVOICE_SUBTOTAL"
  taxTotalAmount:
    column: "INVOICE_TAXTOTAL"
  grandTotalAmount:
    column: "INVOICE_TOTAL"
  totalPrepaidAmount:
    value: "0"
```

Im manuellen Modus werden nur die Rechnungssummen aus der CSV übernommen. Die Steueraufschlüsselung je Steuersatz (BG-23) wird weiterhin aus den Positionen abgeleitet: Die Steuerbasis je Satz ist die Summe der Positionsnettobeträge, der Steuerbetrag je Satz wird daraus berechnet. Eine Differenz zwischen der berechneten Steuersumme und `taxTotalAmount` wird der letzten Steuergruppe zugeschlagen, damit die Summe der Gruppen exakt `taxTotalAmount` ergibt (BR-CO-14). Bei nur einem Steuersatz entspricht der Steuerbetrag der Gruppe damit genau dem CSV-Wert. Rundungsabweichungen im Cent-Bereich zwischen Steuerbasis × Steuersatz und dem übernommenen Steuerbetrag sind zulässig (BR-CO-17 sieht eine Toleranz vor). Zeilennettobeträge (BT-131) werden in beiden Modi aus Einzelpreis × Menge berechnet und kaufmännisch auf zwei Nachkommastellen gerundet; alle Kopfsummen und Steuerbasen summieren diese gerundeten Werte (BR-CO-10, BR-S-08). Im manuellen Modus muss `lineTotalAmount` der Summe der gerundeten Zeilennettobeträge entsprechen.

Für Rabatt-Szenarien mit Positionsdaten ist `auto` bevorzugt, weil `BT-131`, Steuerbasis und Gesamtsummen dann konsistent aus den Positionen berechnet werden.

## Zahlungsbedingungen

```yaml
paymentTerms:
  description:
    value: "Zahlbar ohne Abzug bis zum Faelligkeitsdatum"
  dueDate:
    column: "INVOICE_DUEDATE"
```

Eine Beschreibung ist optional, aber in echten Rechnungen meistens sinnvoll.

## Zahlungsmittel und Bankverbindung

Der Abschnitt `paymentMeans` erzeugt die Zahlungsanweisungen nach EN 16931 (BG-16) inklusive Bankverbindung des Verkäufers und SEPA-Lastschriftdaten. Alle Felder unterstützen `column`, `value`, `default` und `rules`.

### Überweisung

```yaml
paymentMeans:
  typeCode:
    value: "58"                 # SEPA-Überweisung
  information:
    value: "SEPA-Überweisung"
  paymentReference:             # Verwendungszweck (BT-83)
    column: "INVOICE_NO"
  sellerAccounts:               # ein oder mehrere Konten des Verkäufers
    - iban:
        value: "DE02 1203 0000 0000 2020 51"
      bic:
        value: "BYLADEM1001"
      name:
        value: "Muster GmbH"
```

### SEPA-Lastschrift

```yaml
paymentMeans:
  typeCode:
    value: "59"                 # SEPA-Lastschrift
  paymentReference:
    column: "INVOICE_NO"
  directDebit:
    creditorId:                 # Gläubiger-ID (BT-90)
      value: "DE98ZZZ09999999999"
    mandateReference:           # Mandatsreferenz (BT-89)
      column: "MANDATE_ID"
    buyerIban:                  # zu belastendes Konto des Käufers (BT-91)
      column: "CUSTOMER_IBAN"
    buyerBic:
      column: "CUSTOMER_BIC"
```

Überweisung und Lastschrift lassen sich pro Rechnung über eine Regel umschalten:

```yaml
typeCode:
  default: "58"
  rules:
    - when:
        column: "PAYMENT_METHOD"
        regex: "(?i)^lastschrift$"
      value: "59"
```

`directDebit` wird nur bei `typeCode` 59 oder 49 ausgewertet.

| Feld | EN 16931 | Hinweis |
|------|----------|---------|
| `typeCode` | BT-81 | Zahl nach UNTDID 4461 (`30`, `58`, `59`, …) oder Name (`SEPACreditTransfer`, `SEPADirectDebit`, …) |
| `information` | BT-82 | Freitext zur Zahlungsart |
| `paymentReference` | BT-83 | Verwendungszweck |
| `sellerAccounts[].iban` | BT-84 | Leerzeichen werden entfernt |
| `sellerAccounts[].name` | BT-85 | Kontoinhaber |
| `sellerAccounts[].bic` | BT-86 | |
| `directDebit.mandateReference` | BT-89 | |
| `directDebit.creditorId` | BT-90 | |
| `directDebit.buyerIban` | BT-91 | wird im Log maskiert |

| Code | Bedeutung |
|------|-----------|
| `30` | Überweisung (nicht SEPA) |
| `58` | SEPA-Überweisung |
| `59` | SEPA-Lastschrift |
| `49` | Lastschrift (nicht SEPA) |

Unvollständige Angaben brechen die Verarbeitung nicht ab, sondern erzeugen eine Warnung im Log:

- Überweisung (`30`, `58`) ohne IBAN des Verkäufers (BR-61)
- SEPA-Lastschrift ohne Gläubiger-ID, Mandatsreferenz oder IBAN des Käufers
- Profil `XRechnung` ohne `paymentMeans` (BR-DE-1)
- IBAN mit ungültiger Prüfsumme

Ein unbekannter `typeCode` führt dagegen zu einem Fehler.

## Konfiguration für `test\0002486.csv`

Die Datei `test\0002486.csv` verwendet:

| Eigenschaft | Wert |
|-------------|------|
| Trennzeichen | Komma |
| Dezimaltrennzeichen | Punkt |
| Datumsformat | `dd.MM.yyyy` |
| Positionsmodus | `columns` |
| Positionsmuster | Suffix, Startindex `0`, zum Beispiel `PRODUCT_NAME_0` |
| Steuer | globale Spalte `INVOICE_TAX` |
| Produktcode | positionsbezogen, zum Beispiel `PRODUCT_CODE_0` |
| Rabatt | positionsbezogen, zum Beispiel `PRODUCT_DISCOUNT_0` |
| Summen | automatisch aus Positionen berechnet |

Die passende Konfiguration liegt unter `test\config_datasrc.yml`.

## Verarbeitung

1. YAML-Konfiguration laden.
2. CSV mit Separator, Encoding, Header- und Zahlenformat lesen.
3. Rechnungskopf, Käufer, Verkäufer, Lieferung und Bestellung aus erster Zeile oder festen Werten mappen.
4. Positionen je nach Modus aus Zeilen oder nummerierten Spalten erzeugen.
5. Summen automatisch berechnen oder aus CSV übernehmen.
6. `InvoiceDescriptor` als ZUGFeRD-XML speichern.
7. Optional das XML in eine PDF-Vorlage einbetten.

## Externe Validierung mit Mustang-CLI

Die Unit-Tests prüfen Mapping und Summen. Ob das erzeugte XML den ZUGFeRD-Regeln entspricht (Schema, EN16931- und XRechnung-Schematron), prüft zusätzlich das [Mustang-CLI](https://www.mustangproject.org/commandline/). Diese Tests laufen nicht bei `dotnet test`, sondern nur auf ausdrückliche Anforderung, weil sie Java 17 und das Mustang-Jar benötigen.

```powershell
# einmalig: Jar nach tools/Mustang-CLI.jar laden (nicht im Git)
.\tools\Get-MustangCli.ps1

# alle Testdaten-Konfigurationen exportieren und validieren
dotnet test --filter Category=ExternalValidation
```

Alternativ kann der Jar-Pfad über die Umgebungsvariable `MUSTANG_CLI_JAR` gesetzt werden. Fehlt das Jar, melden die Tests `Inconclusive` statt eines Fehlers. Für eine manuelle Gegenprobe einzelner Dateien eignet sich der Browser-Upload auf [e-rechnungs-checker.de](https://e-rechnungs-checker.de/).

## Hinweise

- Paketversionen werden zentral in `Directory.Packages.props` verwaltet.
- Das Target Framework steht zentral in `Directory.Build.props`.
- Markdown-Dateien im Repository sollen UTF-8 mit BOM verwenden.
- `country` sollte als ISO-Code gesetzt werden, zum Beispiel `DE`.
- Verkäufer-Stammdaten gehören in die YAML-Konfiguration, wenn sie für alle Rechnungen identisch sind.

## Haftungsausschluss

> `csv2zugferd` wird **ohne Gewähr** bereitgestellt. Die erzeugten XML- und PDF-Dateien müssen vor produktiver Nutzung fachlich und technisch geprüft werden. Das Tool ersetzt keine steuerliche, rechtliche oder fachliche Beratung.

Für die Richtigkeit, Vollständigkeit und steuerliche Bewertung der Eingabe- und Ausgabedaten bleibt der Anwender verantwortlich. Die Bereitstellung erfolgt zu den Bedingungen der Lizenz [Apache-2.0](https://www.apache.org/licenses/LICENSE-2.0), die einen Gewährleistungs- und Haftungsausschluss enthält.
