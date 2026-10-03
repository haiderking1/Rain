#!/usr/bin/env bash
# Builds the Rain installer with Velopack.
# Usage: ./pack.sh [version]      (defaults to <Version> in src/Rain/Rain.csproj)
# Needs: .NET 10 SDK, and vpk (dotnet tool install -g vpk)
set -euo pipefail
cd "$(dirname "$0")"

version="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/Rain/Rain.csproj)}"
echo "Packing Rain $version"

rm -rf publish
dotnet publish src/Rain -c Release -r win-x64 --self-contained false -p:Version="$version" -o publish

# Framework-dependent keeps the installer small; Setup installs the .NET 10 Desktop Runtime if it's missing.
vpk pack \
  --packId Rain \
  --packVersion "$version" \
  --packDir publish \
  --mainExe Rain.exe \
  --runtime win-x64 \
  --packTitle Rain \
  --packAuthors haiderking1 \
  --icon src/Rain/Assets/Rain.ico \
  --splashImage assets/logo.png \
  --splashProgressColor "#7AA2F7" \
  --framework net10-x64-desktop \
  --outputDir releases

echo "Installer: releases/Rain-win-Setup.exe"
