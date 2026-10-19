#!/usr/bin/env bash
# Installs the Playwright browsers the E2E tests use (Chromium by default).
# Usage: tools/playwright-install.sh [extra args, e.g. --with-deps on Linux CI]
set -euo pipefail
cd "$(dirname "$0")/.."
if command -v pwsh >/dev/null 2>&1; then
  pwsh -NoProfile -File tools/playwright-install.ps1 "$@"
else
  # Windows without PowerShell 7 (Git Bash): use the built-in Windows PowerShell.
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/playwright-install.ps1 "$@"
fi
