#!/usr/bin/env bash
# Remove every file install.sh dropped on disk for the current user.

set -euo pipefail

BIN_DIR="${XDG_BIN_HOME:-$HOME/.local/bin}"
APP_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
ICON_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/256x256/apps"

removed=0
for f in \
    "${BIN_DIR}/krile-helper" \
    "${ICON_DIR}/krile-helper.png" \
    "${APP_DIR}/krile-helper.desktop"
do
    if [[ -f "${f}" ]]; then
        rm -f "${f}"
        echo "removed ${f}"
        removed=$((removed+1))
    fi
done

command -v update-desktop-database >/dev/null && update-desktop-database "${APP_DIR}" 2>/dev/null || true
command -v gtk-update-icon-cache   >/dev/null && gtk-update-icon-cache -f "${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor" 2>/dev/null || true

echo
echo "Uninstalled ${removed} file(s)."
echo "Settings at ~/.config/krile-helper/ and signature cache at ~/.cache/sharlayan-core/ were left in place."
echo "Remove them manually if you want a fully clean slate."
