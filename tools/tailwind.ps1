# Builds src/Akusiap.Web/wwwroot/css/app.css with the Tailwind standalone CLI (no Node needed).
# Usage: pwsh tools/tailwind.ps1 [-Watch]
param([switch]$Watch)
$ErrorActionPreference = 'Stop'
$Version = 'v4.3.3'
$Root = Split-Path -Parent $PSScriptRoot
$BinDir = Join-Path $Root 'tools/bin'
$Bin = Join-Path $BinDir "$Version-tailwindcss-windows-x64.exe"
if (-not (Test-Path $Bin)) {
    New-Item -ItemType Directory -Force $BinDir | Out-Null
    Invoke-WebRequest -Uri "https://github.com/tailwindlabs/tailwindcss/releases/download/$Version/tailwindcss-windows-x64.exe" -OutFile $Bin
}
Push-Location (Join-Path $Root 'src/Akusiap.Web')
try {
    if ($Watch) { & $Bin -i Styles/app.css -o wwwroot/css/app.css --watch }
    else { & $Bin -i Styles/app.css -o wwwroot/css/app.css --minify }
} finally { Pop-Location }
