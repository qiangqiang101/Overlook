#!/usr/bin/env bash
# Usage: build-appimage.sh <publish dir (linux-x64)> <output .AppImage>
set -euo pipefail
publish=$(realpath "$1"); out=$(realpath -m "$2")
here=$(cd "$(dirname "$0")" && pwd)
work=$(mktemp -d)
appdir="$work/Overlook.AppDir"

install -d "$appdir/usr/lib/overlook"
cp -a "$publish/." "$appdir/usr/lib/overlook/"
chmod 755 "$appdir/usr/lib/overlook/Overlook"
install -m 644 "$here/overlook.desktop" "$appdir/overlook.desktop"
install -m 644 "$here/../src/BigBro.App/Assets/app.png" "$appdir/overlook.png"
cp "$appdir/overlook.png" "$appdir/.DirIcon"
cat > "$appdir/AppRun" <<'RUN'
#!/bin/sh
HERE=$(dirname "$(readlink -f "$0")")
exec "$HERE/usr/lib/overlook/Overlook" "$@"
RUN
chmod 755 "$appdir/AppRun"

tool="$work/appimagetool"
curl -fsSL -o "$tool" https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x "$tool"

mkdir -p "$(dirname "$out")"
ARCH=x86_64 "$tool" --appimage-extract-and-run "$appdir" "$out"
