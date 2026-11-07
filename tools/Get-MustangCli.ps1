<#
.SYNOPSIS
    Lädt das Mustang-CLI-Jar für die externe XML-Validierung herunter.

.DESCRIPTION
    Die Tests der Kategorie "ExternalValidation" (MustangValidationTests) prüfen die erzeugten
    ZUGFeRD-XML-Dateien mit dem Mustang-CLI (https://www.mustangproject.org/commandline/).
    Dieses Skript legt das Jar unter tools/Mustang-CLI.jar ab. Die Datei ist in .gitignore ausgeschlossen.

    Voraussetzung: Java 17 oder neuer im PATH.

.PARAMETER Version
    Mustang-CLI-Version, Standard 2.26.0.

.EXAMPLE
    .\tools\Get-MustangCli.ps1
    dotnet test --filter Category=ExternalValidation
#>
param(
    [string]$Version = "2.26.0"
)

$ErrorActionPreference = "Stop"

$target = Join-Path $PSScriptRoot "Mustang-CLI.jar"
$url = "https://www.mustangproject.org/deploy/Mustang-CLI-$Version.jar"

if (Test-Path $target) {
    Write-Host "Mustang-CLI ist bereits vorhanden: $target"
    exit 0
}

Write-Host "Lade Mustang-CLI $Version von $url ..."
Invoke-WebRequest -Uri $url -OutFile $target -UseBasicParsing

$size = [math]::Round((Get-Item $target).Length / 1MB, 1)
Write-Host "Gespeichert: $target ($size MB)"
Write-Host "Validierung starten: dotnet test --filter Category=ExternalValidation"
