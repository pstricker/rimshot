#!/usr/bin/env bash
set -euo pipefail

# Build a Rimshot.app bundle and wrap it in a .dmg for the given macOS RID.
#
# Usage: macos-bundle.sh <rid> <arch-label> <version> <out-dir>
#   rid         : dotnet runtime identifier (osx-arm64 | osx-x64)
#   arch-label  : short label used in the dmg filename (arm64 | x64)
#   version     : version string, e.g. 1.0.0 (used in Info.plist + dmg filename)
#   out-dir     : directory to write the final .dmg into

RID="$1"
ARCH="$2"
VERSION="$3"
OUT_DIR="$4"

WORK_DIR="$(mktemp -d)"
APP_DIR="$WORK_DIR/Rimshot.app"
MACOS_DIR="$APP_DIR/Contents/MacOS"
RESOURCES_DIR="$APP_DIR/Contents/Resources"

mkdir -p "$MACOS_DIR" "$RESOURCES_DIR" "$OUT_DIR"

dotnet publish Rimshot/Rimshot.csproj \
  -c Release -r "$RID" --self-contained true \
  -o "$MACOS_DIR" \
  -p:Version="$VERSION"

dotnet publish Rimshot.Inspector/Rimshot.Inspector.csproj \
  -c Release -r "$RID" --self-contained true \
  -o "$MACOS_DIR/Inspector" \
  -p:Version="$VERSION"

cat > "$APP_DIR/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>Rimshot</string>
    <key>CFBundleDisplayName</key>
    <string>Rimshot</string>
    <key>CFBundleIdentifier</key>
    <string>com.pstricker.rimshot</string>
    <key>CFBundleVersion</key>
    <string>$VERSION</string>
    <key>CFBundleShortVersionString</key>
    <string>$VERSION</string>
    <key>CFBundleExecutable</key>
    <string>Rimshot</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
PLIST

chmod +x "$MACOS_DIR/Rimshot"
chmod +x "$MACOS_DIR/Inspector/Rimshot.Inspector"

# Ad-hoc sign the bundle. Without this, the inner Rimshot binary is signed by
# `dotnet publish` but the bundle has no _CodeSignature manifest — Gatekeeper
# treats this mismatch as "damaged and can't be opened" with no UI bypass.
# Ad-hoc signing produces an internally consistent bundle; macOS still warns
# on first launch (no Apple Dev ID), but right-click → Open works.
codesign --force --deep --sign - "$APP_DIR"

DMG_PATH="$OUT_DIR/Rimshot-macos-$ARCH.dmg"
rm -f "$DMG_PATH"

hdiutil create \
  -volname "Rimshot" \
  -srcfolder "$APP_DIR" \
  -ov -format UDZO \
  "$DMG_PATH"

echo "Built $DMG_PATH"
