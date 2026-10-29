#!/usr/bin/env bash
# Builds every Linux release format of Frosty Mod Manager (with the frosty CLI) into packaging/linux/dist:
#   tar.gz, tar.xz, AppImage, deb, rpm and pkg.tar.zst
# Usage: packaging/linux/build.sh <version>   e.g. 1.0.0 or 1.1.0-beta.1
set -euo pipefail

VERSION="${1:?usage: build.sh <version>}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
ROOT="$(cd "$HERE/../.." && pwd -P)"
WORK="$HERE/.work"
DIST="$HERE/dist"
TOOLS="$HERE/.tools"
NAME="frosty-mod-manager"
# every file is named <name>-<version>-x86_64.<format>
BASENAME="$NAME-$VERSION-x86_64"

NFPM_VERSION="2.48.0"
APPIMAGETOOL_VERSION="1.9.1"

rm -rf "$WORK" "$DIST"
mkdir -p "$WORK" "$DIST" "$TOOLS"

# --- tools ------------------------------------------------------------------------------------------
if [[ ! -x "$TOOLS/nfpm" ]]; then
    curl -fsSL "https://github.com/goreleaser/nfpm/releases/download/v$NFPM_VERSION/nfpm_${NFPM_VERSION}_Linux_x86_64.tar.gz" \
        | tar -xz -C "$TOOLS" nfpm
fi
if [[ ! -x "$TOOLS/appimagetool" ]]; then
    curl -fsSL -o "$TOOLS/appimagetool" \
        "https://github.com/AppImage/appimagetool/releases/download/$APPIMAGETOOL_VERSION/appimagetool-x86_64.AppImage"
    chmod +x "$TOOLS/appimagetool"
fi

# --- publish both apps into one folder (they share the profiles, meta and native libs) ------------
STAGE="$WORK/$BASENAME"
for project in FrostyModManager FrostyCli; do
    dotnet publish "$ROOT/$project" -c Release -r linux-x64 --self-contained \
        -p:DebugType=None -p:DebugSymbols=false -p:Version="$VERSION" -o "$STAGE"
done
# windows builds of the native compression libs are useless here, bcrypt.dll is the wine shim and stays
find "$STAGE/ThirdParty" -name 'lib*.dll' -delete

# --- tar.gz / tar.xz ----------------------------------------------------------------------------------
TARDIR="$WORK/tar/$BASENAME"
mkdir -p "$TARDIR"
cp -r "$STAGE/." "$TARDIR/"
cp "$HERE/install.sh" "$HERE/$NAME.desktop" "$ROOT/LICENSE" "$TARDIR/"
cp "$HERE/icons/256.png" "$TARDIR/$NAME.png"
tar -C "$WORK/tar" -czf "$DIST/$BASENAME.tar.gz" "$BASENAME"
tar -C "$WORK/tar" -cJf "$DIST/$BASENAME.tar.xz" "$BASENAME"

# --- AppImage ---------------------------------------------------------------------------------------
APPDIR="$WORK/AppDir"
mkdir -p "$APPDIR/usr/lib/$NAME" "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/256x256/apps"
cp -r "$STAGE/." "$APPDIR/usr/lib/$NAME/"
cp "$HERE/AppRun" "$APPDIR/AppRun"
cp "$HERE/$NAME.desktop" "$APPDIR/$NAME.desktop"
cp "$HERE/$NAME.desktop" "$APPDIR/usr/share/applications/$NAME.desktop"
cp "$HERE/icons/256.png" "$APPDIR/$NAME.png"
cp "$HERE/icons/256.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/$NAME.png"
ln -s "$NAME.png" "$APPDIR/.DirIcon"
# --appimage-extract-and-run so the tool works without FUSE (containers, CI)
ARCH=x86_64 VERSION="$VERSION" "$TOOLS/appimagetool" --appimage-extract-and-run --no-appstream \
    "$APPDIR" "$DIST/$BASENAME.AppImage"

# --- deb / rpm / pkg.tar.zst ------------------------------------------------------------------------
WRAPPERS="$WORK/wrappers"
mkdir -p "$WRAPPERS"
sed "s/@BINARY@/FrostyModManager/" "$HERE/launcher.sh" > "$WRAPPERS/frosty-mod-manager"
sed "s/@BINARY@/FrostyCli/" "$HERE/launcher.sh" > "$WRAPPERS/frosty"
chmod +x "$WRAPPERS"/*

# nfpm only expands variables in a few fields, so fill the paths in ourselves
sed -e "s|\${VERSION}|$VERSION|g" -e "s|\${STAGE}|$STAGE|g" -e "s|\${WRAPPERS}|$WRAPPERS|g" \
    "$HERE/nfpm.yaml" > "$WORK/nfpm.yaml"
cd "$HERE"
# the package managers read name and version from the metadata, so the file names can match the other formats
for format in deb:deb rpm:rpm archlinux:pkg.tar.zst; do
    "$TOOLS/nfpm" package -f "$WORK/nfpm.yaml" -p "${format%%:*}" -t "$DIST/$BASENAME.${format#*:}"
done

(cd "$DIST" && sha256sum -- * > SHA256SUMS)
ls -lh "$DIST"
