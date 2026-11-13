@echo off
rem Builds both editors:
rem   publish\CanEmAllEditor.exe          Windows (WPF)
rem   publish\linux\CanEmAllEditor        Linux x64 (Avalonia)
cd /d "%~dp0"
dotnet publish src\CanEmAllEditor -c Release -o publish || exit /b 1
dotnet publish src\CanEmAllEditor.Linux -c Release -r linux-x64 -o publish\linux || exit /b 1
echo.
echo Done: publish\CanEmAllEditor.exe and publish\linux\CanEmAllEditor
