#!/bin/bash
set -euo pipefail

VERSION="${1:-0.0.0}"

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RELEASE="$ROOT/BUILD/release"
FINAL="$RELEASE/final"

# 1. Clean (remove dirs, then recreate FINAL — tar below needs it to exist)
rm -rf "$RELEASE/macos-arm64"
rm -rf "$FINAL"
mkdir -p "$FINAL"

# 2. Publish macOS ARM64 self-contained
dotnet publish "$ROOT/src/SafeDiskCleaner.Avalonia" \
    -c Release -r osx-arm64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:Version="$VERSION" \
    -p:AssemblyVersion="$VERSION.0" \
    -p:FileVersion="$VERSION.0" \
    -o "$RELEASE/macos-arm64"

# 3. Package as tar.gz
tar -czf "$FINAL/SafeDiskCleaner-$VERSION-macos-arm64.tar.gz" -C "$RELEASE/macos-arm64" .

echo ""
echo "Artifacts ready:"
ls -lh "$FINAL"
