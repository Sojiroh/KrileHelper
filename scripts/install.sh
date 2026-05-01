#!/usr/bin/env bash
# Install Krile Helper to ~/.local for the current user.
#
# Drops:
#   ~/.local/bin/krile-helper
#   ~/.local/share/icons/hicolor/256x256/apps/krile-helper.png
#   ~/.local/share/applications/krile-helper.desktop

set -euo pipefail

cd "$(dirname "$0")"

if [[ ! -x ./krile-helper ]]; then
    echo "error: ./krile-helper not found. Run scripts/publish.sh first or run this from the dist directory." >&2
    exit 1
fi

BIN_DIR="${XDG_BIN_HOME:-$HOME/.local/bin}"
APP_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
ICON_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/256x256/apps"

mkdir -p "${BIN_DIR}" "${APP_DIR}" "${ICON_DIR}"

install -m 755 ./krile-helper "${BIN_DIR}/krile-helper"
install -m 644 ./icon.png      "${ICON_DIR}/krile-helper.png"

# Substitute the absolute binary path into the .desktop template.
sed "s|@BINARY@|${BIN_DIR}/krile-helper|g" \
    ./krile-helper.desktop.in > "${APP_DIR}/krile-helper.desktop"
chmod 644 "${APP_DIR}/krile-helper.desktop"

# Refresh menu caches if the tools are present (best-effort).
command -v update-desktop-database >/dev/null && update-desktop-database "${APP_DIR}" 2>/dev/null || true
command -v gtk-update-icon-cache   >/dev/null && gtk-update-icon-cache -f "${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor" 2>/dev/null || true

echo
echo "Installed:"
echo "  ${BIN_DIR}/krile-helper"
echo "  ${ICON_DIR}/krile-helper.png"
echo "  ${APP_DIR}/krile-helper.desktop"
echo
case ":${PATH}:" in
    *":${BIN_DIR}:"*) ;;
    *) echo "warning: ${BIN_DIR} is not in your PATH. Add this to your shell rc:"
       echo "         export PATH=\"${BIN_DIR}:\$PATH\"" ;;
esac
echo "Launch from your application menu (\"Krile Helper\") or run:  krile-helper"
