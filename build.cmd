@echo off
rem Builds the single-file CanEmAllEditor.exe into the publish folder.
cd /d "%~dp0"
dotnet publish src\CanEmAllEditor -c Release -o publish || exit /b 1
echo.
echo Done: publish\CanEmAllEditor.exe
