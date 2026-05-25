csv2zugferd Demo-Paket
======================

Dieses Paket enthaelt Beispiel-Rechnungen und ein Startskript zum
Ausprobieren von csv2zugferd unter Windows.

Inhalt
------
csv2zugferd-win-x64.exe   Das csv2zugferd-Programm (Windows 64-bit)
config_demo.yml           Beispiel-Konfiguration (Spalten-Mapping)
demo-2026-0001.csv        Beispiel-Rechnung 1 (Rechnungsdaten)
demo-2026-0001.pdf        Beispiel-Rechnung 1 (PDF-Vorlage)
demo-2026-0002.csv        Beispiel-Rechnung 2 (Rechnungsdaten)
demo-2026-0002.pdf        Beispiel-Rechnung 2 (PDF-Vorlage)
create-zugferd-demo.cmd  ZUGFeRD-Demo-Dateien erzeugen

Schnellstart
------------
1. Alle Dateien aus dem ZIP-Archiv in einen Ordner entpacken.
2. create-zugferd-demo.cmd per Doppelklick oder in der Eingabeaufforderung starten.
3. Die erzeugten ZUGFeRD-PDF-Dateien liegen anschliessend im
   Unterordner "output\".

Die PDF-Dateien im Ordner "output\" sind valide ZUGFeRD-2.3-Rechnungen
(Profil Extended) und koennen z. B. mit dem Mustang-Validator oder
VeraPDF geprueft werden.

Weitere Informationen
---------------------
Website:  https://zugferd-tools.de
GitHub:   https://github.com/rillsoft/csv2zugferd
Lizenz:   Apache 2.0
