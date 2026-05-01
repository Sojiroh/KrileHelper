#!/usr/bin/env bash
# Build a self-contained, single-file Linux binary into ./dist/
#
# Usage: scripts/publish.sh [runtime]
#   runtime defaults to linux-x64. Pass linux-arm64 for ARM.

set -euo pipefail

cd "$(dirname "$0")/.."

RID="${1:-linux-x64}"
CONFIG="${CONFIG:-Release}"
OUT="dist/${RID}"

echo "==> Publishing KrileHelper.UI for ${RID} (${CONFIG})"
rm -rf "${OUT}"

dotnet publish KrileHelper.UI/KrileHelper.UI.csproj \
    -c "${CONFIG}" \
    -r "${RID}" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:DebugType=embedded \
    -p:DebugSymbols=true \
    -o "${OUT}"

# Strip secondary files; the single-file binary already contains everything.
find "${OUT}" -maxdepth 1 -type f ! -name 'KrileHelper.UI' -delete
mv "${OUT}/KrileHelper.UI" "${OUT}/krile-helper"
chmod +x "${OUT}/krile-helper"

# Copy resources used by the installer.
cp packaging/icon.png "${OUT}/icon.png"
cp packaging/krile-helper.desktop.in "${OUT}/krile-helper.desktop.in"
cp scripts/install.sh "${OUT}/install.sh"
cp scripts/uninstall.sh "${OUT}/uninstall.sh"
chmod +x "${OUT}/install.sh" "${OUT}/uninstall.sh"

SIZE=$(du -h "${OUT}/krile-helper" | cut -f1)
echo
echo "==> Published to ${OUT}/  (binary: ${SIZE})"
echo "    To install for the current user:"
echo "      cd ${OUT} && ./install.sh"
