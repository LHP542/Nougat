#!/usr/bin/env bash
# Baut das AppImage aus einem fertigen linux-x64-Publish-Ordner (Kroste-Standard).
# Aufruf: packaging/linux/build-appimage.sh <version> <publish-dir>
# Hinweis: --appimage-extract-and-run ist nötig, weil im CI kein FUSE verfügbar ist.
set -euo pipefail

VERSION="$1"
PUBLISH_DIR="$2"
APPDIR="AppDir"

rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin"
cp -r "$PUBLISH_DIR"/* "$APPDIR/usr/bin/"
cp packaging/linux/nougat.desktop "$APPDIR/"
cp Nougat/Assets/nougat.png "$APPDIR/"
cp packaging/linux/AppRun "$APPDIR/AppRun"
chmod +x "$APPDIR/AppRun" "$APPDIR/usr/bin/Nougat"

curl -sSL -o appimagetool \
  https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x appimagetool
./appimagetool --appimage-extract-and-run "$APPDIR" "Nougat-${VERSION}-x86_64.AppImage"
echo "AppImage gebaut: Nougat-${VERSION}-x86_64.AppImage"
