# Installs the Playwright browsers the E2E tests use (Chromium by default).
# Usage: powershell -ExecutionPolicy Bypass -File tools/playwright-install.ps1 [extra args, e.g. --with-deps on Linux CI]
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet build (Join-Path $root 'tests/Akusiap.E2E') --nologo -v quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& (Join-Path $root 'tests/Akusiap.E2E/bin/Debug/net10.0/playwright.ps1') install chromium @args
exit $LASTEXITCODE
