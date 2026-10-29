#!/bin/sh
# Installs this folder for the current user: ~/.local/lib/frosty-mod-manager, an app-menu entry,
# and the `frosty-mod-manager` / `frosty` commands in ~/.local/bin. `./install.sh --uninstall` removes them.
# Keys, sdks and mods live in ~/.local/share/frosty and are never touched.
set -e
SRC="$(dirname "$(readlink -f "$0")")"
DATA="${XDG_DATA_HOME:-$HOME/.local/share}"
LIB="$HOME/.local/lib/frosty-mod-manager"
BIN="$HOME/.local/bin"

rm -rf "$LIB" "$BIN/frosty-mod-manager" "$BIN/frosty" \
    "$DATA/applications/frosty-mod-manager.desktop" "$DATA/icons/hicolor/256x256/apps/frosty-mod-manager.png"

if [ "$1" = "--uninstall" ]; then
    echo "Frosty Mod Manager removed. Your mods and keys are still in $DATA/frosty."
    exit 0
fi

mkdir -p "$LIB" "$BIN" "$DATA/applications" "$DATA/icons/hicolor/256x256/apps"
cp -r "$SRC/." "$LIB/"
ln -s "$LIB/FrostyModManager" "$BIN/frosty-mod-manager"
ln -s "$LIB/FrostyCli" "$BIN/frosty"
cp "$SRC/frosty-mod-manager.png" "$DATA/icons/hicolor/256x256/apps/frosty-mod-manager.png"
sed "s|^Exec=.*|Exec=$LIB/FrostyModManager|" "$SRC/frosty-mod-manager.desktop" > "$DATA/applications/frosty-mod-manager.desktop"
gtk-update-icon-cache -q -t "$DATA/icons/hicolor" 2>/dev/null || true
update-desktop-database -q "$DATA/applications" 2>/dev/null || true
echo "Installed. Open \"Frosty Mod Manager\" from your app menu, or run frosty-mod-manager."
