@echo off
setlocal

set "DIR=%~dp0"
set "EXE=%DIR%csv2zugferd-win-x64.exe"
set "CONFIG=%DIR%config_demo.yml"
set "OUTPUT=%DIR%output"

if not exist "%EXE%" (
    echo Fehler: csv2zugferd-win-x64.exe nicht gefunden.
    echo Bitte alle Dateien aus dem ZIP-Archiv in denselben Ordner entpacken.
    pause
    exit /b 1
)

if not exist "%CONFIG%" (
    echo Fehler: config_demo.yml nicht gefunden.
    pause
    exit /b 1
)

if not exist "%OUTPUT%" mkdir "%OUTPUT%"

call :run_demo demo-2026-0001
if errorlevel 1 goto :error

call :run_demo demo-2026-0002
if errorlevel 1 goto :error

echo.
echo ZUGFeRD-Demo-Dateien erstellt. Ergebnisse im Ordner: %OUTPUT%
pause
exit /b 0

:run_demo
set "BASE=%~1"
set "CSV=%DIR%%BASE%.csv"
set "PDF=%DIR%%BASE%.pdf"
if not exist "%CSV%" (
    echo Fehler: %CSV% nicht gefunden.
    exit /b 1
)
if not exist "%PDF%" (
    echo Fehler: %PDF% nicht gefunden.
    exit /b 1
)
echo Verarbeite %BASE% ...
"%EXE%" --csv "%CSV%" --pdf "%PDF%" --config "%CONFIG%" --output "%OUTPUT%"
exit /b %ERRORLEVEL%

:error
echo.
echo Erzeugung der ZUGFeRD-Demo-Dateien fehlgeschlagen.
pause
exit /b 1
