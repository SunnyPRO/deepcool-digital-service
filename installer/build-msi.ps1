# Wrapper script: builds service and WiX MSI (requires WiX + MSBuild on Windows)
param(
  [string]$Configuration = 'Release',
  [string]$Version = '1.0.0'
)
$ErrorActionPreference='Stop'
Write-Host "Building service ($Configuration)" -ForegroundColor Cyan
msbuild DeepcoolService.sln /p:Configuration=$Configuration /t:Build

$serviceExe = "DeepcoolService/bin/$Configuration/Deepcool Digital.exe"
if(!(Test-Path $serviceExe)){ throw "Service exe not found: $serviceExe" }
Write-Host "Building WiX MSI (Version=$Version)" -ForegroundColor Cyan
msbuild installer/InstallerWiX/InstallerWiX.wixproj /p:Configuration=$Configuration /p:ProductVersion=$Version

$msi = "installer/InstallerWiX/bin/$Configuration/DeepcoolServiceInstaller.msi"
if(Test-Path $msi){ Write-Host "MSI built: $msi" -ForegroundColor Green } else { throw "MSI missing: $msi" }
