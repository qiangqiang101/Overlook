#!/usr/bin/env bash
# Usage: build-deb.sh <publish dir> <deb arch: amd64|arm64|armhf> <version> <output .deb>
set -euo pipefail
publish=$(realpath "$1"); arch=$2; version=$3; out=$(realpath -m "$4")
here=$(cd "$(dirname "$0")" && pwd)
root=$(mktemp -d)/overlook

install -d "$root/DEBIAN" "$root/opt/overlook" "$root/usr/bin" "$root/usr/share/applications" "$root/usr/share/icons/hicolor/256x256/apps"
cp -a "$publish/." "$root/opt/overlook/"
chmod 755 "$root/opt/overlook/Overlook"
ln -s /opt/overlook/Overlook "$root/usr/bin/overlook"
install -m 644 "$here/overlook.desktop" "$root/usr/share/applications/overlook.desktop"
install -m 644 "$here/../src/BigBro.App/Assets/app.png" "$root/usr/share/icons/hicolor/256x256/apps/overlook.png"

cat > "$root/DEBIAN/control" <<CONTROL
Package: overlook
Version: $version
Section: utils
Priority: optional
Architecture: $arch
Maintainer: Overlook <noreply@users.noreply.github.com>
Installed-Size: $(du -sk "$root/opt" | cut -f1)
Depends: libfontconfig1, libx11-6, libice6, libsm6
Recommends: pulseaudio-utils
Description: Sensor panel for LibreHardwareMonitor
 Overlook shows LibreHardwareMonitor sensors (read over the network) on a
 themeable panel, with RGB lighting backgrounds, an audio visualizer and a
 theme editor.
CONTROL

mkdir -p "$(dirname "$out")"
dpkg-deb --build --root-owner-group "$root" "$out"
