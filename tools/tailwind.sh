#!/usr/bin/env bash
# Builds src/Akusiap.Web/wwwroot/css/app.css with the Tailwind standalone CLI (no Node needed).
# Usage: tools/tailwind.sh [--watch]
set -euo pipefail
VERSION="v4.3.3"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BIN_DIR="$ROOT/tools/bin"
case "$(uname -s)-$(uname -m)" in
  Linux-x86_64)  ASSET="tailwindcss-linux-x64" ;;
  Linux-aarch64) ASSET="tailwindcss-linux-arm64" ;;
  Darwin-arm64)  ASSET="tailwindcss-macos-arm64" ;;
  Darwin-x86_64) ASSET="tailwindcss-macos-x64" ;;
  MINGW*|MSYS*|CYGWIN*) ASSET="tailwindcss-windows-x64.exe" ;;
  *) echo "Unsupported platform: $(uname -s)-$(uname -m)" >&2; exit 1 ;;
esac
BIN="$BIN_DIR/$VERSION-$ASSET"
if [ ! -f "$BIN" ]; then
  mkdir -p "$BIN_DIR"
  curl -sfL -o "$BIN" "https://github.com/tailwindlabs/tailwindcss/releases/download/$VERSION/$ASSET"
  chmod +x "$BIN"
fi
cd "$ROOT/src/Akusiap.Web"
MODE=(--minify)
if [ "${1:-}" = "--watch" ]; then MODE=(--watch); fi
"$BIN" -i Styles/app.css -o wwwroot/css/app.css "${MODE[@]}"
