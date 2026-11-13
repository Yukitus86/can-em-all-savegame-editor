#!/bin/sh
# Builds the Linux editor into publish/linux/CanEmAllEditor (needs the .NET 10 SDK).
set -e
cd "$(dirname "$0")"
dotnet publish src/CanEmAllEditor.Linux -c Release -r linux-x64 -o publish/linux
chmod +x publish/linux/CanEmAllEditor
echo "Done: publish/linux/CanEmAllEditor"
