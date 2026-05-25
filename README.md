# csv2zugferd Dokumentation

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

## Käuferdaten

```yaml
buyer:
  name:
    column: "ACCOUNT_NAME"
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

Für Länder sollte ein ISO-3166-Alpha-2-Code wie `DE` verwendet werden. Freitext wie `Deutschland` wird nicht zuverlässig als ZUGFeRD-Ländercode erkannt.

`taxScheme` akzeptiert `FC` (Steuernummer) oder `VA` (Umsatzsteuer-ID). Unterstützte Werte für `scheme` bei `electronicAddress`: `GermanyVatNumber`, `LuxemburgVatNumber`.

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

Alle drei Felder sind optional. `deliveryNoteNumber` und `deliveryNoteDate` werden gemeinsam als Lieferscheinreferenz geschrieben. `actualDeliveryDate` setzt das tatsächliche Lieferdatum.

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

Das Tool unterstützt zwei CSV-Strukturen.

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

`allowanceCharge` ist optional. `isDiscount: true` erzeugt einen `TradeAllowance` (Rabatt), `false` einen `TradeCharge` (Zuschlag). `chargePercentage` akzeptiert Werte wie `10`, `10.00` oder `10%`. Im `columns`-Modus wird Rabatt stattdessen über `discountPercent` in `fields` konfiguriert.

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

`PRODUCT_CODE` ist hier eine positionsbezogene Verkäufer-Artikelnummer und wird deshalb als `sellerAssignedId` gemappt. Es sollte nicht als `description` verwendet werden.

`discountPercent` akzeptiert Werte wie `10`, `10.00` oder `10%`. Der Rabatt wird als rabattierter Netto-Einzelpreis geschrieben. Ein Listenpreis bleibt optional als `GrossPriceProductTradePrice` erhalten; es wird kein `AppliedTradeAllowanceCharge` für Positionsrabatte erzeugt.

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

## Hinweise

- Paketversionen werden zentral in `Directory.Packages.props` verwaltet.
- Das Target Framework steht zentral in `Directory.Build.props`.
- Markdown-Dateien im Repository sollen UTF-8 mit BOM verwenden.
- `country` sollte als ISO-Code gesetzt werden, zum Beispiel `DE`.
- Verkäufer-Stammdaten gehören in die YAML-Konfiguration, wenn sie für alle Rechnungen identisch sind.

## Haftungsausschluss

> `csv2zugferd` wird **ohne Gewähr** bereitgestellt. Die erzeugten XML- und PDF-Dateien müssen vor produktiver Nutzung fachlich und technisch geprüft werden. Das Tool ersetzt keine steuerliche, rechtliche oder fachliche Beratung.

Für die Richtigkeit, Vollständigkeit und steuerliche Bewertung der Eingabe- und Ausgabedaten bleibt der Anwender verantwortlich. Die Bereitstellung erfolgt zu den Bedingungen der Lizenz [Apache-2.0](https://www.apache.org/licenses/LICENSE-2.0), die einen Gewährleistungs- und Haftungsausschluss enthält.
