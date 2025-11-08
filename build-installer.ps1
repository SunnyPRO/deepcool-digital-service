# Build script for Deepcool Digital service and WiX MSI
# Usage: run in PowerShell on Windows with Visual Studio + WiX Toolset installed.
# Optional parameters: -Configuration Release -Version 1.2.3
param(
  [string]$Configuration = 'Release',
  [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'

Write-Host "Building service ($Configuration)..." -ForegroundColor Cyan
msbuild DeepcoolService.sln /p:Configuration=$Configuration /t:Build

# Verify service binary exists
$serviceExe = Join-Path -Path (Resolve-Path 'DeepcoolService/bin/'+$Configuration) -ChildPath 'Deepcool Digital.exe'
if(!(Test-Path $serviceExe)){ throw "Service executable not found: $serviceExe" }

Write-Host "Building WiX MSI (Version=$Version)..." -ForegroundColor Cyan
# Optionally inject version into Product.wxs via light/candle preprocess define (simpler: pass property to msbuild and reference variable in .wxs)
msbuild InstallerWiX/InstallerWiX.wixproj /p:Configuration=$Configuration /p:ProductVersion=$Version

$msi = "InstallerWiX/bin/$Configuration/DeepcoolServiceInstaller.msi"
if(Test-Path $msi){
  Write-Host "MSI built: $msi" -ForegroundColor Green
} else {
  throw "MSI not found at expected path: $msi"
}

Write-Host "Done." -ForegroundColor Green
